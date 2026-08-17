namespace Okf.Core.Tests.Search;

/// <summary>
/// The search engine (PRD CORE-11; decisions.md Q7): tokenization, the corpus boundary,
/// field-weighted BM25 ranking and its determinism, the AND→OR fallback, filters, snippets,
/// and the trust/staleness fields a result carries.
/// </summary>
public class OkfSearchTests
{
    [Theory]
    [InlineData("Cost-Optimization, GA4!", "cost optimization ga4")]
    [InlineData("net_amount", "net amount")]
    [InlineData("   ", "")]
    [InlineData("Straße Café", "straße café")]
    [InlineData("ÅNGSTRÖM", "ångström")]
    [InlineData("widget🚀gadget", "widget gadget")]
    [InlineData("2026-08-14", "2026 08 14")]
    public void TokenizationLowercasesAndSplitsOnEverythingThatIsNotALetterOrDigit(string text, string expected)
    {
        // Expectations come from the rule stated in decisions.md Q7, not from the code:
        // lowercase, split on non-alphanumerics, Unicode-aware, no stemming.
        Assert.Equal(
            expected.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            OkfTokenizer.Tokenize(text));
    }

    [Fact]
    public void TokenizationDoesNotStem()
    {
        // Deliberate for v1: `widgets` and `widget` are different terms.
        Assert.Equal(["widgets"], OkfTokenizer.Tokenize("Widgets"));
        Assert.NotEqual(OkfTokenizer.Tokenize("widget"), OkfTokenizer.Tokenize("widgets"));
    }

    [Fact]
    public void AQuerySeparatesFiltersFromTermsCaseInsensitively()
    {
        var query = OkfSearchQuery.Parse("Widget pricing TAG:Catalog type:Playbook tag:widgets");

        Assert.Equal(["widget", "pricing"], query.Terms);
        Assert.Equal(["Playbook"], query.Types);
        Assert.Equal(["Catalog", "widgets"], query.Tags);
        Assert.False(query.IsEmpty);
    }

    [Fact]
    public void AQueryIgnoresBlankAndDuplicateFilters()
    {
        var query = OkfSearchQuery.Parse("tag: type: tag:catalog tag:CATALOG");

        Assert.Equal(["catalog"], query.Tags);
        Assert.Empty(query.Types);

        // Nothing to match and nothing to filter by: the CLI turns this into exit 2.
        Assert.True(OkfSearchQuery.Parse("tag: ").IsEmpty);
        Assert.True(OkfSearchQuery.Parse(null).IsEmpty);
    }

    [Fact]
    public void AQueryRendersCanonicallyWithItsTermsFirst()
    {
        // What `okf search --verbose` prints back as the effective query (PRD CLI-4), so
        // the rendering has to carry every part of it and in one fixed order however the
        // parts were written.
        Assert.Equal(
            "widget pricing type:Playbook tag:Catalog tag:widgets",
            OkfSearchQuery.Parse("tag:Catalog Widget type:Playbook pricing tag:widgets").ToString());
        Assert.Equal(string.Empty, OkfSearchQuery.Parse(null).ToString());
    }

    [Theory]
    // Where each token starts and how long it is, in the original text — what lets a
    // snippet mark the matched words without re-finding them.
    [InlineData("ab cd", "ab@0+2 cd@3+2")]
    // A run of separators opens no token, at the edges or in the middle.
    [InlineData("  ab", "ab@2+2")]
    [InlineData("ab, cd", "ab@0+2 cd@4+2")]
    [InlineData("ab ", "ab@0+2")]
    [InlineData("   ", "")]
    // A token still open when the text runs out is closed at the end of the text.
    [InlineData("ab", "ab@0+2")]
    // Offsets are in UTF-16 code units, so an astral separator advances by the two units
    // it occupies and the token after it is still found where it really is.
    [InlineData("ab\U0001F680cd", "ab@0+2 cd@4+2")]
    public void TokenOffsetsAreWhereTheTokensReallyAre(string text, string expected) =>
        Assert.Equal(
            expected,
            string.Join(
                ' ',
                OkfTokenizer.TokenizeWithOffsets(text).Select(
                    token => $"{token.Token}@{token.Start}+{token.Length}")));

    [Fact]
    public void AUrlInAQueryIsNotMistakenForAFilter()
    {
        var query = OkfSearchQuery.Parse("https://example.invalid/spec");

        Assert.Empty(query.Types);
        Assert.Empty(query.Tags);
        Assert.Contains("example", query.Terms);
    }

