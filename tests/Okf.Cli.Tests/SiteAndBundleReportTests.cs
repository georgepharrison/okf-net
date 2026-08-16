using System.Text.Json;

namespace Okf.Cli.Tests;

/// <summary>
/// What <c>okf site</c> and <c>okf bundle</c> report about what they wrote: the JSON a
/// pipeline reads (AD-40, PRD §5) and the summary a person reads, whose counts and
/// pluralization are the only place the shape on disk becomes visible without opening it.
/// </summary>
[Collection(BundleLintCollection.Name)]
public class SiteAndBundleReportTests
{
    /// <summary>
    /// <c>okf site --json</c> describes the site it just wrote, and every number in it is
    /// checked against the directory rather than against another field of the same report:
    /// a report that agreed with itself and not with the disk would be worse than none.
    /// </summary>
    [Fact]
    public void SiteJsonDescribesTheSiteItWrote()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var output = Path.Combine(tree.Root, "site");

        var run = Cli.RunIn(
            tree.Root, tree.Root, "site", "--out", output, "--name", "Fixture Vault", "--json");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("\n  \"out\":", run.Output, StringComparison.Ordinal);

        using var report = JsonDocument.Parse(run.Output);
        var root = report.RootElement;
        Assert.Equal(output, root.GetProperty("out").GetString());
        Assert.Equal(Path.Combine(output, "index.html"), root.GetProperty("entry").GetString());
        Assert.Equal("Fixture Vault", root.GetProperty("name").GetString());
        Assert.False(root.GetProperty("singleFile").GetBoolean());
        Assert.Equal(
            Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Count(),
            root.GetProperty("files").GetInt32());

        // Every browsable page except the three the site always has — the landing page,
        // the dashboard and the graph — is a page of the model, so the count is the HTML
        // on disk minus that fixed chrome.
        Assert.Equal(
            Directory.EnumerateFiles(output, "*.html", SearchOption.AllDirectories).Count() - 3,
            root.GetProperty("pages").GetInt32());

        // One edge per link between the fixture's concepts. Pinned to the count rather
        // than asserted "at least zero", which every int satisfies — a graph that lost its
        // edges is exactly the regression this number exists to catch.
        Assert.Equal(2, root.GetProperty("edges").GetInt32());

