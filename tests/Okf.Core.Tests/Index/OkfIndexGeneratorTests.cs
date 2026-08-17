namespace Okf.Core.Tests.Index;

/// <summary>
/// Index generation (PRD CORE-9, CORE-10; decisions.md Q1): the ordering rule, the
/// title/description fallbacks, <c>about.md</c> sourcing, the root's <c>okf_version</c>
/// frontmatter, idempotence, and the marker that keeps drift detection off foreign
/// bundles.
/// </summary>
public class OkfIndexGeneratorTests
{
    [Fact]
    public void SectionsAreOrderedByTypeWithSubdirectoriesLast()
    {
        using var bundle = new TempBundle();
        bundle.Add("zeta.md", Concept("Metric", "Zeta"))
            .Add("alpha.md", Concept("BigQuery Table", "Alpha"))
            .Add("tables/orders.md", Concept("BigQuery Table", "Orders"));

        var root = Root(bundle);

        Assert.Equal(
            ["BigQuery Table", "Metric", "Subdirectories"],
            root.Entries.Select(entry => entry.Section).Distinct());

        // The subdirectory section is always last, whatever the bundle's type vocabulary
        // sorts like — `Metric` sorts before `Subdirectories`, `Zeta` would not.
        Assert.Equal(OkfIndexEntry.SubdirectoriesSection, root.Entries[^1].Section);
    }

    [Fact]
    public void EntriesWithinASectionAreOrderedCaseInsensitivelyByFilename()
    {
        using var bundle = new TempBundle();
        bundle.Add("beta.md", Concept("Reference", "Zzz"))
            .Add("Alpha.md", Concept("Reference", "Mmm"))
            .Add("gamma.md", Concept("Reference", "Aaa"));

        // By filename, not by title: `title` is optional and may repeat, so ordering by
        // it is neither total nor stable.
        Assert.Equal(["Alpha.md", "beta.md", "gamma.md"], Root(bundle).Entries.Select(entry => entry.Link));
    }

    [Fact]
    public void SectionOrderIsCaseInsensitiveThenOrdinalWithSubdirectoriesLastWhateverItSortsLike()
    {
        using var bundle = new TempBundle();
        bundle.Add("p.md", Concept("alpha", "P"))
            .Add("q.md", Concept("Beta", "Q"))
            .Add("z.md", Concept("Metric", "Z"))
            .Add("a.md", Concept("metric", "A"))
            .Add("r.md", Concept("Zeta", "R"))
            .Add("sub/leaf.md", Concept("Reference", "Leaf"));

        // AD-15's ordering rule, exercised where each of its three clauses decides
        // something the next one cannot. `Zeta` sorts after `Subdirectories`, so only the
        // subdirectory-last clause can put the navigation section at the end. `alpha` and
        // `Beta` sort one way case-insensitively and the other way ordinally, so only the
        // case-insensitive compare can order them. `Metric` and `metric` are equal
        // case-insensitively, so only the ordinal tiebreak can separate them — and their
        // filenames run the other way, so a comparison that fell through to the link would
        // be visible here.
        Assert.Equal(
            [
                "alpha|p.md",
                "Beta|q.md",
                "Metric|z.md",
                "metric|a.md",
                "Zeta|r.md",
                "Subdirectories|sub/index.md",
            ],
            Root(bundle).Entries.Select(entry => entry.Section + "|" + entry.Link));
    }

    [Fact]
    public void WithinASectionTheOrdinalTiebreakOrdersLinksThatDifferOnlyInCase()
    {
        using var bundle = new TempBundle();
        bundle.Add("alpha.md", Concept("Reference", "Alpha"))
            .Add("Beta.md", Concept("Reference", "Beta"))
            .Add("a%28b.md", Concept("Reference", "Encoded"))
            .Add("a(B.md", Concept("Reference", "Bracketed"));

        // `a(B.md` percent-encodes to `a%28B.md`, which differs from `a%28b.md` only in
        // case — the pair the ordinal tiebreak exists for. The walk hands them over in the
        // opposite order (`%` sorts before `(`), so a comparison that stopped at
        // OrdinalIgnoreCase would leave them in the order they arrived, and the order would
        // be the sort's rather than the rule's. AD-15 requires it to be total.
        Assert.Equal(
            ["a%28B.md", "a%28b.md", "alpha.md", "Beta.md"],
            Root(bundle).Entries.Select(entry => entry.Link));
    }