    [Fact]
    public void FrontmatterOutranksBodyForTheSameTerm()
    {
        using var bundle = new TempBundle();

        // The two documents carry the same tokens and the same weighted length, so only
        // *where* the term sits can separate them — and the path tiebreak favours the body
        // copy, so an engine that did not weight the title would fail this.
        bundle.Add("z-titled.md", "---\ntype: Reference\ntitle: widget\n---\n\ngamma\n")
            .Add("a-bodied.md", "---\ntype: Reference\ntitle: gamma\n---\n\nwidget\n");

        var results = Search(bundle, "widget").Results;

        Assert.Equal(["z-titled.md", "a-bodied.md"], results.Select(result => result.Path));
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void ABriefConceptOutranksALongOneThatMentionsTheTermJustAsOften()
    {
        using var bundle = new TempBundle();

        // BM25's length normalization (Robertson & Zaragoza, "The Probabilistic Relevance
        // Framework", §3.1 — the ranking function decisions.md Q7 commits to): with the
        // same term frequency the shorter document is the better match, because the term
        // makes up more of it. The expectation is the published formula's, not okf-net's
        // arithmetic. `long.md` sorts before `short.md`, so the path tiebreak would put it
        // first if document length did not enter the score at all.
        bundle.Add("long.md", "---\ntype: Reference\ntitle: Alpha\n---\n\nwidget " + Filler())
            .Add("short.md", "---\ntype: Reference\ntitle: Beta\n---\n\nwidget\n");

        var results = Search(bundle, "widget").Results;

        Assert.Equal(["short.md", "long.md"], results.Select(result => result.Path));
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void TheScoreIsBm25WithTheParametersAndWeightsAd26Fixes()
    {
        using var bundle = new TempBundle();
        bundle.Add("one.md", "---\ntype: Reference\ntitle: Widget\n---\n\nwidget\n")
            .Add("two.md", "---\ntype: Reference\ntitle: Gadget\n---\n\nwidget widget\n");

        var results = Search(bundle, "widget").Results;

        // Worked by hand from AD-26's statement of the ranking function — BM25 with
        // k1 = 1.2, b = 0.75, the non-negative IDF variant, field weights title ×3,
        // `type` ×2, body ×1, and four decimals on output — rather than from okf-net's
        // arithmetic. Ranking tests cannot see a constant factor applied to every term;
        // the number can, which is what makes the published formula worth asserting
        // against rather than paraphrasing.
        //
        //   one.md   f = 3 (title) + 1 (body) = 4,   length = 3 + 2 + 1 = 6
        //   two.md   f = 2 (body),                   length = 3 + 2 + 2 = 7
        //   N = 2, df = 2       idf = ln(1 + 0.5 / 2.5)             = 0.18232156
        //   average length      = 13 / 2                            = 6.5
        //   norm(L)             = 1.2 × (1 − 0.75 + 0.75 × L / 6.5)
        //   score               = idf × f × (1.2 + 1) / (f + norm(L))
        //   one.md              = 0.18232156 × 4 × 2.2 / (4 + 1.13076923) → 0.3127
        //   two.md              = 0.18232156 × 2 × 2.2 / (2 + 1.26923077) → 0.2454
        Assert.Equal(["one.md", "two.md"], results.Select(result => result.Path));
        Assert.Equal(0.3127, results[0].Score, precision: 6);
        Assert.Equal(0.2454, results[1].Score, precision: 6);
    }

    [Fact]
    public void ADescriptionIsIndexedAndOutweighsTheBody()
    {
        using var bundle = new TempBundle();
        bundle.Add("z-described.md", "---\ntype: Reference\ntitle: Gamma\ndescription: widget\n---\n\ndelta\n")
            .Add("a-bodied.md", "---\ntype: Reference\ntitle: Gamma\ndescription: delta\n---\n\nwidget\n");

        var results = Search(bundle, "widget").Results;

        // PRD CORE-11 makes `description` matchable and AD-26 weights it ×2 against the
        // body's ×1. The two documents carry the same tokens and the same weighted length,
        // so only *where* the term sits can separate them — and the path tiebreak favours
        // the body copy, so an engine that never indexed the description would come back
        // with one result and one that indexed it unweighted would come back reversed.
        Assert.Equal(["z-described.md", "a-bodied.md"], results.Select(result => result.Path));
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void TagsAndTypeAreMatchableText()
    {
        using var bundle = new TempBundle();
        bundle.Add("tagged.md", "---\ntype: Playbook\ntitle: Nothing\ntags: [catalog]\n---\n\n# Nothing\n");

        // PRD CORE-11: matching covers `title`, `description`, `tags` and `type`.
        Assert.Single(Search(bundle, "catalog").Results);
        Assert.Single(Search(bundle, "playbook").Results);
    }

    [Fact]
    public void RankingIsDeterministicAndTiesBreakByBundleThenPath()
    {
        using var first = new TempBundle("aaa");
        using var second = new TempBundle("zzz");
        var identical = "---\ntype: Reference\ntitle: Widget\n---\n\nA widget.\n";
        first.Add("b.md", identical).Add("a.md", identical);
        second.Add("a.md", identical);

        var bundles = new[] { second.Bundle, first.Bundle };
        var one = OkfSearchEngine.Search(bundles, OkfSearchQuery.Parse("widget"), Options());
        var two = OkfSearchEngine.Search(bundles, OkfSearchQuery.Parse("widget"), Options());

        // Identical documents score identically, so only the tiebreak decides the order:
        // bundle root first, then bundle-relative path — never the order they were walked,
        // which put `zzz` ahead of `aaa`.
        Assert.Equal(3, one.Results.Count);
        Assert.Equal(
            new[] { first.Root + "|a.md", first.Root + "|b.md", second.Root + "|a.md" }
                .Order(StringComparer.Ordinal),
            one.Results.Select(result => result.Bundle + "|" + result.Path));
        Assert.Equal(one.Results[0].Score, one.Results[2].Score);
        Assert.Equal(
            one.Results.Select(result => (result.AbsolutePath, result.Score, result.Snippet)),
            two.Results.Select(result => (result.AbsolutePath, result.Score, result.Snippet)));
    }

    [Fact]
    public void TermsAreAndedByDefault()
    {
        using var bundle = new TempBundle();
        bundle.Add("both.md", Concept("Reference", "Widget pricing", "The price of a widget."))
            .Add("one.md", Concept("Reference", "Widget", "A widget."));

        var outcome = Search(bundle, "widget pricing");

        Assert.Equal("both.md", Assert.Single(outcome.Results).Path);
        Assert.Equal(OkfSearchMatchMode.All, outcome.MatchMode);
        Assert.False(outcome.UsedFallback);
    }

    [Fact]
    public void AndFallsBackToOrWhenNothingMatchesEveryTerm()
    {
        using var bundle = new TempBundle();
        bundle.Add("widget.md", Concept("Reference", "Widget", "A widget."))
            .Add("gadget.md", Concept("Reference", "Gadget", "A gadget."));

        var outcome = Search(bundle, "widget gadget");

        Assert.Equal(OkfSearchMatchMode.Any, outcome.MatchMode);
        Assert.True(outcome.UsedFallback);
        Assert.Equal(["gadget.md", "widget.md"], outcome.Results.Select(result => result.Path).Order());
    }

    [Fact]
    public void ASingleTermThatMatchesNothingIsNotAFallback()
    {
        using var bundle = new TempBundle();
        bundle.Add("widget.md", Concept("Reference", "Widget", "A widget."));

        var outcome = Search(bundle, "phlogiston");

        Assert.Empty(outcome.Results);
        Assert.Equal(OkfSearchMatchMode.All, outcome.MatchMode);
        Assert.False(outcome.UsedFallback);
    }

    [Fact]
    public void SeveralTermsThatMatchNothingAreStillNotAFallback()
    {
        using var bundle = new TempBundle();
        bundle.Add("widget.md", Concept("Reference", "Widget", "A widget."));

        var outcome = Search(bundle, "phlogiston caloric");

        // The OR pass ran and came back empty too, so nothing was widened and the outcome
        // must not claim it was — a command that printed "no concept matched every term,
        // showing any" over an empty result would be describing a search that never
        // happened.
        Assert.Empty(outcome.Results);
        Assert.Equal(OkfSearchMatchMode.All, outcome.MatchMode);
        Assert.False(outcome.UsedFallback);
    }

    [Fact]
    public void FiltersRestrictCandidatesWithoutChangingHowSurvivorsScore()
    {
        using var bundle = new TempBundle();
        bundle.Add("guide.md", "---\ntype: Guide\ntitle: Widget guide\ntags: [catalog, widgets]\n---\n\nA widget.\n")
            .Add("play.md", "---\ntype: Playbook\ntitle: Widget playbook\ntags: [catalog]\n---\n\nA widget.\n");

        var unfiltered = Search(bundle, "widget");
        var filtered = Search(bundle, "widget type:guide");

        Assert.Equal(["guide.md"], filtered.Results.Select(result => result.Path));

        // Collection statistics are taken over the whole corpus, so filtering changes
        // which concepts come back but never how the survivors rank (decisions.md Q7).
        Assert.Equal(
            unfiltered.Results.Single(result => result.Path == "guide.md").Score,
            filtered.Results.Single().Score);
        Assert.Equal(2, filtered.ConceptCount);
    }

    [Fact]
    public void RepeatedTypeFiltersAreOredAndRepeatedTagFiltersAreAnded()
    {
        using var bundle = new TempBundle();
        bundle.Add("guide.md", "---\ntype: Guide\ntitle: Widget guide\ntags: [catalog, widgets]\n---\n\nA widget.\n")
            .Add("play.md", "---\ntype: Playbook\ntitle: Widget playbook\ntags: [catalog]\n---\n\nA widget.\n");

        // A concept has one `type`, so AND-ing two would always return nothing; it has many
        // tags, so AND-ing two narrows (decisions.md Q7).
        Assert.Equal(2, Search(bundle, "widget type:Guide type:playbook").Results.Count);
        Assert.Equal(2, Search(bundle, "widget tag:catalog").Results.Count);
        Assert.Equal(["guide.md"], Search(bundle, "widget tag:catalog tag:WIDGETS").Results.Select(r => r.Path));
    }

    [Fact]
    public void AFilterOnlyQueryListsItsCandidatesUnscored()
    {
        using var bundle = new TempBundle();
        bundle.Add("guide.md", "---\ntype: Guide\ntitle: A\ntags: [catalog]\n---\n\nBody.\n")
            .Add("play.md", "---\ntype: Playbook\ntitle: B\n---\n\nBody.\n");

        var outcome = Search(bundle, "tag:catalog");

        Assert.Equal(OkfSearchMatchMode.Filter, outcome.MatchMode);
        Assert.Equal(0, Assert.Single(outcome.Results).Score);
    }

    [Fact]
    public void AnEmptyQueryIsNotASynonymForEverything()
    {
        using var bundle = new TempBundle();
        bundle.Add("one.md", Concept("Reference", "One", "One."));

        var outcome = Search(bundle, string.Empty);

        Assert.Empty(outcome.Results);
        Assert.Equal(0, outcome.ConceptCount);
    }

    [Fact]
    public void ReservedFilesAreNotCorpusEntriesButEveryOtherMarkdownFileIs()
    {
        using var bundle = new TempBundle();
        bundle.Add("index.md", "# Bundle\n\n* [Phlogiston](one.md) - phlogiston\n")
            .Add("log.md", "# Log\n\n## 2026-01-01\n\nPhlogiston.\n")
            .Add("one.md", Concept("Reference", "One", "One."))
            .Add("notes/about.md", Concept("Guide", "About", "About the notes."))
            .Add("notes/deep/two.md", Concept("Reference", "Two", "Two."));

        // An index is a *view* of the concepts, so indexing it would rank navigation above
        // content and double-count every title (decisions.md Q7).
        Assert.Empty(Search(bundle, "phlogiston").Results);

        // And nothing else is skipped: every non-reserved `.md` in the tree, at any depth,
        // is a corpus entry.
        Assert.Equal(3, Search(bundle, "one").ConceptCount);
        Assert.Equal(
            ["notes/about.md", "notes/deep/two.md", "one.md"],
            Search(bundle, "one two about").Results.Select(result => result.Path).Order());
    }

    [Fact]
    public void AConceptWhoseFrontmatterDoesNotParseIsSkippedAndCounted()
    {
        using var bundle = new TempBundle();
        bundle.Add("good.md", Concept("Reference", "Widget", "A widget."))
            .Add("bad.md", "---\ntitle: Widget\n  bad: [unclosed\n---\n\nA widget.\n");

        var outcome = Search(bundle, "widget");

        Assert.Equal("good.md", Assert.Single(outcome.Results).Path);
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Equal(1, outcome.ConceptCount);
    }

    [Fact]
    public void ASnippetIsABoundedBodyWindowAroundTheMatchWithTheTermsMarked()
    {
        using var bundle = new TempBundle();
        var body = string.Join(' ', Enumerable.Repeat("padding", 60)) +
            " the ladder is where pricing lives " +
            string.Join(' ', Enumerable.Repeat("padding", 60));
        bundle.Add("one.md", "---\ntype: Reference\ntitle: One\n---\n\n" + body + "\n");

        var snippet = Assert.Single(Search(bundle, "ladder").Results).Snippet;

        Assert.Contains("**ladder**", snippet, StringComparison.Ordinal);
        Assert.StartsWith("…", snippet, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet, StringComparison.Ordinal);

        // Bounded and cut at word boundaries: the four marking asterisks and the two
        // ellipses are the only characters beyond the window.
        Assert.True(snippet.Length <= OkfSearchEngine.SnippetLength + 6, snippet);
        Assert.DoesNotContain("paddin ", snippet, StringComparison.Ordinal);
        Assert.DoesNotContain(" adding", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnippetPrefersTheWindowCoveringTheMostTerms()
    {
        using var bundle = new TempBundle();
        var body = "alpha mentioned alone here. " +
            string.Join(' ', Enumerable.Repeat("padding", 60)) +
            " alpha and beta together. ";
        bundle.Add("one.md", "---\ntype: Reference\ntitle: One\n---\n\n" + body + "\n");

        var snippet = Assert.Single(Search(bundle, "alpha beta").Results).Snippet;

        Assert.Contains("**alpha** and **beta**", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnippetAnchorsOnTheFirstWindowWhenTwoCoverAsManyTerms()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Reference\ntitle: One\n---\n\nalpha beta at the front. "
            + string.Join(' ', Enumerable.Repeat("pad", 40))
            + " alpha beta at the back.\n");

        var snippet = Assert.Single(Search(bundle, "alpha beta").Results).Snippet;

        // Both ends of the body carry a window covering the same two distinct terms. The
        // rule is "the most distinct terms, earliest first on a tie", so the front window
        // wins and the snippet opens at offset 0 with no leading ellipsis — a later window
        // would be at least as good only if the tie broke the other way.
        Assert.StartsWith("**alpha** **beta** at the front.", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnippetWindowStartsAtAWordAndKeepsItsLeadIn()
    {
        using var bundle = new TempBundle();
        bundle.Add("head.md", "---\ntype: Reference\ntitle: Head\n---\n\n— widget at the head.\n")
            .Add(
                "snap.md",
                "---\ntype: Reference\ntitle: Snap\n---\n\n"
                + string.Join(' ', Enumerable.Repeat("abc", 20)) + " widget tail\n")
            .Add(
                "mid.md",
                "---\ntype: Reference\ntitle: Mid\n---\n\n"
                + string.Join(' ', Enumerable.Repeat("alpha", 20)) + " widget tail\n");

        var snippets = Search(bundle, "widget").Results.ToDictionary(
            result => result.Path,
            result => result.Snippet,
            StringComparer.Ordinal);

        // The window keeps 32 characters of lead-in before the match, and never at the cost
        // of half a word: the raw offset snaps *forward* to the next token boundary, and
        // only forward, so the lead-in can shrink but the window never opens mid-word and
        // never swallows the lead-in altogether.
        //
        //   head.md  the match sits 2 characters in, so there is no lead-in to trim and the
        //            window opens at the very start — no ellipsis.
        //   snap.md  three-letter words: the raw offset lands exactly on a word start, and
        //            that word is kept rather than skipped.
        //   mid.md   five-letter words: the raw offset lands inside a word, which is given
        //            up in full.
        Assert.Equal("— **widget** at the head.", snippets["head.md"]);
        Assert.Equal(
            "…" + string.Join(' ', Enumerable.Repeat("abc", 8)) + " **widget** tail",
            snippets["snap.md"]);
        Assert.Equal(
            "…" + string.Join(' ', Enumerable.Repeat("alpha", 5)) + " **widget** tail",
            snippets["mid.md"]);
    }

    [Fact]
    public void ASnippetWindowEndsAtAWordAndMarksTheMatchAtItsVeryStart()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "edge.md",
            "---\ntype: Reference\ntitle: Edge\n---\n\nwidget z "
            + string.Join(' ', Enumerable.Repeat("abc", 40)) + "\n")
            .Add(
                "trim.md",
                "---\ntype: Reference\ntitle: Trim\n---\n\nwidget z "
                + string.Join(' ', Enumerable.Repeat("abcd", 32)) + "\n");

        var snippets = Search(bundle, "widget").Results.ToDictionary(
            result => result.Path,
            result => result.Snippet,
            StringComparer.Ordinal);

        // Both bodies overrun the 160-character window, so both are cut back to the last
        // word that fits — `edge.md` to a word ending exactly on the limit, which counts as
        // fitting, and `trim.md` to the word before the limit. The match sits at offset 0
        // in both, which is where the window itself begins: a hit at the very start of the
        // window is still marked.
        Assert.Equal(
            "**widget** z " + string.Join(' ', Enumerable.Repeat("abc", 38)) + "…",
            snippets["edge.md"]);
        Assert.Equal(
            "**widget** z " + string.Join(' ', Enumerable.Repeat("abcd", 30)) + "…",
            snippets["trim.md"]);
    }

    [Fact]
    public void AMatchEndingExactlyOnTheWindowEdgeIsInsideTheWindow()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Reference\ntitle: One\n---\n\nwidget z "
            + string.Join(' ', Enumerable.Repeat("abcd", 29))
            + " gadget then widget again.\n");

        var snippet = Assert.Single(Search(bundle, "widget gadget").Results).Snippet;

        // `gadget` ends on character 160 — the last position the window covers. Counted as
        // inside, the window around the first `widget` covers both terms and wins the
        // anchor outright, and the snippet marks `gadget` at its own edge. Counted as
        // outside, that window covers one term, the window around `gadget` covers two, and
        // the snippet opens somewhere in the middle of the body instead.
        Assert.Equal(
            "**widget** z " + string.Join(' ', Enumerable.Repeat("abcd", 29)) + " **gadget**…",
            snippet);
    }

    [Fact]
    public void AnUnmatchedBodyIsHeadedAtAWholeWordAndOnlyWhenItOverrunsTheWindow()
    {
        using var bundle = new TempBundle();
        var exact = string.Join(' ', Enumerable.Repeat("alpha", 26)) + " abcd";
        bundle.Add("exact.md", "---\ntype: Heading\ntitle: Exact\n---\n\n" + exact + "\n")
            .Add(
                "over.md",
                "---\ntype: Heading\ntitle: Over\n---\n\n"
                + string.Join(' ', Enumerable.Repeat("alpha", 40)) + "\n");

        var snippets = Search(bundle, "heading").Results.ToDictionary(
            result => result.Path,
            result => result.Snippet,
            StringComparer.Ordinal);

        // Neither body carries the matched term, so each snippet is the head of the body.
        // A body that is exactly the window long is shown whole and unmarked; a longer one
        // is cut back to the last whole word that fits — 26 five-letter words plus their
        // spaces is 155 characters and a 27th would carry it past 160.
        Assert.Equal(OkfSearchEngine.SnippetLength, exact.Length);
        Assert.Equal(exact, snippets["exact.md"]);
        Assert.Equal(string.Join(' ', Enumerable.Repeat("alpha", 26)) + "…", snippets["over.md"]);
    }

    [Fact]
    public void TheBodyWindowIsPreferredToTheDescriptionWheneverTheBodyMatches()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Reference\ntitle: One\ndescription: A description that also says widget.\n---\n\n"
            + "The body says widget too.\n");

        // Links-first (AD-28): the snippet exists to show *where in the concept* the match
        // is, so a body match is what a reader wants to see and the description is the
        // stand-in for when there is nothing in the body to show.
        Assert.Equal(
            "The body says **widget** too.",
            Assert.Single(Search(bundle, "widget").Results).Snippet);
    }

    [Fact]
    public void AConceptWithNoBodyAtAllStillSnippetsItsDescription()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Placeholder\ntitle: One\ndescription: Only the description says anything.\n---\n");

        // An empty body has no window to offer — not an empty window, which would leave the
        // result with nothing to display at all.
        Assert.Equal(
            "Only the description says anything.",
            Assert.Single(Search(bundle, "placeholder").Results).Snippet);
    }

