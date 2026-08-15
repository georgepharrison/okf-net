using System.Text;
using System.Xml;

namespace Okf.Core.Tests;

/// <summary>
/// The emitters: which files a site is made of, that every page is well-formed markup, and
/// that nothing in the output reaches for the network.
/// </summary>
public class OkfSiteGeneratorTests
{
    [Fact]
    public void MultiPageEmitsOnePageForEveryDocumentPlusTheDashboardGraphAndAssets()
    {
        using var fixture = new SiteFixture();
        var plan = fixture.Plan();

        Assert.Equal(
            [
                "assets/site-data.js",
                "assets/site.css",
                "assets/site.js",
                "graph.html",
                "index.html",
                "kb/confirmed.html",
                "kb/deep/index.html",
                "kb/deep/nested.html",
                "kb/expired.html",
                "kb/fresh.html",
                "kb/hub.html",
                "kb/index.html",
                "kb/log.html",
                "kb/reviewed.html",
                "kb/rough.html",
            ],
            plan.Files.Select(file => file.Path));
    }

    [Fact]
    public void EveryGeneratedPageIsWellFormedMarkup()
    {
        using var fixture = new SiteFixture();

        foreach (var singleFile in (bool[])[false, true])
        {
            foreach (var file in fixture.Plan(singleFile).Files.Where(file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
            {
                // A real parser, not a tag tally: an unclosed element, an unquoted
                // attribute, an unescaped `&` or a `<` that escaped the JSON payload all
                // fail here, and each of them is a page a browser would render wrongly.
                var exception = Record.Exception(() => Parse(file.Content));
                Assert.True(exception is null, $"{file.Path} ({(singleFile ? "single-file" : "multi-page")}): {exception?.Message}");
            }
        }
    }

    [Fact]
    public void NoPageReachesForTheNetwork()
    {
        using var fixture = new SiteFixture();

        foreach (var singleFile in (bool[])[false, true])
        {
            foreach (var file in fixture.Plan(singleFile).Files.Where(file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
            {
                // Concept bodies may cite external URLs — those are the reader's choice to
                // follow. What must never appear is a resource the page loads by itself.
                Assert.DoesNotContain("<script src=\"http", file.Content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("<script src=\"//", file.Content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("rel=\"stylesheet\" href=\"http", file.Content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("@import", file.Content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void SingleFileIsOneSelfContainedFileCarryingEveryConcept()
    {
        using var fixture = new SiteFixture();
        var plan = fixture.Plan(singleFile: true);

        var file = Assert.Single(plan.Files);
        Assert.Equal("index.html", file.Path);

        // Nothing is loaded from beside it, because there is nothing beside it.
        Assert.DoesNotContain("<script src=", file.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"stylesheet\"", file.Content, StringComparison.Ordinal);
        Assert.Contains("<style>", file.Content, StringComparison.Ordinal);

        // Every page's article travels with it, keyed by the id the router looks up.
        Assert.Contains("id=\"okf-articles\"", file.Content, StringComparison.Ordinal);
        foreach (var page in plan.Model.Pages)
        {
            Assert.Contains($"\"{page.Id}\":", file.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InlineCodeIsWrappedSoTheDocumentStaysParseable()
    {
        using var fixture = new SiteFixture();
        var file = Assert.Single(fixture.Plan(singleFile: true).Files);

        // The stylesheet and the script both contain `<` and `&`. Without the CDATA
        // wrapper the single-file page is not XML at all, which is the check above.
        Assert.Contains("/*<![CDATA[*/", file.Content, StringComparison.Ordinal);
        Assert.Contains("/*]]>*/", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTileCarriesTheFilterTheClientDispatchesOn()
    {
        using var fixture = new SiteFixture();
        var index = fixture.Plan().Files.Single(file => file.Path == "index.html").Content;

        foreach (var filter in (string[])["bundles", "all", "human-reviewed", "machine-confirmed", "unverified", "stale", "draft"])
        {
            Assert.Contains($"data-filter=\"{filter}\"", index, StringComparison.Ordinal);
        }

        // A tile is a toggle, and it says so to a screen reader as well as to the script.
        Assert.Contains("aria-pressed=\"false\"", index, StringComparison.Ordinal);

        // Colour never carries a tile's meaning alone: every one of them is labelled.
        foreach (var label in (string[])["Bundles", "Concepts", "Human-reviewed", "Machine-confirmed", "Unverified", "Stale", "Draft"])
        {
            Assert.Contains($">{label}</span>", index, StringComparison.Ordinal);
        }

        // The list is in the HTML, not built by the script, so it survives with JS off.
        Assert.Contains("data-id=\"kb/hub\"", index, StringComparison.Ordinal);
    }

    [Fact]
    public void AConceptPageShowsItsMetadataSourcesAndBacklinks()
    {
        using var fixture = new SiteFixture();
        var hub = fixture.Plan().Files.Single(file => file.Path == "kb/hub.html").Content;

        Assert.Contains("<h2>Metadata</h2>", hub, StringComparison.Ordinal);
        Assert.Contains("<h2>Sources</h2>", hub, StringComparison.Ordinal);
        Assert.Contains("<h2>Cited by</h2>", hub, StringComparison.Ordinal);

        // The source panel points at the footnote it is joined to (§5.1).
        Assert.Contains("id=\"src-spec\"", hub, StringComparison.Ordinal);
        Assert.Contains("href=\"#fn:1\"", hub, StringComparison.Ordinal);
        Assert.Contains("cited as [^spec]", hub, StringComparison.Ordinal);
        Assert.Contains("not cited in the body", hub, StringComparison.Ordinal);

        // The breadcrumb trail is the index hierarchy, ending at this page.
        Assert.Contains("aria-label=\"Breadcrumb\"", hub, StringComparison.Ordinal);
        Assert.Contains("href=\"index.html\"", hub, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyWritesEveryPlannedFileWhereThePlanSaysItGoes()
    {
        using var fixture = new SiteFixture();
        using var tree = new TempDirectory();
        var plan = fixture.Plan();

        var written = OkfSiteGenerator.Apply(plan, tree.Root);

        Assert.Equal(plan.Files.Count, written.Count);
        foreach (var file in plan.Files)
        {
            var path = Path.Combine(tree.Root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{file.Path} was planned but not written");
            Assert.Equal(file.Content, File.ReadAllText(path));
        }

        // UTF-8 with no BOM (PRD ACC-7): the bytes must not depend on the machine.
        var bytes = File.ReadAllBytes(Path.Combine(tree.Root, "index.html"));
        Assert.NotEqual<byte[]>([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        Assert.StartsWith("<!DOCTYPE html>", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void TheStylesheetAndScriptComeOutOfTheAssemblyItself()
    {
        // Embedded, not read from beside the binary: a published single-file AOT binary has
        // nothing beside it, and `okf site` must still produce a styled, interactive site.
        Assert.Contains("--good:", OkfSiteAssets.StyleSheet, StringComparison.Ordinal);
        Assert.Contains("OKF_SITE", OkfSiteAssets.Script, StringComparison.Ordinal);

        using var fixture = new SiteFixture();
        var plan = fixture.Plan();
        Assert.Equal(
            OkfSiteAssets.StyleSheet,
            plan.Files.Single(file => file.Path == OkfSiteAssets.StyleSheetPath).Content);
        Assert.Equal(
            OkfSiteAssets.Script,
            plan.Files.Single(file => file.Path == OkfSiteAssets.ScriptPath).Content);
    }

    private static void Parse(string html)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(new StringReader(html), settings);
        while (reader.Read())
        {
            // Reading to the end is the assertion: XmlReader throws on the first thing it
            // cannot parse.
        }
    }
}

/// <summary>A throwaway output directory, removed with the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>Creates the directory.</summary>
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(Root);
    }

    /// <summary>The directory's path.</summary>
    public string Root { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
