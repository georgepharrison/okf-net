namespace Okf.Core.Tests;

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
