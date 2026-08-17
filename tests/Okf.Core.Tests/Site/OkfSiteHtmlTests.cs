using System.Text.Json;

namespace Okf.Core.Tests.Site;

/// <summary>
/// The site's chrome: the standing nav, the landing page's bundle sections, the dashboard's
/// controls, and the JSON payload the client runs on.
/// </summary>
public class OkfSiteHtmlTests
{
    [Theory]
    [InlineData("index.html", "Home")]
    [InlineData("dashboard.html", "Dashboard")]
    [InlineData("graph.html", "Graph")]
    public void AStandingViewIsTheOnlyNavEntryThatMarksItselfCurrent(string path, string label)
    {
        using var fixture = new SiteFixture();
        var content = fixture.Plan().Files.Single(file => file.Path == path).Content;

        // `aria-current="page"` is how a screen reader is told which of the three standing
        // views the reader is already in. Exactly one entry may claim it, or it says nothing.
        Assert.Contains($" aria-current=\"page\">{label}</a>", content, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(content, "aria-current=\"page\""));
    }

    [Fact]
    public void AConceptPageIsNoneOfTheThreeStandingViews()
    {
        using var fixture = new SiteFixture();
        var hub = fixture.Plan().Files.Single(file => file.Path == "kb/hub.html").Content;

        Assert.Equal(0, Occurrences(hub, "aria-current=\"page\""));
    }

