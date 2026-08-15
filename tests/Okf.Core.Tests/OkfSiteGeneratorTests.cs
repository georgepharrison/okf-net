using System.Text;
using System.Xml;
using System.Xml.Linq;

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
                "dashboard.html",
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
        var dashboard = fixture.Plan().Files.Single(file => file.Path == "dashboard.html").Content;

        foreach (var filter in (string[])["bundles", "all", "human-reviewed", "machine-confirmed", "unverified", "stale", "draft"])
        {
            Assert.Contains($"data-filter=\"{filter}\"", dashboard, StringComparison.Ordinal);
        }

        // A tile is a toggle, and it says so to a screen reader as well as to the script.
        Assert.Contains("aria-pressed=\"false\"", dashboard, StringComparison.Ordinal);

        // Colour never carries a tile's meaning alone: every one of them is labelled.
        foreach (var label in (string[])["Bundles", "Concepts", "Human-reviewed", "Machine-confirmed", "Unverified", "Stale", "Draft"])
        {
            Assert.Contains($">{label}</span>", dashboard, StringComparison.Ordinal);
        }

        // The list is in the HTML, not built by the script, so it survives with JS off.
        Assert.Contains("data-id=\"kb/hub\"", dashboard, StringComparison.Ordinal);

        // The dashboard is a destination now, so it carries the same way home a concept
        // page does — the breadcrumb root — as well as the standing nav.
        Assert.Contains("aria-label=\"Breadcrumb\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"index.html\">Fixture</a></li>", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLandingPageIsTheBundleIndexUnderACompactTrustStrip()
    {
        using var fixture = new SiteFixture();
        var index = fixture.Plan().Files.Single(file => file.Path == "index.html").Content;

        // Progressive disclosure is the front door: the vault's own index hierarchy, with
        // the trust tiles reduced to a strip above it.
        Assert.Contains("class=\"tiles tiles-strip\"", index, StringComparison.Ordinal);
        Assert.Contains("class=\"bundle-index\"", index, StringComparison.Ordinal);
        Assert.Contains("class=\"prose landing-index\"", index, StringComparison.Ordinal);

        // The bundle's root index.md, rendered — the same entries a reader would see in
        // Obsidian, and its links resolved from the site root rather than from kb/.
        Assert.Contains("href=\"kb/hub.html\"", index, StringComparison.Ordinal);
        Assert.Contains("href=\"kb/reviewed.html\"", index, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"hub.html\"", index, StringComparison.Ordinal);

        // Every tile is a link into the dashboard carrying its own filter; none of them is
        // a toggle any more, because the landing page holds no filter state.
        foreach (var filter in (string[])["bundles", "all", "human-reviewed", "machine-confirmed", "unverified", "stale", "draft"])
        {
            Assert.Contains($"href=\"dashboard.html#f={filter}\"", index, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("id=\"concept-list\"", index, StringComparison.Ordinal);

        // The graph stays reachable from here as well as from the dashboard.
        Assert.Contains("href=\"graph.html\"", index, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageCarriesAStandingWayHome()
    {
        using var fixture = new SiteFixture();

        foreach (var file in fixture.Plan().Files.Where(file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
        {
            var root = string.Concat(Enumerable.Repeat("../", file.Path.Count(character => character == '/')));
            Assert.Contains($"<a class=\"brand\" href=\"{root}index.html\">", file.Content, StringComparison.Ordinal);
            Assert.Contains($"<a href=\"{root}index.html\"", file.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSingleFileFragmentSchemeCarriesTheSameFourDestinations()
    {
        using var fixture = new SiteFixture();
        var file = Assert.Single(fixture.Plan(singleFile: true).Files).Content;

        foreach (var view in (string[])["home", "dashboard", "graph", "concept"])
        {
            Assert.Contains($"data-view=\"{view}\"", file, StringComparison.Ordinal);
        }

        // Home is what opens, and the other two standing views are fragment routes.
        Assert.Contains("class=\"view active\" data-view=\"home\"", file, StringComparison.Ordinal);
        Assert.Contains("href=\"#v=dashboard\"", file, StringComparison.Ordinal);
        Assert.Contains("href=\"#v=graph\"", file, StringComparison.Ordinal);
        Assert.Contains("href=\"#v=dashboard&amp;f=stale\"", file, StringComparison.Ordinal);
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

    [Fact]
    public void EveryTagIsALinkToTheDashboardWithThatTagFiltered()
    {
        using var fixture = new SiteFixture();
        var plan = fixture.Plan();

        // On a concept page: the dashboard is one directory up, and the badge says which
        // tag it carries in an attribute the client reads rather than in the href it parses.
        var hub = plan.Files.Single(file => file.Path == "kb/hub.html").Content;
        Assert.Contains("href=\"../dashboard.html#t=a\"", hub, StringComparison.Ordinal);
        Assert.Contains("data-tag=\"a\"", hub, StringComparison.Ordinal);

        // On the dashboard's own list: still a real link, so it works with JavaScript off;
        // the client intercepts it there so the tag composes with the tier, bundle and
        // query filters already applied instead of replacing them.
        var dashboard = plan.Files.Single(file => file.Path == "dashboard.html").Content;
        Assert.Contains("href=\"#t=a\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("href=\"#t=b\"", dashboard, StringComparison.Ordinal);

        // A concept with no tags contributes no badges — the absence is part of the shape.
        var fresh = plan.Files.Single(file => file.Path == "kb/fresh.html").Content;
        Assert.DoesNotContain("data-tag=", fresh, StringComparison.Ordinal);

        // Single-file mode has no dashboard.html to point at, so the same filter is a
        // fragment route — and the `&` joining the two keys is an entity, or the page
        // stops being well-formed XML.
        var single = Assert.Single(fixture.Plan(singleFile: true).Files).Content;
        Assert.Contains("href=\"#v=dashboard&amp;t=a\"", single, StringComparison.Ordinal);
    }

    [Theory]
    // A tag is data okf-net did not write (PRD ACC-1). These are the shapes that break an
    // attribute, a CDATA section, an embedded JSON payload and the fragment grammar itself.
    [InlineData("<b>")]
    [InlineData("]]>")]
    [InlineData("\"quoted\"")]
    [InlineData("'quoted'")]
    [InlineData("a&b")]
    [InlineData("\"><script>alert(1)</script>")]
    // The fragment is a `key=value&…` grammar; a tag that spells one must not become one.
    [InlineData("f=stale&b=kb")]
    [InlineData("x#y")]
    [InlineData("a space")]
    public void AHostileTagStaysInertInEveryHrefAndEveryLabel(string tag)
    {
        using var bundle = new TempBundle("kb");
        bundle.Add("x.md", $"---\ntype: Concept\ntitle: X\ntags: [\"{tag.Replace("\"", "\\\"", StringComparison.Ordinal)}\"]\n---\n\nBody.\n");

        var workingSet = new OkfWorkingSet([bundle.Bundle], null, "fixture");

        foreach (var singleFile in (bool[])[false, true])
        {
            var plan = OkfSiteGenerator.Plan(
                workingSet,
                new OkfSiteOptions { Today = SiteFixture.Today, SingleFile = singleFile });

            // The tag has to survive the YAML reader before any of this means anything.
            Assert.Equal([tag], plan.Model.Concepts.Single().Tags);
            var seen = 0;

            void Check(XDocument document)
            {
                foreach (var badge in document.Descendants().Where(element => element.Attribute("data-tag") is not null))
                {
                    seen++;

                    // The attribute the client reads and the text the reader sees are both
                    // the tag itself, not markup built out of it.
                    Assert.Equal(tag, badge.Attribute("data-tag")!.Value);
                    Assert.Equal(tag, badge.Value.Trim());

                    // The href carries the tag percent-escaped, so it can neither leave the
                    // attribute nor add a key to the fragment; unescaped it is the tag again.
                    var href = badge.Attribute("href")!.Value;
                    var start = href.LastIndexOf("t=", StringComparison.Ordinal) + 2;
                    Assert.Equal(tag, Uri.UnescapeDataString(href[start..]));
                    Assert.DoesNotContain("#", href[start..], StringComparison.Ordinal);
                    Assert.DoesNotContain("&", href[start..], StringComparison.Ordinal);
                }
            }

            foreach (var file in plan.Files.Where(file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
            {
                // Well-formedness first: a tag that closed its own attribute, its element or
                // the CDATA section around the inline script fails here.
                Check(XDocument.Load(Reader(file.Content)));
            }

            // A single-file site keeps every concept's article in an embedded JSON payload
            // rather than in the document tree, so the badges there are reachable only
            // through the payload — and that is exactly the copy the router writes into the
            // page with innerHTML.
            foreach (var article in Articles(plan))
            {
                Check(XDocument.Parse($"<root>{article}</root>"));
            }

            // The loop above is only an assertion if it ran: the concept page and the
            // dashboard card both carry the badge in either mode.
            Assert.True(seen >= 2, $"{(singleFile ? "single-file" : "multi-page")}: {seen} tag badges rendered");
        }
    }

    /// <summary>Every pre-rendered article a single-file plan embeds; empty for a multi-page plan.</summary>
    /// <param name="plan">The plan to read.</param>
    /// <returns>The article markup.</returns>
    private static IEnumerable<string> Articles(OkfSitePlan plan)
    {
        if (!plan.Model.SingleFile)
        {
            yield break;
        }

        const string opening = "<script type=\"application/json\" id=\"okf-articles\">";
        var content = plan.Files.Single().Content;
        var start = content.IndexOf(opening, StringComparison.Ordinal) + opening.Length;
        var end = content.IndexOf("</script>", start, StringComparison.Ordinal);

        using var document = System.Text.Json.JsonDocument.Parse(content[start..end]);
        foreach (var article in document.RootElement.EnumerateObject())
        {
            yield return article.Value.GetString()!;
        }
    }

    private static XmlReader Reader(string html) =>
        XmlReader.Create(
            new StringReader(html),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

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
