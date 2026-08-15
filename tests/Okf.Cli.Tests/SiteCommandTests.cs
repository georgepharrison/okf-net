using System.Text.Json;

namespace Okf.Cli.Tests;

/// <summary>
/// <c>okf site</c>: its usage contract (PRD CLI-14), what it writes, and that the repository's
/// own dogfood vault renders end to end.
/// </summary>
public class SiteCommandTests
{
    [Fact]
    public void HelpExitsZeroAndNamesTheRequiredOutputDirectory()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "site", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("--out <dir>", run.Output, StringComparison.Ordinal);
        Assert.Contains("--single-file", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsListedInTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf site", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingOutputDirectoryIsAUsageFailure()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("conformant", "bundle");

        var run = Cli.RunIn(tree.Root, tree.Root, "site", bundle);

        // Exit 2, not 1: nothing was wrong with the bundle (PRD CLI-14).
        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("--out", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void AnUnknownOptionIsAUsageFailure()
    {
        using var tree = new TempTree();
        var run = Cli.RunIn(tree.Root, tree.Root, "site", "--out", "site", "--nope");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("--nope", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratingABundleWritesALandingPageAndAPagePerConcept()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("conformant", "bundle");
        var output = Path.Combine(tree.Root, "site");

        var run = Cli.RunIn(tree.Root, tree.Root, "site", bundle, "--out", output, "--name", "Fixture");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(output, "index.html")));
        Assert.True(File.Exists(Path.Combine(output, "graph.html")));
        Assert.True(File.Exists(Path.Combine(output, "assets", "site.css")));
        Assert.True(File.Exists(Path.Combine(output, "assets", "site.js")));

        // Every concept in the fixture got a page, and the pages mirror the bundle tree.
        var concepts = Directory.EnumerateFiles(bundle, "*.md", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "index.md", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(bundle, file)[..^3] + ".html");

        foreach (var concept in concepts)
        {
            Assert.True(
                File.Exists(Path.Combine(output, "bundle", concept)),
                $"no page for {concept}");
        }

        Assert.Contains("Generated ", run.Output, StringComparison.Ordinal);
        Assert.Contains("human-reviewed", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratingIntoTheBundleItRendersIsRefused()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("conformant", "bundle");

        var run = Cli.RunIn(tree.Root, tree.Root, "site", bundle, "--out", Path.Combine(bundle, "site"));

        // The next run would otherwise read its own output, and `okf lint` would find a
        // bundle full of HTML.
        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("inside bundle", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(bundle, "site")));
    }

    [Fact]
    public void GeneratingIntoTheDirectoryHoldingTheBundleIsRefused()
    {
        using var tree = new TempTree();
        var bundles = Path.Combine(tree.Root, "bundles");
        Directory.CreateDirectory(bundles);
        tree.CopyFixture("conformant", Path.Combine("bundles", "conformant"));

        var run = Cli.RunIn(tree.Root, tree.Root, "site", tree.Root, "--out", bundles);

        // `--out` is not inside a bundle here — it holds one. The site's own per-bundle
        // subdirectory is named after the bundle, so every page would land back inside it,
        // which is exactly what the refusal exists to prevent.
        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("inside bundle", run.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(bundles, "*.html", SearchOption.AllDirectories));
    }

    [Fact]
    public void SingleFileWritesExactlyOneFile()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("conformant", "bundle");
        var output = Path.Combine(tree.Root, "one");

        var run = Cli.RunIn(tree.Root, tree.Root, "site", bundle, "--out", output, "--single-file");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        var files = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).ToList();
        Assert.Equal([Path.Combine(output, "index.html")], files);
    }

    [Fact]
    public void TheJsonReportCarriesTheTileCounts()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("conformant", "bundle");
        var output = Path.Combine(tree.Root, "site");

        var run = Cli.RunIn(tree.Root, tree.Root, "site", bundle, "--out", output, "--json");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        var root = document.RootElement;

        Assert.Equal(Path.Combine(output, "index.html"), root.GetProperty("entry").GetString());
        Assert.False(root.GetProperty("singleFile").GetBoolean());
        Assert.True(root.GetProperty("files").GetInt32() > 3);

        var counts = root.GetProperty("counts");
        var concepts = counts.GetProperty("concepts").GetInt32();
        Assert.True(concepts > 0);
        Assert.Equal(
            concepts,
            counts.GetProperty("humanReviewed").GetInt32()
            + counts.GetProperty("machineConfirmed").GetInt32()
            + counts.GetProperty("unverified").GetInt32());
    }

    [Fact]
    public void TwoRunsOverAnUnchangedVaultProduceTheSameBytes()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("conformant", "bundle");
        var first = Path.Combine(tree.Root, "first");
        var second = Path.Combine(tree.Root, "second");

        Assert.Equal(CliApplication.ExitSuccess, Cli.RunIn(tree.Root, tree.Root, "site", bundle, "--out", first).ExitCode);
        Assert.Equal(CliApplication.ExitSuccess, Cli.RunIn(tree.Root, tree.Root, "site", bundle, "--out", second).ExitCode);

        // The graph layout is seeded, not random, and nothing else reads a clock beyond the
        // date. Two runs that differed would make the site useless to keep in git.
        foreach (var file in Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(first, file);
            Assert.Equal(File.ReadAllText(file), File.ReadAllText(Path.Combine(second, relative)));
        }
    }

    [SkippableFact]
    public void TheDogfoodVaultGeneratesWithoutError()
    {
        var vault = Repository.DogfoodVault;
        Skip.If(vault is null || !Directory.Exists(vault), "The dogfood vault 'okf/' is not present.");

        using var tree = new TempTree();
        var output = Path.Combine(tree.Root, "site");

        var run = Cli.RunIn(tree.Root, tree.Root, "site", vault, "--out", output);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Empty(run.Error);

        // The vault's own concepts are all unverified today; the site must say so rather
        // than round anything up.
        Assert.Contains("unverified", run.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "index.html")));
        Assert.True(File.Exists(Path.Combine(output, "okf-net", "about-this-bundle.html")));
    }
}