    [Fact]
    public void LinkTargetsPercentEncodeTheThreeCharactersThatWouldBreakTheEntry()
    {
        using var bundle = new TempBundle();
        bundle.Add("net (gross) rate.md", Concept("Metric", "Net (gross) rate"))
            .Add("q (a)/leaf.md", Concept("Reference", "Leaf"));

        // A markdown link target ends at the first `)`, and a bare space ends it too, so a
        // filename or a directory name carrying either would emit an entry that no longer
        // matches the §8 form `okf lint` enforces (OKF0003). Nothing else is escaped: `/`
        // has to stay a separator.
        var root = Root(bundle);

        Assert.Equal(
            ["net%20%28gross%29%20rate.md", "q%20%28a%29/index.md"],
            root.Entries.Select(entry => entry.Link));
        Assert.Contains(
            "* [Net (gross) rate](net%20%28gross%29%20rate.md)",
            root.Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ABlurbLosesItsPaddingRatherThanCarryingItIntoTheBullet()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "orders.md",
            "---\ntype: Metric\ntitle: \"  Orders  \"\ndescription: \"  The   orders.  \"\n---\n\n# Orders\n");

        var entry = Assert.Single(Root(bundle).Entries);

        // An index entry is one line, so runs of whitespace inside a value collapse to a
        // single space and whitespace at either edge disappears entirely — a quoted YAML
        // scalar keeps its padding and a bullet must not.
        Assert.Equal("Orders", entry.Title);
        Assert.Equal("The orders.", entry.Description);
        Assert.Equal("* [Orders](orders.md) - The orders.", entry.ToString());
    }

    [Fact]
    public void TitleFallsBackToTheFilenameStemAndDescriptionIsSimplyOmitted()
    {
        using var bundle = new TempBundle();
        bundle.Add("no-title.md", "---\ntype: Reference\n---\n\n# Body\n");

        var entry = Assert.Single(Root(bundle).Entries);

        Assert.Equal("no-title", entry.Title);
        Assert.Null(entry.Description);
        Assert.Equal("* [no-title](no-title.md)", entry.ToString());
        Assert.DoesNotContain(" - ", Root(bundle).Content, StringComparison.Ordinal);
    }

    [Fact]
    public void AConceptWithNoTypeLandsInTheOtherSection()
    {
        using var bundle = new TempBundle();
        bundle.Add("bare.md", "---\ntitle: Bare\n---\n\n# Bare\n");

        Assert.Equal(OkfIndexEntry.OtherSection, Assert.Single(Root(bundle).Entries).Section);
    }

    [Fact]
    public void SubdirectoryEntriesTakeTheirBlurbFromAboutMarkdown()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/about.md", Concept("Guide", "About the tables", "The tables, described."))
            .Add("tables/orders.md", Concept("BigQuery Table", "Orders"))
            .Add("metrics/revenue.md", Concept("Metric", "Revenue"));

        var subdirectories = Root(bundle).Entries.Where(entry => entry.IsSubdirectory).ToList();