    [Fact]
    public void EachBundleGetsItsOwnSectionOnTheLandingPage()
    {
        using var withIndex = new TempBundle("alpha");
        using var withoutIndex = new TempBundle("beta");
        withIndex
            .Add("index.md", """
                # Concept

                * [One](one.md) - one
                """)
            .Add("one.md", "---\ntype: Concept\ntitle: One\n---\n\nOne.\n")
            .Add("two.md", "---\ntype: Concept\ntitle: Two\n---\n\nTwo.\n");
        withoutIndex.Add("three.md", "---\ntype: Concept\ntitle: Three\n---\n\nThree.\n");

        var landing = Landing([withIndex, withoutIndex]);

        // More than one bundle is something to pick between; a site over one bundle is the
        // same markup without the affordance, and the stylesheet reads the class to know.
        Assert.Contains("<div class=\"bundle-picker\">", landing, StringComparison.Ordinal);
        Assert.DoesNotContain("bundle-picker single", landing, StringComparison.Ordinal);

        // A bundle with a root index.md heads its section with a link to that page and shows
        // the index's own entries — the list a reader would see in Obsidian.
        Assert.Contains("<h2><a href=\"alpha/index.html\">alpha</a>", landing, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"alpha/one.html\">One</a> - one</li>", landing, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha/two.html", landing, StringComparison.Ordinal);

        // §8 reserves `index.md`; it does not require it. A bundle without one is still
        // openable from the front door, and its heading is a name rather than a dead link.
        Assert.Contains("<h2>beta <span class=\"count\">1 concept</span></h2>", landing, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"beta/three.html\">Three</a></li>", landing, StringComparison.Ordinal);

        // The fallback list is per bundle, not per site: `beta`'s section may not list
        // `alpha`'s concepts, and `alpha` — which has an index — gets no fallback at all.
        Assert.DoesNotContain("<li><a href=\"alpha/one.html\">One</a></li>", landing, StringComparison.Ordinal);
    }

    [Fact]
    public void ABundleWhoseIndexIsEmptyFallsBackToItsConceptList()
    {
        using var bundle = new TempBundle("kb");
        bundle
            .Add("index.md", string.Empty)
            .Add("one.md", "---\ntype: Concept\ntitle: One\n---\n\nOne.\n");

        var landing = Landing([bundle]);

        // The heading still links the index — it exists — but an index with nothing in it
        // would leave the front door blank, so the concept list stands in for its entries.
        Assert.Contains("<h2><a href=\"kb/index.html\">kb</a>", landing, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"kb/one.html\">One</a></li>", landing, StringComparison.Ordinal);
        Assert.Contains("<div class=\"bundle-picker single\">", landing, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndexWithEntriesIsShownInsteadOfTheFallbackList()
    {
        using var fixture = new SiteFixture();
        var landing = fixture.Plan().Files.Single(file => file.Path == "index.html").Content;

        // `kb/index.md` names `hub` and `reviewed` and nothing else. The fallback names every
        // concept in the bundle, so a front door carrying both would be showing the reader a
        // table of contents the vault did not write.
        Assert.Contains("href=\"kb/hub.html\"", landing, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"kb/fresh.html\"", landing, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"kb/confirmed.html\"", landing, StringComparison.Ordinal);
    }

    [Fact]
    public void ASiteOverNoBundlesSaysSoRatherThanShowingABlankFrontDoor()
    {
        var plan = OkfSiteGenerator.Plan(
            new OkfWorkingSet([], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today, Name = "Empty" });
        var landing = plan.Files.Single(file => file.Path == OkfSiteBuilder.IndexHref).Content;

        Assert.Contains("<p class=\"empty\">This site covers no bundles.</p>", landing, StringComparison.Ordinal);
        Assert.Contains("0 concepts across 0 bundles", landing, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLandingPageCarriesItsHeadingItsCountsAndItsWayOnward()
    {
        using var fixture = new SiteFixture();
        var landing = fixture.Plan().Files.Single(file => file.Path == "index.html").Content;

        Assert.Contains("<div class=\"page-head\"><h1>Fixture</h1>", landing, StringComparison.Ordinal);
        Assert.Contains("7 concepts across 1 bundle", landing, StringComparison.Ordinal);
        Assert.Contains("<p class=\"landing-more\">", landing, StringComparison.Ordinal);

        // The footer states the generation date the site was built against, which is what
        // makes a stale reading on the page traceable to the run that produced it.
        Assert.Contains("Generated by okf from 1 bundle on 2026-06-01", landing, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDashboardCarriesItsHeadingItsDrillDownAndItsFilterBar()
    {
        using var fixture = new SiteFixture();
        var dashboard = fixture.Plan().Files.Single(file => file.Path == "dashboard.html").Content;

        Assert.Contains("<div class=\"page-head\"><h1>Fixture</h1>", dashboard, StringComparison.Ordinal);

        // The bundle drill-down, the search box and the reset are the three controls the
        // client binds to; a page missing one of them loses that filter silently.
        Assert.Contains("<div class=\"bundle-drill\" id=\"bundle-drill\">", dashboard, StringComparison.Ordinal);
        Assert.Contains(
            "data-bundle=\"kb\" aria-pressed=\"false\">kb <span class=\"count\">7</span>",
            dashboard,
            StringComparison.Ordinal);
        Assert.Contains("<div class=\"filterbar\">", dashboard, StringComparison.Ordinal);
        Assert.Contains("id=\"concept-search\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("id=\"filter-reset\"", dashboard, StringComparison.Ordinal);

        // A card states what the concept is before it offers the tags to filter on, and only
        // a concept carrying tags gets the second row: `hub` is the fixture's only one.
        Assert.Contains("<div class=\"card-meta\"><dl class=\"facts facts-tight\">", dashboard, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(dashboard, "<div class=\"card-tags\">"));
    }

    [Fact]
    public void OnlyThePagesThatRunOnTheDataPayloadLoadIt()
    {
        using var fixture = new SiteFixture();

        foreach (var file in fixture.Plan().Files.Where(file => file.Path.EndsWith(".html", StringComparison.Ordinal)))
        {
            var root = OkfSiteHtml.Root(file.Path);

            // The dashboard filters and the graph draws. Every other page is static markup
            // that would be paying for a payload of every concept in the vault to read none
            // of it. The client script itself is on every page — the nav needs it.
            var wantsData = file.Path is OkfSiteBuilder.DashboardHref or OkfSiteBuilder.GraphHref;
            Assert.Equal(
                wantsData,
                file.Content.Contains(
                    $"<script src=\"{root}{OkfSiteAssets.DataPath}\" defer=\"defer\">",
                    StringComparison.Ordinal));
            Assert.Contains(
                $"<script src=\"{root}{OkfSiteAssets.ScriptPath}\" defer=\"defer\">",
                file.Content,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlyAPageWithADescriptionCarriesTheMetaTag()
    {
        using var fixture = new SiteFixture();
        var plan = fixture.Plan();

        // §4.1's `description` is optional, and `<meta name="description" content="" />` is
        // worse than none: a search result shows the empty string as the page's summary.
        Assert.Contains(
            "<meta name=\"description\" content=\"Draft notes on &lt;tags&gt; &amp; ampersands.\" />",
            plan.Files.Single(file => file.Path == "kb/rough.html").Content,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<meta name=\"description\"",
            plan.Files.Single(file => file.Path == "kb/fresh.html").Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheDataPayloadCarriesEveryFieldTheClientReads()
    {
        using var fixture = new SiteFixture();
        var data = fixture.Plan().Files.Single(file => file.Path == OkfSiteAssets.DataPath).Content;

        // One assignment on one line. The same payload is embedded verbatim inside a
        // <script> element in single-file mode, where pretty-printing buys nothing and costs
        // bytes on every regeneration.
        Assert.StartsWith("window.OKF_SITE = {", data, StringComparison.Ordinal);
        Assert.EndsWith("};\n", data, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", data[..^1], StringComparison.Ordinal);

        using var document = JsonDocument.Parse(data["window.OKF_SITE = ".Length..].TrimEnd('\n', ';'));
        var root = document.RootElement;

        Assert.Equal("Fixture", root.GetProperty("name").GetString());
        Assert.Equal("2026-06-01", root.GetProperty("generated").GetString());
        Assert.False(root.GetProperty("singleFile").GetBoolean());

        // Every tile on the dashboard is a count out of this object; a key that stopped being
        // written is a tile rendering `undefined` with no error anywhere.
        Assert.Equal(
            [
                ("bundles", 1),
                ("concepts", 7),
                ("humanReviewed", 1),
                ("machineConfirmed", 1),
                ("unverified", 5),
                ("stale", 1),
                ("draft", 1),
            ],
            root.GetProperty("counts").EnumerateObject().Select(field => (field.Name, field.Value.GetInt32())));

        // The bundle drill-down runs off this array and nothing else.
        var bundle = Assert.Single(root.GetProperty("bundles").EnumerateArray());
        Assert.Equal("kb", bundle.GetProperty("name").GetString());
        Assert.Equal("kb", bundle.GetProperty("slug").GetString());
        Assert.Equal("kb/index.html", bundle.GetProperty("href").GetString());
        Assert.Equal(7, bundle.GetProperty("concepts").GetInt32());

        // The graph's tooltip shows the path, and the search box matches on the description,
        // so a concept with neither contributes an empty string rather than a missing key.
        var concepts = root.GetProperty("concepts").EnumerateArray().ToList();
        Assert.Equal(
            "hub.md",
            concepts.Single(concept => concept.GetProperty("id").GetString() == "kb/hub").GetProperty("path").GetString());
        Assert.Equal(
            string.Empty,
            concepts.Single(concept => concept.GetProperty("id").GetString() == "kb/fresh").GetProperty("description").GetString());
    }

    [Fact]
    public void TheSingleFileArticleMapIsOneLineToo()
    {
        using var fixture = new SiteFixture();
        var plan = fixture.Plan(singleFile: true);
        var content = Assert.Single(plan.Files).Content;

        const string opening = "<script type=\"application/json\" id=\"okf-articles\">";
        var start = content.IndexOf(opening, StringComparison.Ordinal) + opening.Length;
        var map = content[start..content.IndexOf("</script>", start, StringComparison.Ordinal)];

        // Every article's own newlines travel as `\n` escapes inside the JSON strings, so a
        // literal one here is the map having been pretty-printed — bytes added to the single
        // file a reader is handed, for something no reader ever sees.
        Assert.DoesNotContain("\n", map, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(map);
        Assert.Equal(
            plan.Model.Pages.Select(page => page.Id).Order(StringComparer.Ordinal),
            document.RootElement.EnumerateObject().Select(article => article.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0, "0 bundles")]
    [InlineData(1, "1 bundle")]
    [InlineData(2, "2 bundles")]
    [InlineData(7, "7 bundles")]
    public void ACountAgreesWithTheNounItCounts(int count, string expected) =>
        Assert.Equal(expected, OkfSiteHtml.Plural(count, "bundle"));

    [Fact]
    public void EscapingTextWithNothingInItProducesNothing()
    {
        // §5.1's `resource` and §4.1's `description` are both optional, so absent has to
        // render as absent rather than as whatever a null formats to.
        Assert.Equal(string.Empty, OkfSiteHtml.Escape(null));
        Assert.Equal("&lt;b&gt; &amp; &quot;q&quot;", OkfSiteHtml.Escape("<b> & \"q\""));
    }

    [Fact]
    public void EveryEmbeddedAssetTheSiteNamesIsActuallyInTheAssembly()
    {
        // `OkfSiteAssets` throws when a resource is missing, which is a build-time invariant
        // rather than a reachable branch — nothing at run time can take an EmbeddedResource
        // out of the image. The invariant itself is what is worth asserting, and this is the
        // assertion: the csproj declares the resources, the assembly reports them, and the
        // two are checked against each other rather than against a copy of either.
        var resources = typeof(OkfSiteAssets).Assembly.GetManifestResourceNames();

        Assert.Contains(OkfSiteAssets.StyleSheetResource, resources);
        Assert.Contains(OkfSiteAssets.ScriptResource, resources);
    }

    [Fact]
    public void AConceptPagesBreadcrumbEndsOnItsOwnUnlinkedTitle()
    {
        using var fixture = new SiteFixture();
        var nested = fixture.Plan().Files.Single(file => file.Path == "kb/deep/nested.html").Content;

        // The trail is the index hierarchy: the site, then the bundle, then each directory
        // above the page, then the page — which is where the reader already is, so it is a
        // label and not a link back to here.
        Assert.Contains(
            "<nav class=\"crumbs\" aria-label=\"Breadcrumb\"><ol>\n"
            + "<li><a href=\"../../index.html\">Fixture</a></li>\n"
            + "<li><a href=\"../index.html\">kb</a></li>\n"
            + "<li><a href=\"index.html\">deep</a></li>\n"
            + "<li>Nested</li>\n"
            + "</ol></nav>",
            nested,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDocumentOpensWithTheSameHeadAndNamesItself()
    {
        using var fixture = new SiteFixture();
        var hub = fixture.Plan().Files.Single(file => file.Path == "kb/hub.html").Content;

        Assert.StartsWith(
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n"
            + "<meta charset=\"utf-8\" />\n"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n"
            + "<title>",
            hub,
            StringComparison.Ordinal);

        // A page's title is what a bookmark, a tab and a search result carry, so it names
        // the concept before it names the site.
        Assert.Contains("<title>Hub — Fixture</title>", hub, StringComparison.Ordinal);
        Assert.Contains(
            $"<link rel=\"stylesheet\" href=\"../{OkfSiteAssets.StyleSheetPath}\" />",
            hub,
            StringComparison.Ordinal);
        Assert.EndsWith("</body>\n</html>\n", hub, StringComparison.Ordinal);
    }

    [Fact]
    public void ADashboardCardCarriesEverythingTheClientAndTheReaderNeed()
    {
        using var fixture = new SiteFixture();
        var dashboard = fixture.Plan().Files.Single(file => file.Path == "dashboard.html").Content;

        // The tier is a class so the stylesheet can key on it, the id is the handle the
        // client hides and shows the card by, and the path is what tells two concepts with
        // the same title apart.
        Assert.Contains(
            "<li class=\"card tier-unverified\" data-id=\"kb/hub\">"
            + "<a class=\"card-title\" href=\"kb/hub.html\">Hub</a>"
            + "<p>Links to everything.</p>",
            dashboard,
            StringComparison.Ordinal);
        Assert.Contains("<span class=\"path\">kb/hub.md</span>", dashboard, StringComparison.Ordinal);

        // The filter can land on nothing, and a list that just went blank has to say why.
        Assert.Contains(
            "<p class=\"empty\" id=\"concept-empty\" hidden=\"hidden\">No concept matches this filter.</p>",
            dashboard,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheGraphPageCarriesItsCanvasItsControlsAndAWayAroundThem()
    {
        using var fixture = new SiteFixture();
        var graph = fixture.Plan().Files.Single(file => file.Path == "graph.html").Content;

        // AD-39: the layout runs in the reader's browser off okf-net's own code, so the
        // canvas and its three controls are the whole page — and AD-40 keeps every concept
        // reachable without any of it.
        Assert.Contains("<canvas id=\"graph-canvas\" role=\"img\"", graph, StringComparison.Ordinal);
        foreach (var control in (string[])["graph-color", "graph-fit", "graph-relayout"])
        {
            Assert.Contains($"id=\"{control}\">", graph, StringComparison.Ordinal);
        }

        Assert.Contains("<div class=\"graph-tip\" id=\"graph-tip\">", graph, StringComparison.Ordinal);
        Assert.Contains("<div class=\"graph-legend\" id=\"graph-legend\">", graph, StringComparison.Ordinal);
        Assert.Contains("<noscript>", graph, StringComparison.Ordinal);

        // The fixture's seven concepts, and the five concept-to-concept links its bodies
        // draw between them — the graph is a reading of the vault, not of the file tree.
        Assert.Contains("7 concepts, 5 cross-links", graph, StringComparison.Ordinal);
    }

    private static string Landing(IReadOnlyList<TempBundle> bundles) =>
        OkfSiteGenerator.Plan(
                new OkfWorkingSet([.. bundles.Select(bundle => bundle.Bundle)], null, "fixture"),
                new OkfSiteOptions { Today = SiteFixture.Today, Name = "Fixture" })
            .Files.Single(file => file.Path == OkfSiteBuilder.IndexHref)
            .Content;

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