        var counts = root.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("bundles").GetInt32());
        Assert.True(counts.GetProperty("concepts").GetInt32() > 0);
        foreach (var tile in (string[])["humanReviewed", "machineConfirmed", "unverified", "stale", "draft"])
        {
            Assert.True(counts.GetProperty(tile).GetInt32() >= 0, tile);
        }

        var bundle = Assert.Single(root.GetProperty("bundles").EnumerateArray().ToList());
        Assert.Equal("clean", bundle.GetProperty("name").GetString());
        Assert.Equal("clean", bundle.GetProperty("slug").GetString());

        // Counted from the bundle on disk, not from the report's own `counts`: two fields
        // of one report agreeing proves only that they were written from one variable.
        Assert.Equal(
            Directory.EnumerateFiles(
                Path.Combine(tree.Root, "okf", "bundles", "clean"), "*.md", SearchOption.AllDirectories)
                .Count(file => Path.GetFileName(file) is not ("index.md" or "log.md")),
            bundle.GetProperty("concepts").GetInt32());
    }

    /// <summary>
    /// <c>--single-file</c> says so in the report, so a pipeline can tell the two shapes
    /// apart without listing the directory — and the shape it names is the one on disk:
    /// fewer files than the multi-page run wrote, all of them still counted.
    /// </summary>
    [Fact]
    public void SiteJsonSaysWhenItWroteOneSelfContainedFile()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var single = Path.Combine(tree.Root, "single");
        var multi = Path.Combine(tree.Root, "multi");

        var singleRun = Cli.RunIn(tree.Root, tree.Root, "site", "--out", single, "--single-file", "--json");
        var multiRun = Cli.RunIn(tree.Root, tree.Root, "site", "--out", multi, "--json");

        using var singleReport = JsonDocument.Parse(singleRun.Output);
        using var multiReport = JsonDocument.Parse(multiRun.Output);
        Assert.True(singleReport.RootElement.GetProperty("singleFile").GetBoolean());
        Assert.False(multiReport.RootElement.GetProperty("singleFile").GetBoolean());
        Assert.Equal(
            Directory.EnumerateFiles(single, "*", SearchOption.AllDirectories).Count(),
            singleReport.RootElement.GetProperty("files").GetInt32());
        Assert.True(
            singleReport.RootElement.GetProperty("files").GetInt32()
            < multiReport.RootElement.GetProperty("files").GetInt32());
    }

    /// <summary>
    /// The text report ends by naming the file to open. It is the only line that tells a
    /// person what to do with what was just written.
    /// </summary>
    [Fact]
    public void SiteTextReportNamesTheLandingPageToOpen()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Cli.RunIn(tree.Root, tree.Root, "site", "--out", "site");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains($"Open site{Path.AltDirectorySeparatorChar}index.html", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The packaged size is reported in bytes below a kilobyte and in kilobytes above it,
    /// so a distribution's size is readable at both ends of the range it spans.
    /// </summary>
    [Theory]
    [InlineData(40, " B)")]
    [InlineData(4000, " KB)")]
    public void BundleReportsItsSizeInBytesOrKilobytes(int bodyLength, string expected)
    {
        using var tree = new TempTree();
        WritePackageableBundle(tree, new string('x', bodyLength));
        var destination = Path.Combine(tree.Root, "dist.tar.gz");

        var run = Cli.RunIn(tree.Root, tree.Root, "bundle", "--out", destination);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(expected, run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// §6.1 makes a link that leaves the packaged bundles legal and obliges consumers to
    /// tolerate it, so it is reported rather than refused — in the singular or the plural
    /// as the count requires, and as "no link leaves" when there is none.
    /// </summary>
    [Theory]
    [InlineData(0, "No link leaves the packaged bundles.")]
    [InlineData(1, "1 link leaves the packaged bundles and is recorded in")]
    [InlineData(2, "2 links leave the packaged bundles and are recorded in")]
    public void BundleReportsDanglingLinksInTheRightNumber(int links, string expected)
    {
        using var tree = new TempTree();
        var body = string.Join(
            "\n",
            Enumerable.Range(0, links).Select(index => $"See [elsewhere]("
                + $"../../outside/{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}.md)."));
        WritePackageableBundle(tree, body);

        var run = Cli.RunIn(tree.Root, tree.Root, "bundle", "--out", Path.Combine(tree.Root, "dist.tar.gz"));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(expected, run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--lint</c> lints the archive it wrote by unpacking it to a temporary directory
    /// and removes that directory afterwards; with <c>--format dir</c> there is nothing to
    /// unpack and the written directory is linted in place. Either way the packaged tree is
    /// the one that gets linted, and nothing is left behind (AD-37).
    /// </summary>
    [Theory]
    [InlineData("dist.tar.gz", null)]
    [InlineData("dist", "dir")]
    public void BundleLintsWhatItPackagedAndLeavesNoTemporaryTree(string name, string? format)
    {
        using var tree = new TempTree();
        WritePackageableBundle(tree, "Body.");
        var destination = Path.Combine(tree.Root, name);
        string[] args = format is null
            ? ["bundle", "--out", destination, "--lint"]
            : ["bundle", "--out", destination, "--format", format, "--lint"];

        var before = Directory.Exists(Path.Combine(Path.GetTempPath(), "okf-bundle"))
            ? Directory.EnumerateDirectories(Path.Combine(Path.GetTempPath(), "okf-bundle")).Count()
            : 0;
        var run = Cli.RunIn(tree.Root, tree.Root, args);
        var after = Directory.Exists(Path.Combine(Path.GetTempPath(), "okf-bundle"))
            ? Directory.EnumerateDirectories(Path.Combine(Path.GetTempPath(), "okf-bundle")).Count()
            : 0;

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(
            "Linting the distribution as a consumer would (default severities, no config):",
            run.Output,
            StringComparison.Ordinal);
        Assert.Contains("Checked ", run.Output, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    private static void WritePackageableBundle(TempTree tree, string body)
    {
        tree.Write(
            Path.Combine("okf", "bundles", "notes", "bundle.yaml"),
            """
            okf_version: "0.2"
            name: notes
            """);
        tree.Write(
            Path.Combine("okf", "bundles", "notes", "concept.md"),
            $"""
            ---
            id: concept
            type: concept
            title: Concept
            description: A concept with a body of a known length.
            ---

            # Concept

            {body}
            """);
    }
}
