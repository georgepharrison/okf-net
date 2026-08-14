namespace Okf.Core.Tests;

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