    [Fact]
    public void AFrontmatterOnlyMatchSnippetsTheDescription()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Playbook\ntitle: Widget pricing\ndescription: How the fixture prices a widget.\n---\n\n" +
            "# Widget pricing\n\nNothing in this body says what kind of document it is.\n");

        var result = Assert.Single(Search(bundle, "playbook").Results);

        Assert.Equal("How the fixture prices a widget.", result.Snippet);
        Assert.Equal(["playbook"], result.MatchedTerms);
    }

    [Fact]
    public void MarkdownDecorationIsNotCarriedIntoASnippet()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Reference\ntitle: One\n---\n\n# Heading\n\n- **A widget** is bold in the source.\n");

        var snippet = Assert.Single(Search(bundle, "widget").Results).Snippet;

        // The body's own `**` would otherwise collide with the snippet's own marking and
        // render as `****widget****`.
        Assert.Equal("Heading A **widget** is bold in the source.", snippet);
    }

    [Fact]
    public void ASnippetKeepsLinkTextAndDropsLinkTargets()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Reference\ntitle: One\n---\n\n" +
            "Read [the custodian model](custodian-model.md), see ![a diagram](img/custodian.png),\n" +
            "and the [reference form][ref] too.\n\n[ref]: ../elsewhere/custodian.md\n");

        var snippet = Assert.Single(Search(bundle, "custodian").Results).Snippet;

        // The term inside the target is gone with the target, so the snippet can no longer
        // emit `[The **custodian** model](**custodian**-model.md)` — half a link with a
        // marked-up URL, which is neither readable in a terminal nor valid markdown.
        Assert.Equal("Read the **custodian** model, see a diagram, and the reference form too.", snippet);
    }

    [Fact]
    public void SnippetLinkFlatteningLeavesNonLinkBracketsAlone()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "one.md",
            "---\ntype: Reference\ntitle: One\n---\n\n" +
            "A widget is an array[0] item, footnoted[^src], and [unclosed (see below).\n\n" +
            "[^src]: A source.\n");

        var snippet = Assert.Single(Search(bundle, "widget").Results).Snippet;

        // Only the complete single-line link forms are rewritten: a subscript, a footnote
        // reference, and a bracket that never closes as a link stay exactly as written.
        Assert.Equal(
            "A **widget** is an array[0] item, footnoted[^src], and [unclosed (see below). [^src]: A source.",
            snippet);
    }

    [Theory]
    // Heading markers go, up to the six levels markdown has; a seventh `#` is not a
    // heading and neither is a `#` with no space after it.
    [InlineData("# Heading text", "Heading text")]
    [InlineData("###### Six deep", "Six deep")]
    [InlineData("####### Seven is not a heading", "####### Seven is not a heading")]
    [InlineData("#nospace", "#nospace")]
    // A line that is nothing but hashes contributes no text at all, and the blank it
    // leaves behind never becomes a leading space.
    [InlineData("##\n\nProse.", "Prose.")]
    // One block marker per line, after the heading marker and the space behind it.
    [InlineData("> Quoted", "Quoted")]
    [InlineData("- Item", "Item")]
    [InlineData("## - Nested", "Nested")]
    // A marker needs the space that makes it a marker: a hyphen with nothing after it
    // and a hyphen glued to a word are both prose.
    [InlineData("-\n\nProse.", "- Prose.")]
    [InlineData("-Item", "-Item")]
    // `[label]: destination` is address, not prose, and goes whole. `[]:` is not a label,
    // and a footnote definition carries the note itself.
    [InlineData("Prose.\n\n[label]: ../elsewhere.md", "Prose.")]
    [InlineData("[]: not an address", "[]: not an address")]
    [InlineData("[^src]: A source.", "[^src]: A source.")]
    // A link is rewritten only in its complete single-line form. A bracket that never
    // closes, one that closes with nothing after it, a bare `[`, an opener whose closer
    // never arrives, and a footnote reference are all left as written.
    [InlineData("Hey! [a](b) done", "Hey! a done")]
    [InlineData("[", "[")]
    [InlineData("[label]", "[label]")]
    [InlineData("[a(b.md) unclosed", "[a(b.md) unclosed")]
    [InlineData("([unclosed)", "([unclosed)")]
    [InlineData("[text](unclosed", "[text](unclosed")]
    // The target is looked for *after* the label, so a `)` inside the label's own text
    // does not end the link early and leave its address in the snippet.
    [InlineData("[a (b)](d.md)", "a (b)")]
    [InlineData("See [^note](notes.md) here.", "See [^note](notes.md) here.")]
    // A `!` that opens no image is just punctuation.
    [InlineData("Wow!", "Wow!")]
    public void MarkdownIsFlattenedToTheOneLineASnippetIsMadeOf(string body, string expected)
    {
        using var bundle = new TempBundle();
        bundle.Add("one.md", "---\ntype: Flattening\ntitle: One\n---\n\n" + body + "\n");

        // The concept matches on `type` alone and carries no `description`, so the snippet
        // is the flattened body itself rather than a window inside it — which makes the
        // flattening the only thing under test.
        Assert.Equal(expected, Assert.Single(Search(bundle, "flattening").Results).Snippet);
    }

    [Fact]
    public void ASnippetNeverCutsThroughASurrogatePair()
    {
        using var bundle = new TempBundle();

        // U+20BB7 is astral — two UTF-16 code units per character — and an unspaced script
        // gives the window no word boundary to cut back to, so the cut lands wherever the
        // 160-character limit falls. The leading `a` offsets the run so that limit sits
        // between the two halves of one character.
        var run = string.Concat(Enumerable.Repeat("\U00020BB7", 200));
        bundle.Add("head.md", "---\ntype: Astral\ntitle: Head\n---\n\na" + run + "\n")
            .Add("hit.md", "---\ntype: Astral\ntitle: Hit\n---\n\nlead " + run + " tail\n");

        // Two ways to reach the cut: `head.md` matches on frontmatter only, so its snippet
        // is the head of the body; `hit.md` matches on a token longer than the whole
        // window, so the window has no token end inside it to trim back to.
        var head = Search(bundle, "astral").Results.Single(result => result.Path == "head.md").Snippet;
        var hit = Assert.Single(Search(bundle, run).Results).Snippet;

        // The oracle is the UTF-16 well-formedness rule itself (Unicode §3.9, D91): every
        // high surrogate is followed by a low one and no low surrogate stands alone. A
        // snippet that breaks it is not text — it round-trips to U+FFFD through JSON and to
        // mojibake through a terminal.
        Assert.True(IsWellFormedUtf16(head), head);
        Assert.True(IsWellFormedUtf16(hit), hit);
        Assert.Contains("\U00020BB7", hit, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsCarryTheTrustTierAndStalenessOfTheConcept()
    {
        using var bundle = new TempBundle();
        bundle.Add("unverified.md", Concept("Reference", "Widget one", "A widget."))
            .Add(
                "machine.md",
                "---\ntype: Reference\ntitle: Widget two\nstale_after: 2020-01-01\n" +
                "verified: { by: okf-net/tests, at: 2026-01-02T00:00:00Z }\n---\n\nA widget.\n")
            .Add(
                "human.md",
                "---\ntype: Reference\ntitle: Widget three\nstale_after: 2030-01-01\n" +
                "verified:\n  - { by: human:tests@example.invalid, at: 2026-01-02T00:00:00Z }\n---\n\nA widget.\n");

        var results = Search(bundle, "widget").Results.ToDictionary(result => result.Path);

        Assert.Equal(OkfTrustTier.Unverified, results["unverified.md"].TrustTier);
        Assert.Equal(OkfTrustTier.MachineConfirmed, results["machine.md"].TrustTier);
        Assert.Equal(OkfTrustTier.HumanReviewed, results["human.md"].TrustTier);

        // Staleness is judged against the injected date, never the clock (PRD CORE-7).
        Assert.True(results["machine.md"].Stale);
        Assert.False(results["human.md"].Stale);
        Assert.False(results["unverified.md"].Stale);
    }

    [Fact]
    public void AResultIdentifiesItsConceptByPathAndByOkfId()
    {
        using var bundle = new TempBundle();
        bundle.Add("notes/deep/one.md", "---\ntype: Reference\n---\n\nA widget.\n");

        var result = Assert.Single(Search(bundle, "widget").Results);

        Assert.Equal("notes/deep/one", result.Id);
        Assert.Equal("notes/deep/one.md", result.Path);
        Assert.Equal(Path.Combine(bundle.Root, "notes", "deep", "one.md"), result.AbsolutePath);
        Assert.Equal(bundle.Root, result.Bundle);

        // `title` is optional (§4.1); generated indexes fall back to the filename stem and
        // so does search, so a result always has something to display.
        Assert.Equal("one", result.Title);
        Assert.Null(result.Description);
    }

    [Fact]
    public void TheLimitBoundsTheResultsButNotTheReportedTotal()
    {
        using var bundle = new TempBundle();
        for (var i = 0; i < 5; i++)
        {
            bundle.Add($"{i}.md", Concept("Reference", $"Widget {i}", "A widget."));
        }

        var outcome = OkfSearchEngine.Search(
            [bundle.Bundle],
            OkfSearchQuery.Parse("widget"),
            new OkfSearchOptions { Limit = 2, Today = TempBundle.Today });

        Assert.Equal(2, outcome.Results.Count);
        Assert.Equal(5, outcome.TotalMatches);
        Assert.True(outcome.Truncated);
    }

    [Fact]
    public void SuppliedTextIsSearchedInsteadOfTheFileOnDisk()
    {
        // The seam a caller holding a warm cache uses (the MCP server, a future incremental
        // index): what `ReadText` returns is the concept, and the file is not read again.
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("Reference", "Orders", "On disk."));

        var read = new List<string>();
        var options = Options();
        options.ReadText = path =>
        {
            read.Add(path);
            return Concept("Reference", "Orders", "Supplied, and phlogiston is only here.");
        };

        var outcome = OkfSearchEngine.Search([bundle.Bundle], OkfSearchQuery.Parse("phlogiston"), options);

        Assert.NotEmpty(read);
        Assert.Equal("orders.md", Assert.Single(outcome.Results).Path);
    }

    private static OkfSearchOutcome Search(TempBundle bundle, string query) =>
        OkfSearchEngine.Search([bundle.Bundle], OkfSearchQuery.Parse(query), Options());

    private static OkfSearchOptions Options() =>
        new() { Limit = 0, Today = TempBundle.Today };

    private static string Concept(string type, string title, string description) =>
        $"---\ntype: {type}\ntitle: {title}\ndescription: {description}\n---\n\n# {title}\n";

    private static string Filler() => string.Join(' ', Enumerable.Repeat("filler", 20)) + "\n";

    /// <summary>
    /// Whether a string is well-formed UTF-16: every high surrogate paired with a following
    /// low one, and no low surrogate standing alone (Unicode §3.9, D91).
    /// </summary>
    private static bool IsWellFormedUtf16(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsLowSurrogate(text[index]))
            {
                return false;
            }

            if (!char.IsHighSurrogate(text[index]))
            {
                continue;
            }

            if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
            {
                return false;
            }

            index++;
        }

        return true;
    }
}
