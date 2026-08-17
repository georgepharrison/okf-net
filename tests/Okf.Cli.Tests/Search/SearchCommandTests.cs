using System.Text.Json;

namespace Okf.Cli.Tests.Search;

/// <summary>
/// End-to-end <c>okf search</c> behavior: the flags, the exit-code contract (PRD CLI-11,
/// CLI-14), the links-first text report, and the JSON array that is also the MCP contract
/// (MCP-2, MCP-3).
/// </summary>
public class SearchCommandTests
{
    [Fact]
    public void HelpExitsZeroAndDescribesTheFlags()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "search", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf search <query> [path] [options]", run.Output, StringComparison.Ordinal);
        Assert.Contains("--limit <n>", run.Output, StringComparison.Ordinal);
        Assert.Contains("--tag <tag>", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsReachableFromTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf search <query> [path]", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--nope")]
    [InlineData("--format=yaml")]
    [InlineData("--limit=zero")]
    [InlineData("--limit=0")]
    [InlineData("--limit")]
    public void AMalformedCommandLineIsAUsageFailure(string argument)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "search", "widget", argument);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void AThirdBareArgumentIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "search", "widget", "path", "extra");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Quote a multi-word query", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tag:")]
    public void AQueryThatAsksForNothingIsAUsageFailure(string query)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "search", query);

        // Not an empty result set: with no terms and no filters there is nothing to run.
        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no query", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void SearchWithNoArgumentsAtAllIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "search");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no query", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnresolvableTargetIsAnEnvironmentFailure()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "search", "widget", Path.Combine(home.Root, "nowhere"));

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("No such directory", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextReportIsLinksFirstAndCarriesTrustAndStalenessMarkers()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal("Found 3 results in 3 concepts across 1 bundle.", run.Summary);

        var hits = run.OutputLines.Where(line => line.Contains(".md  ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, hits.Count);
        Assert.StartsWith("1. ", hits[0], StringComparison.Ordinal);
        Assert.StartsWith("3. ", hits[2], StringComparison.Ordinal);

        // Rank, score, path, title, type, then the trust and staleness markers.
        Assert.Contains(
            "bundle/widgets.md  Widget catalog (Reference)  [human-reviewed]",
            run.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "bundle/pricing.md  Widget pricing (Playbook)  [machine-confirmed, stale]",
            run.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "bundle/notes/gadgets.md  Gadgets (Reference)  [unverified]",
            run.Output,
            StringComparison.Ordinal);

        // Links-first: a snippet, never the body. The last sentence of `widgets.md` never
        // appears in the report.
        Assert.DoesNotContain("has read an index.md", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void NoMatchesIsNotAnError()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "unobtainium", bundle);

        // PRD CLI-11 and the §3 table: exit 0 including no matches — the grep convention
        // deliberately does not apply (decisions.md Q7).
        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal("Found 0 results in 3 concepts across 1 bundle.", run.Summary);
    }

    [Fact]
    public void ReservedFilesAreNeverSearched()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        // `phlogiston` appears only in the fixture's index.md and log.md files.
        Assert.Contains("Phlogiston", File.ReadAllText(Path.Combine(bundle, "index.md")), StringComparison.Ordinal);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "phlogiston", bundle);

        Assert.Equal("Found 0 results in 3 concepts across 1 bundle.", run.Summary);
    }

    [Fact]
    public void TheDropZoneOutsideTheBundlesIsNeverSearched()
    {
        using var tree = new TempTree();
        tree.CopyFixture("searchable", "project/okf/bundles/first");
        var leaked = tree.Write(
            "project/okf/raw/captured.md",
            "---\ntype: Reference\ntitle: Captured\n---\n\nA widget, and phlogiston too.\n");
        var project = Path.Combine(tree.Root, "project");

        var widgets = CliHarness.RunIn(project, tree.Root, "search", "widget");
        var phlogiston = CliHarness.RunIn(project, tree.Root, "search", "phlogiston");

        // `raw/` is the drop zone and sits outside every bundle root (decisions.md Q3), so
        // bundle-scoped search cannot reach it — asserted, not assumed.
        Assert.True(File.Exists(leaked));
        Assert.Equal("Found 3 results in 3 concepts across 1 bundle.", widgets.Summary);
        Assert.Equal("Found 0 results in 3 concepts across 1 bundle.", phlogiston.Summary);
        Assert.DoesNotContain("captured.md", widgets.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AndFallsBackToOrAndSaysSo()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget unobtainium", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(
            "No concept matched all 2 terms; showing concepts matching any of them.",
            run.OutputLines[0]);
        Assert.Equal("Found 3 results in 3 concepts across 1 bundle.", run.Summary);
    }

    [Fact]
    public void TheLimitTruncatesAndSaysSo()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget", bundle, "--limit", "1");

        Assert.Equal("Found 3 results in 3 concepts across 1 bundle; showing the top 1.", run.Summary);
        Assert.Single(run.OutputLines, line => line.StartsWith("1. ", StringComparison.Ordinal));
        Assert.DoesNotContain("2. ", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterFlagsAreTheInlineFilterSyntaxByAnotherSpelling()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        var inline = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget tag:catalog type:Reference", bundle);
        var flags = CliHarness.RunIn(
            tree.Root, tree.Root, "search", "widget", bundle, "--tag", "CATALOG", "--type", "reference");

        Assert.Equal(inline.Output, flags.Output);

        // `pricing.md` is tagged `pricing`/`widgets`, not `catalog`, so the filter drops it
        // — and the flags drop exactly the same concept, case notwithstanding.
        Assert.Equal("Found 2 results in 3 concepts across 1 bundle.", inline.Summary);
        Assert.Contains("bundle/widgets.md", inline.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("bundle/pricing.md", inline.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameCorpusAndQueryProduceByteIdenticalOutput()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        var text = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget catalog", bundle);
        var again = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget catalog", bundle);
        var json = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget catalog", bundle, "--json");
        var jsonAgain = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget catalog", bundle, "--json");

        // PRD ACC-7: running a command twice on an unchanged tree produces identical output
        // and identical exit codes.
        Assert.Equal(text.Output, again.Output);
        Assert.Equal(json.Output, jsonAgain.Output);
        Assert.NotEmpty(text.Output);
    }

    [Fact]
    public void JsonIsTheStableSortedResultContract()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget", bundle, "--json");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        var results = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(3, results.Count);

        // The field set is the contract MCP consumers read (PRD MCP-2); adding to it is a
        // decision, not an accident.
        Assert.Equal(
            [
                "id", "path", "absolutePath", "bundle", "bundleName", "title", "type",
                "description", "tags", "score", "snippet", "trustTier", "stale",
                "matchMode", "matchedTerms",
            ],
            results[0].EnumerateObject().Select(property => property.Name));

        var first = results.Single(result => result.GetProperty("id").GetString() == "widgets");
        Assert.Equal("widgets.md", first.GetProperty("path").GetString());
        Assert.Equal(Path.Combine(bundle, "widgets.md"), first.GetProperty("absolutePath").GetString());
        Assert.Equal(bundle, first.GetProperty("bundle").GetString());
        Assert.Equal("bundle", first.GetProperty("bundleName").GetString());
        Assert.Equal("Widget catalog", first.GetProperty("title").GetString());
        Assert.Equal("Reference", first.GetProperty("type").GetString());
        Assert.Equal("human-reviewed", first.GetProperty("trustTier").GetString());
        Assert.False(first.GetProperty("stale").GetBoolean());
        Assert.Equal("all", first.GetProperty("matchMode").GetString());
        Assert.Equal(["widget"], first.GetProperty("matchedTerms").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("**widget**", first.GetProperty("snippet").GetString()!, StringComparison.Ordinal);
        Assert.True(first.GetProperty("score").GetDouble() > 0);

        // Sorted: scores descend down the array.
        Assert.Equal(
            results.Select(result => result.GetProperty("score").GetDouble()).OrderDescending(),
            results.Select(result => result.GetProperty("score").GetDouble()));

        // A concept with no `description` reports it as null rather than inventing one.
        var gadgets = results.Single(result => result.GetProperty("id").GetString() == "notes/gadgets");
        Assert.Equal(JsonValueKind.Null, gadgets.GetProperty("description").ValueKind);
        Assert.Equal("unverified", gadgets.GetProperty("trustTier").GetString());
    }

    [Fact]
    public void ItResolvesAVaultTheSameWayLintDoes()
    {
        using var tree = new TempTree();
        tree.CopyFixture("searchable", "project/okf/bundles/first");
        tree.CopyFixture("conformant", "project/okf/bundles/second");
        var project = Path.Combine(tree.Root, "project");

        var run = CliHarness.RunIn(project, tree.Root, "search", "widget", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("across 2 bundles", run.Summary, StringComparison.Ordinal);
        Assert.Contains("okf: resolved vault", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: query widget", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: match mode all", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatDoesNotParseIsSkippedAndReportedUnderVerbose()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("searchable", "bundle");
        File.WriteAllText(Path.Combine(bundle, "broken.md"), "---\ntitle: Widget\n  bad: [unclosed\n---\n\nBody.\n");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget", bundle, "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal("Found 3 results in 3 concepts across 1 bundle.", run.Summary);
        Assert.Contains("okf: skipped 1 file whose frontmatter does not parse", run.Error, StringComparison.Ordinal);
    }
}