        // Q1, resolved: the blurb comes from `<subdir>/about.md` and from nowhere else —
        // a subdirectory without one is emitted with no blurb rather than a synthesized
        // sentence, which is what keeps generation offline (PRD CLI-16).
        Assert.Equal("The tables, described.", subdirectories.Single(e => e.Title == "tables").Description);
        Assert.Null(subdirectories.Single(e => e.Title == "metrics").Description);
    }

    [Fact]
    public void AboutMarkdownIsAlsoListedAsAnOrdinaryConcept()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/about.md", Concept("Guide", "About the tables", "The tables, described."))
            .Add("tables/orders.md", Concept("BigQuery Table", "Orders"));

        var tables = Index(bundle, "tables/index.md");

        // about.md is not a §3.1 reserved name: it is a concept, and it appears in its own
        // directory's index like any other.
        Assert.Contains(tables.Entries, entry => entry.Link == "about.md");
    }

    [Fact]
    public void ReservedFilesAndNonConceptAssetsAreNeverListed()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("log.md", "# Log\n\n## 2026-06-01\n* **Update**: something.\n")
            .Add("viz.html", "<html></html>")
            .Add("attesters/sql_equality.py", "# not a concept");

        var root = Root(bundle);

        Assert.Equal(["orders.md"], root.Entries.Select(entry => entry.Link));
        Assert.DoesNotContain("log.md", root.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dot-prefixed concept is a concept everywhere the walk reaches, so it is listed
    /// and its directory gets an index of its own (work item #42).
    /// </summary>
    [Fact]
    public void DotPrefixedConceptsAreListedAndTheirDirectoriesIndexed()
    {
        using var bundle = new TempBundle();
        bundle.Add(".hidden.md", Concept("Reference", "Hidden"))
            .Add(".drafts/sketch.md", Concept("Reference", "Sketch"));

        var root = Root(bundle);

        Assert.Equal([".hidden.md", ".drafts/index.md"], root.Entries.Select(entry => entry.Link));
        Assert.Equal(["sketch.md"], Index(bundle, ".drafts/index.md").Entries.Select(entry => entry.Link));
    }

    [Fact]
    public void OnlyTheBundleRootIndexCarriesFrontmatter()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/orders.md", Concept("BigQuery Table", "Orders"));

        // §8/§12: the bundle-root index.md is the only index permitted frontmatter, and
        // only `okf_version`.
        Assert.StartsWith("---\nokf_version: \"0.2\"\n---\n\n", Root(bundle).Content, StringComparison.Ordinal);
        Assert.StartsWith(OkfIndexGenerator.GeneratedMarker, Index(bundle, "tables/index.md").Content, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGeneratedIndexCarriesTheMarker()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/orders.md", Concept("BigQuery Table", "Orders"));

        var plan = OkfIndexGenerator.Plan(bundle.Bundle);

        Assert.Equal(2, plan.Indexes.Count);
        Assert.All(plan.Indexes, index => Assert.True(OkfIndexGenerator.IsGenerated(index.Content)));
        Assert.False(OkfIndexGenerator.IsGenerated("# Subdirectories\n\n* [tables](tables/index.md)\n"));
        Assert.False(OkfIndexGenerator.IsGenerated(null));
    }

    [Fact]
    public void OneBlankLineSeparatesSectionsAndNothingPrecedesTheFirstHeading()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders", "The orders."))
            .Add("revenue.md", Concept("Metric", "Revenue"));

        // The whole file, byte for byte: §12's root frontmatter, AD-13's marker on its own
        // line, §8's bullet form, and exactly one blank line between sections with none
        // before the first heading. Idempotence (PRD ACC-7) is a byte comparison, so the
        // separators are as much the output as the entries are.
        Assert.Equal(
            """
            ---
            okf_version: "0.2"
            ---

            <!-- generated by okf -->

            # BigQuery Table

            * [Orders](orders.md) - The orders.

            # Metric

            * [Revenue](revenue.md)

            """.ReplaceLineEndings("\n"),
            Root(bundle).Content);
    }

    [Fact]
    public void TheReadSideAnswersNullForADirectoryWithNothingToList()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("attesters/sql_equality.py", "# not a concept");

        var plan = OkfIndexGenerator.Plan(bundle.Bundle);

        // PRD CORE-10's read side, which `okf_list` renders as `"source": "empty"`: asking
        // for a directory the generator would not write an index for is an ordinary
        // question with a null answer, not a failure.
        Assert.Null(plan.For(Path.Combine(bundle.Root, "attesters")));
        Assert.NotNull(plan.For(bundle.Root));
    }

    [Fact]
    public void GenerationIsIdempotent()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/about.md", Concept("Guide", "About", "The tables."))
            .Add("tables/orders.md", Concept("BigQuery Table", "Orders"))
            .Add("metrics/revenue.md", Concept("Metric", "Revenue"))
            .Add("deep/nested/leaf.md", Concept("Reference", "Leaf"));

        OkfIndexGenerator.Apply(OkfIndexGenerator.Plan(bundle.Bundle));
        var first = Snapshot(bundle);

        var second = OkfIndexGenerator.Plan(bundle.Bundle);

        // PRD CORE-9/ACC-7: a second run writes nothing and the bytes are identical.
        Assert.All(second.Indexes, index => Assert.Equal(OkfIndexStatus.Unchanged, index.Status));
        Assert.Empty(OkfIndexGenerator.Apply(second));
        Assert.Equal(first, Snapshot(bundle));
    }

    [Fact]
    public void ApplyReturnsEveryIndexItWroteAndWritesItAsUtf8WithNoByteOrderMark()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("sub/leaf.md", Concept("Reference", "Leaf"));

        var written = OkfIndexGenerator.Apply(OkfIndexGenerator.Plan(bundle.Bundle));

        // The return value is the write log `okf index` reports from, and every other
        // assertion on it in this file is a negative one — an empty second run, an orphan
        // absent — so returning nothing at all would satisfy them. The bytes are the other
        // half: `File.ReadAllText` eats a BOM, so a writer that emitted one would still
        // pass every text comparison here while leaving a file that can never again equal
        // its own rendered `Content` — drift on every later run (PRD ACC-7).
        Assert.Equal(
            [Path.Combine(bundle.Root, "index.md"), Path.Combine(bundle.Root, "sub", "index.md")],
            written.Select(index => index.Path));
        Assert.All(
            written,
            index => Assert.Equal(
                System.Text.Encoding.UTF8.GetBytes(index.Content),
                File.ReadAllBytes(index.Path)));
    }

    [Fact]
    public void ADirectoryWithNoConceptsOfItsOwnStillGetsAnIndexWhenAChildDoes()
    {
        using var bundle = new TempBundle();
        bundle.Add("deep/nested/leaf.md", Concept("Reference", "Leaf"));

        var middle = Index(bundle, "deep/index.md");

        // Otherwise the root's `deep/index.md` link would point at a file nothing writes.
        Assert.Equal(["nested/index.md"], middle.Entries.Select(entry => entry.Link));
        Assert.Contains("deep/index.md", Root(bundle).Entries.Select(entry => entry.Link));
    }

    [Fact]
    public void ADirectoryWithNothingToIndexIsNotListedAndGetsNoIndex()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("attesters/sql_equality.py", "# not a concept");

        var plan = OkfIndexGenerator.Plan(bundle.Bundle);

        Assert.Equal(["index.md"], plan.Indexes.Select(index => bundle.Bundle.RelativePath(index.Path)));
        Assert.DoesNotContain("attesters", Root(bundle).Content, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandWrittenIndexIsForeignRatherThanDrifted()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("index.md", "# Tables\n\n* **Orders** — the orders, described in prose.\n");

        var plan = OkfIndexGenerator.Plan(bundle.Bundle);

        // PRD ACC-1: Google's reference bundles hand-style their indexes. Without the
        // marker okf-net writes, drift detection would condemn every one of them.
        Assert.Equal(OkfIndexStatus.Foreign, Assert.Single(plan.Indexes).Status);
        Assert.False(plan.HasDrift);
        Assert.True(plan.Indexes[0].WouldWrite);
    }

    [Fact]
    public void AHandWrittenIndexThatMerelyQuotesTheMarkerIsStillForeign()
    {
        // The marker is a declaration, not a substring. An index that documents okf — or
        // any that happens to carry the line in a fenced block — must not be claimed as
        // okf-net's output: doing so would blame a foreign bundle for drift (PRD ACC-1)
        // and would overwrite hand-written content as an "update" rather than announce
        // the replacement.
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add(
                "index.md",
                "# Tables\n\n* [Orders](orders.md) - the orders.\n\nGenerated files open with:\n\n```\n"
                + OkfIndexGenerator.GeneratedMarker
                + "\n```\n");

        var plan = OkfIndexGenerator.Plan(bundle.Bundle);

        Assert.False(OkfIndexGenerator.IsGenerated(Assert.Single(plan.Indexes).ExistingContent));
        Assert.Equal(OkfIndexStatus.Foreign, plan.Indexes[0].Status);
        Assert.False(plan.HasDrift);
        Assert.DoesNotContain(OkfRules.GeneratedIndexDrift, bundle.LintIds());
    }

    [Theory]
    // CRLF endings, a trailing-whitespace marker line, and leading blank lines are all
    // read as the marker: a checkout or an editor may produce any of them.
    [InlineData("<!-- generated by okf -->\r\n\r\n# Metric\r\n", true)]
    [InlineData("<!-- generated by okf -->   \n\n# Metric\n", true)]
    [InlineData("\n\n<!-- generated by okf -->\n\n# Metric\n", true)]
    // After the frontmatter block, which only the bundle-root index carries (§8, §12).
    [InlineData("---\nokf_version: \"0.2\"\n---\n\n<!-- generated by okf -->\n\n# Metric\n", true)]
    // Anywhere else it is prose about the marker, not a claim to have written the file.
    [InlineData("# Metric\n\n<!-- generated by okf -->\n", false)]
    [InlineData("---\nokf_version: \"0.2\"\n---\n\n# Metric\n\n<!-- generated by okf -->\n", false)]
    [InlineData("<!-- generated by okf --> and more on the line\n", false)]
    [InlineData("---\nokf_version: \"0.2\"\n<!-- generated by okf -->\n", false)]
    [InlineData("", false)]
    // An unterminated frontmatter block is not something the renderer can have produced,
    // so the marker inside it claims nothing — the scan stops rather than restarting at
    // the line after the opening delimiter.
    [InlineData("---\n<!-- generated by okf -->\n", false)]
    // A `---` further down is a thematic break in the body, not a second frontmatter
    // fence: the block ends at the *first* closing delimiter and the marker is read from
    // there.
    [InlineData("---\nokf_version: \"0.2\"\n---\n\n<!-- generated by okf -->\n\n# Metric\n\n---\n\nMore.\n", true)]
    public void TheMarkerIsReadWhereAGeneratedFileCarriesIt(string text, bool generated) =>
        Assert.Equal(generated, OkfIndexGenerator.IsGenerated(text));

    [Fact]
    public void AGeneratedIndexIsRecognizedThroughACrlfCheckout()
    {
        // A generated file that came back with CRLF endings is still okf-net's, so it is
        // drift to be regenerated rather than a foreign index to be reported.
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"));
        var content = Assert.Single(OkfIndexGenerator.Plan(bundle.Bundle).Indexes).Content;
        bundle.Add("index.md", content.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(OkfIndexStatus.Drifted, Assert.Single(OkfIndexGenerator.Plan(bundle.Bundle).Indexes).Status);
    }

    [Fact]
    public void TheGeneratedRootIndexRoundTripsThroughOkfDocument()
    {
        // §8/§12 put frontmatter on the bundle-root index and the marker after it, so the
        // one index that is both marked and frontmattered must still survive
        // parse-then-serialize unchanged (PRD ACC-4).
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders", "The orders."));

        var content = Root(bundle).Content;

        Assert.Equal(content, OkfDocument.Parse(content).Serialize());
        Assert.Equal("0.2", Assert.IsType<OkfScalar>(OkfDocument.Parse(content).Frontmatter["okf_version"]).Value);
    }

    [Fact]
    public void AnEditedGeneratedIndexIsDrift()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"));
        OkfIndexGenerator.Apply(OkfIndexGenerator.Plan(bundle.Bundle));

        bundle.Add("index.md", $"{OkfIndexGenerator.GeneratedMarker}\n\n# Tables\n\n* [Orders](orders.md) - edited by hand\n");

        var plan = OkfIndexGenerator.Plan(bundle.Bundle);

        Assert.Equal(OkfIndexStatus.Drifted, Assert.Single(plan.Indexes).Status);
        Assert.True(plan.HasDrift);
    }

    [Fact]
    public void OneDriftedIndexAmongCleanOnesIsStillDrift()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("topics/widgets.md", Concept("Reference", "Widgets"));
        OkfIndexGenerator.Apply(OkfIndexGenerator.Plan(bundle.Bundle));

        bundle.Add(
            "topics/index.md",
            $"{OkfIndexGenerator.GeneratedMarker}\n\n# Reference\n\n* [Widgets](widgets.md) - edited by hand\n");

        var plan = OkfIndexGenerator.Plan(bundle.Bundle);

        // `HasDrift` is "any", not "all": PRD CLI-14 fails the whole run on one stale
        // index, so a plan whose other indexes are byte-identical must still report drift.
        Assert.Equal(OkfIndexStatus.Drifted, Assert.Single(plan.Indexes, index => index.IsDrift).Status);
        Assert.Equal(OkfIndexStatus.Unchanged, Assert.Single(plan.Indexes, index => !index.IsDrift).Status);
        Assert.True(plan.HasDrift);
    }

    [Fact]
    public void AGeneratedIndexLeftInAnEmptiedDirectoryIsAnOrphan()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("gone/index.md", $"{OkfIndexGenerator.GeneratedMarker}\n\n# Reference\n\n* [Old](old.md)\n");

        var orphan = Assert.Single(OkfIndexGenerator.Plan(bundle.Bundle).Drift);

        Assert.Equal(OkfIndexStatus.Orphaned, orphan.Status);
        Assert.Empty(orphan.Entries);

        // Never written and never deleted: removing a file a human may still want is not
        // a generator's call.
        Assert.False(orphan.WouldWrite);
        Assert.DoesNotContain(
            OkfIndexGenerator.Apply(OkfIndexGenerator.Plan(bundle.Bundle)),
            written => string.Equals(written.Path, orphan.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void AConceptWhoseFrontmatterDoesNotParseIsSkipped()
    {
        using var bundle = new TempBundle();
        bundle.Add("good.md", Concept("Reference", "Good"))
            .Add("broken.md", "---\ntype: [unterminated\n---\n\n# Broken\n");

        // It is already an OKF0001 error; inventing an entry for it would only put a
        // second complaint inside a generated file.
        Assert.Equal(["good.md"], Root(bundle).Entries.Select(entry => entry.Link));
    }

    [Fact]
    public void MultiLineDescriptionsAreFlattenedOntoOneBullet()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "wrapped.md",
            "---\ntype: Reference\ntitle: Wrapped\ndescription: >\n  One sentence that the\n  producer wrapped.\n---\n\n# Wrapped\n");

        var entry = Assert.Single(Root(bundle).Entries);

        Assert.Equal("One sentence that the producer wrapped.", entry.Description);
        Assert.DoesNotContain('\n', entry.Description!);
    }

    [Fact]
    public void GenerationNeverWritesWhenOnlyPlanning()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"));

        OkfIndexGenerator.Plan(bundle.Bundle);

        // PRD CORE-10: synthesis never writes into a bundle the tool does not own.
        Assert.False(File.Exists(Path.Combine(bundle.Root, "index.md")));
    }

    [Fact]
    public void TheLinterReusesTheWalkAndReportsDriftAsOKF0306()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"));
        OkfIndexGenerator.Apply(OkfIndexGenerator.Plan(bundle.Bundle));
        bundle.Add("revenue.md", Concept("Metric", "Revenue"));

        var diagnostic = Assert.Single(
            bundle.Lint(),
            reported => string.Equals(reported.RuleId, OkfRules.GeneratedIndexDrift, StringComparison.Ordinal));

        Assert.Equal(OkfSeverity.Warning, diagnostic.Severity);
        Assert.Equal(Path.Combine(bundle.Root, "index.md"), diagnostic.Path);
    }

    [Fact]
    public void TheLinterLeavesAForeignIndexAlone()
    {
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders"))
            .Add("index.md", "# Tables\n\n* [Orders](orders.md) - hand-written, and out of date on purpose.\n")
            .Add("extra.md", Concept("Metric", "Extra"));

        Assert.DoesNotContain(OkfRules.GeneratedIndexDrift, bundle.LintIds());
    }

    [Fact]
    public void PlanningFromSuppliedCachesReadsNothingButTheIndexesThemselves()
    {
        // What lets `okf lint` surface OKF0306 for no extra file read or YAML parse: given
        // a concept's frontmatter, the generator never wants its text. The linter caches
        // on that promise and keeps only index texts, so pin it — a generator that started
        // asking for concept text would silently turn one lint walk back into two.
        using var bundle = new TempBundle();
        bundle.Add("orders.md", Concept("BigQuery Table", "Orders", "The orders."))
            .Add("sub/about.md", Concept("Overview", "About", "The subdirectory."));

        var files = bundle.Bundle.MarkdownFiles();
        var asked = new List<string>();

        var plan = OkfIndexGenerator.Plan(
            bundle.Bundle,
            new OkfIndexOptions
            {
                Files = files,
                ReadText = path =>
                {
                    asked.Add(path);
                    return null;
                },
                // The linter's cache is a dictionary lookup: a miss is null, never a read.
                ReadFrontmatter = path =>
                    File.Exists(path) ? OkfDocument.Parse(File.ReadAllText(path)).Frontmatter : null,
            });

        Assert.NotEmpty(plan.Indexes);
        Assert.All(asked, path => Assert.Equal(OkfBundle.IndexFileName, Path.GetFileName(path)));
    }

    private static OkfIndex Root(TempBundle bundle) => Index(bundle, "index.md");

    private static OkfIndex Index(TempBundle bundle, string relativePath)
    {
        var wanted = Path.Combine(bundle.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return OkfIndexGenerator.Plan(bundle.Bundle).Indexes.Single(index =>
            string.Equals(index.Path, wanted, StringComparison.Ordinal));
    }

    private static string Snapshot(TempBundle bundle) =>
        string.Join(
            "\n\0\n",
            Directory.EnumerateFiles(bundle.Root, "index.md", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => path + "\n" + File.ReadAllText(path)));

    private static string Concept(string type, string title, string? description = null) =>
        $"---\ntype: {type}\ntitle: {title}\n"
        + (description is null ? string.Empty : $"description: {description}\n")
        + "tags: [fixture]\n---\n\n# " + title + "\n";
}
