using System.Text.Json;

namespace Okf.Core.Tests;

/// <summary>
/// The site model: which pages exist, how links are rewired, which links become graph edges
/// and backlinks, and what the trust dashboard counts.
/// </summary>
/// <remarks>
/// Every expected value here is read off <see cref="SiteFixture" />'s frontmatter and the
/// spec section it comes from, never off the generator's output. The fixture's doc comment
/// carries the tally.
/// </remarks>
public class OkfSiteBuilderTests
{
    [Fact]
    public void EveryMarkdownFileBecomesOnePageMirroringTheBundleTree()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();

        Assert.Equal(
            [
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
            model.Pages.Select(page => page.Href));
    }

    [Fact]
    public void ReservedFilesArePagesButNeverConcepts()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();

        // §3.1: index.md and log.md are reserved names, not concepts. Three of the ten
        // pages are reserved, so seven concepts remain.
        Assert.Equal(7, model.Concepts.Count);
        Assert.DoesNotContain(model.Concepts, page => page.Path.EndsWith("index.md", StringComparison.Ordinal));
        Assert.DoesNotContain(model.Concepts, page => page.Path.EndsWith("log.md", StringComparison.Ordinal));

        var index = model.Pages.Single(page => page.Href == "kb/deep/index.html");
        Assert.Equal(OkfSitePageKind.Index, index.Kind);

        // An index names its own directory, not the file it lives in.
        Assert.Equal("deep", index.Title);
    }

    [Fact]
    public void TileCountsMatchTheFixturesFrontmatter()
    {
        using var fixture = new SiteFixture();
        var counts = fixture.Build().Counts;

        Assert.Equal(1, counts.Bundles);
        Assert.Equal(7, counts.Concepts);
        Assert.Equal(1, counts.HumanReviewed);
        Assert.Equal(1, counts.MachineConfirmed);
        Assert.Equal(5, counts.Unverified);
        Assert.Equal(1, counts.Stale);
        Assert.Equal(1, counts.Draft);

        // The three trust tiers partition the concepts (§5.3): a concept is in exactly one.
        Assert.Equal(
            counts.Concepts,
            counts.HumanReviewed + counts.MachineConfirmed + counts.Unverified);
    }

    [Fact]
    public void RelativeLinksBetweenConceptsPointAtGeneratedPages()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();

        var hub = model.Pages.Single(page => page.Href == "kb/hub.html");
        Assert.Contains("href=\"reviewed.html\"", hub.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"deep/nested.html\"", hub.BodyHtml, StringComparison.Ordinal);

        // The only `.md` destination left standing is the deliberately broken one; every
        // link that resolved got the page, not the source file.
        Assert.DoesNotContain("href=\"reviewed.md\"", hub.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"deep/nested.md\"", hub.BodyHtml, StringComparison.Ordinal);

        // A link that has to climb out of its directory climbs the page tree the same way,
        // which is what makes the output work from a file:// path with no web root.
        var nested = model.Pages.Single(page => page.Href == "kb/deep/nested.html");
        Assert.Contains("href=\"../hub.html\"", nested.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleRootRelativeLinksResolveAgainstTheBundleRoot()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();
        var hub = model.Pages.Single(page => page.Href == "kb/hub.html");

        // §6.1: a leading `/` is bundle-root-relative, not host-root-relative. Emitting it
        // unchanged would produce a link to `/expired.md` on the host, which resolves to
        // the filesystem root under file:// and to the domain root under Pages.
        Assert.Contains("href=\"expired.html\"", hub.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/expired", hub.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenLinksAreMarkedRatherThanDropped()
    {
        using var fixture = new SiteFixture();
        var hub = fixture.Build().Pages.Single(page => page.Href == "kb/hub.html");

        // §6.1: "Consumers MUST tolerate broken links" — the anchor survives, with its
        // original destination, and says it is broken.
        Assert.Contains("class=\"broken\"", hub.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("missing.md", hub.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("kb/missing", string.Join(' ', hub.LinksTo), StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalLinksOpenAwayFromTheSiteAndAreNeverGraphEdges()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();
        var hub = model.Pages.Single(page => page.Href == "kb/hub.html");

        Assert.Contains("https://example.invalid/", hub.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"external\"", hub.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", hub.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Edges, edge => edge.Target.Contains("example", StringComparison.Ordinal));
    }

    [Fact]
    public void EdgesAndBacklinksComeFromConceptsOnlyNeverFromGeneratedIndexes()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();

        // The bundle-root index links hub.md and reviewed.md; if index links counted, hub
        // would be cited by "kb/index" and every generated table of contents would show up
        // as a citation.
        Assert.All(model.Edges, edge => Assert.DoesNotContain("index", edge.Source, StringComparison.Ordinal));

        var hub = model.Pages.Single(page => page.Href == "kb/hub.html");
        Assert.Equal(["kb/deep/nested", "kb/reviewed"], hub.CitedBy.Order(StringComparer.Ordinal));

        var reviewed = model.Pages.Single(page => page.Href == "kb/reviewed.html");
        Assert.Equal(["kb/hub"], reviewed.CitedBy);

        // Nothing links to `fresh.md`, so it has no backlinks at all — the absence is as
        // much a part of the contract as the presence.
        var fresh = model.Pages.Single(page => page.Href == "kb/fresh.html");
        Assert.Empty(fresh.CitedBy);
    }

    [Fact]
    public void FootnoteLabelsJoinToTheSourcesTheyCite()
    {
        using var fixture = new SiteFixture();
        var hub = fixture.Build().Pages.Single(page => page.Href == "kb/hub.html");

        // §5.1: the footnote label is the join key into `sources[].id`. `spec` is cited in
        // the body; `unused` is not, and must not claim to be.
        var spec = hub.Sources.Single(source => source.Id == "spec");
        Assert.Equal(1, spec.FootnoteOrder);
        Assert.True(spec.IsHumanAuthored);
        Assert.True(spec.IsUrl);

        var unused = hub.Sources.Single(source => source.Id == "unused");
        Assert.Null(unused.FootnoteOrder);

        // The order is the number the rendered footnote anchor carries, so the panel's
        // link lands on the right definition.
        Assert.Contains("id=\"fn:1\"", hub.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void BreadcrumbsFollowTheIndexHierarchy()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();

        var nested = model.Pages.Single(page => page.Href == "kb/deep/nested.html");
        Assert.Equal(["kb", "deep", "Nested"], nested.Crumbs.Select(crumb => crumb.Label));
        Assert.Equal("../index.html", nested.Crumbs[0].Href);
        Assert.Equal("index.html", nested.Crumbs[1].Href);
        Assert.Null(nested.Crumbs[^1].Href);
    }

    [Fact]
    public void RawHtmlInABodyIsShownAsTextRatherThanEmitted()
    {
        using var bundle = new TempBundle("kb");
        bundle.Add("x.md", "---\ntype: Concept\n---\n\n<script>alert(1)</script>\n");

        var model = OkfSiteBuilder.Build(
            new OkfWorkingSet([bundle.Bundle], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today });

        var page = model.Pages.Single();
        Assert.DoesNotContain("<script>", page.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", page.BodyHtml, StringComparison.Ordinal);
    }

    [Theory]
    // A markdown link, an angle-bracket destination and a reference definition are three
    // ways to write the same anchor, and a CommonMark autolink is a fourth that `DisableHtml`
    // never sees. Each of them renders something a reader can click on a `file://` page.
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JaVaScRiPt:alert(1))")]
    [InlineData("[x]( javascript:alert(1))")]
    // Markdig decodes the reference before this runs, and a browser drops the tab before it
    // decides what scheme the URL carries.
    [InlineData("[x](&#106;avascript:alert(1))")]
    [InlineData("[x](java&#9;script:alert(1))")]
    // `//` opens a JavaScript comment, so this is a working `javascript:` URL that also
    // reads as external to anything matching on `://`.
    [InlineData("[x](javascript://example.invalid%0Aalert(1))")]
    [InlineData("[x](vbscript:msgbox(1))")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<JaVaScRiPt:alert(1)>")]
    // An image's `src` is the same attribute problem wearing a different name.
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("![x](data:text/html,<script>alert(1)</script>)")]
    // Not script, but still a navigation the site must not offer: `file:` reads the
    // reader's disk, and neither is on the allowlist.
    [InlineData("[x](file:///etc/passwd)")]
    [InlineData("[x](about:blank)")]
    public void ADestinationWithAnUnallowedSchemeNeverReachesAnAttribute(string markdown)
    {
        using var bundle = new TempBundle("kb");
        bundle.Add("x.md", $"---\ntype: Concept\n---\n\n{markdown}\n");

        var model = OkfSiteBuilder.Build(
            new OkfWorkingSet([bundle.Bundle], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today });

        var body = model.Pages.Single().BodyHtml;

        // The colon is what makes a destination a scheme, and the only place it may survive
        // is inside the link's own text. Nothing in an `href` or a `src` may carry one.
        foreach (var attribute in (string[])["href=\"", "src=\""])
        {
            foreach (var value in Values(body, attribute))
            {
                Assert.False(
                    value.Contains(':', StringComparison.Ordinal),
                    $"{markdown} left {attribute}{value}\" in the page");
            }
        }
    }

    [Theory]
    [InlineData("[x](https://example.invalid/a)", "https://example.invalid/a")]
    [InlineData("[x](HTTP://example.invalid/a)", "HTTP://example.invalid/a")]
    [InlineData("[x](mailto:a@b.invalid)", "mailto:a@b.invalid")]
    [InlineData("<https://example.invalid/a>", "https://example.invalid/a")]
    [InlineData("<a@b.invalid>", "mailto:a@b.invalid")]
    public void AnAllowlistedSchemeIsLeftExactlyAsItWasWritten(string markdown, string expected)
    {
        using var bundle = new TempBundle("kb");
        bundle.Add("x.md", $"---\ntype: Concept\n---\n\n{markdown}\n");

        var model = OkfSiteBuilder.Build(
            new OkfWorkingSet([bundle.Bundle], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today });

        // The allowlist is not a filter on external links: the reader's own web is still the
        // reader's to follow, and §6.1 leaves a destination okf-net cannot resolve alone.
        Assert.Contains($"\"{expected}\"", model.Pages.Single().BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenBundleRootRelativeLinkIsStillARelativeLink()
    {
        using var bundle = new TempBundle("kb");
        bundle.Add("deep/x.md", "---\ntype: Concept\n---\n\n[gone](/nope.md) and [out](/../secrets.md)\n");

        var model = OkfSiteBuilder.Build(
            new OkfWorkingSet([bundle.Bundle], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today });

        var body = model.Pages.Single().BodyHtml;

        // A leading `/` left standing is the filesystem root under file:// and the domain
        // root under Pages. §6.1 says mark a broken link rather than drop it — it does not
        // say point it at the host.
        Assert.DoesNotContain("href=\"/", body, StringComparison.Ordinal);
        Assert.Contains("href=\"../nope.md\" class=\"broken\"", body, StringComparison.Ordinal);

        // A destination that climbs out of the bundle names nowhere the site contains.
        Assert.DoesNotContain("href=\"../../", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("index.html", "kb/hub.html", "kb/hub.html")]
    [InlineData("kb/hub.html", "kb/reviewed.html", "reviewed.html")]
    [InlineData("kb/deep/nested.html", "kb/hub.html", "../hub.html")]
    [InlineData("kb/deep/nested.html", "index.html", "../../index.html")]
    [InlineData("a/b/c.html", "x/y/z.html", "../../x/y/z.html")]
    public void RelativeHrefWalksUpOnlyAsFarAsItMust(string from, string to, string expected) =>
        Assert.Equal(expected, OkfSiteBuilder.RelativeHref(from, to));

    [Fact]
    public void ABundleRootIndexAlsoRendersWithItsLinksResolvedFromTheSiteRoot()
    {
        using var fixture = new SiteFixture();
        var model = fixture.Build();

        // The landing page carries this body inline at the site root, so its links have to
        // be resolved from there — `kb/hub.html`, not the `hub.html` the index page itself
        // links to one directory down.
        var index = model.Pages.Single(page => page.Href == "kb/index.html");
        Assert.True(index.IsBundleIndex);
        Assert.Contains("href=\"hub.html\"", index.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"kb/hub.html\"", index.RootBodyHtml, StringComparison.Ordinal);

        // Only a bundle's own root index gets one: nothing else is inlined at the root.
        var nestedIndex = model.Pages.Single(page => page.Href == "kb/deep/index.html");
        Assert.False(nestedIndex.IsBundleIndex);
        Assert.Empty(nestedIndex.RootBodyHtml);
        Assert.Empty(model.Pages.Single(page => page.Href == "kb/hub.html").RootBodyHtml);
    }

    [Fact]
    public void SingleFileLinksRouteOnTheFragmentInsteadOfAPath()
    {
        using var fixture = new SiteFixture();
        var hub = fixture.Build(singleFile: true).Pages.Single(page => page.Href == "kb/hub.html");

        Assert.Contains("href=\"#c=kb%2Freviewed\"", hub.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"reviewed.html\"", hub.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleSlugsAreUniqueEvenWhenTwoBundlesShareAName()
    {
        using var first = new TempBundle("kb");
        using var second = new TempBundle("kb");
        first.Add("a.md", "---\ntype: Concept\n---\n\nA.\n");
        second.Add("b.md", "---\ntype: Concept\n---\n\nB.\n");

        var model = OkfSiteBuilder.Build(
            new OkfWorkingSet([first.Bundle, second.Bundle], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today });

        // Two pages that collided on one href would silently overwrite each other on disk.
        Assert.Equal(
            ["kb-2/b.html", "kb/a.html"],
            model.Pages.Select(page => page.Href));
    }

    [Fact]
    public void ABundleNamedAfterTheSitesOwnDirectoryIsMovedOutOfItsWay()
    {
        using var bundle = new TempBundle("assets");
        bundle.Add("a.md", "---\ntype: Concept\n---\n\nA.\n");

        var model = OkfSiteBuilder.Build(
            new OkfWorkingSet([bundle.Bundle], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today });

        // `assets/` is where the stylesheet and the client script go; a bundle that took
        // that name would have its pages and the site's own assets overwrite each other.
        Assert.Equal(["assets-2/a.html"], model.Pages.Select(page => page.Href));
        Assert.NotEqual(OkfSiteAssets.StyleSheetPath, model.Pages[0].Href);
    }

    [Fact]
    public void AWorkingSetWithNoBundlesStillProducesASite()
    {
        var model = OkfSiteBuilder.Build(
            new OkfWorkingSet([], null, "fixture"),
            new OkfSiteOptions { Today = SiteFixture.Today });

        // Nothing to show is a site that says so, not an IndexOutOfRangeException: `Build`
        // is library API, and a caller may hand it whatever a filter left behind.
        Assert.Empty(model.Pages);
        Assert.Equal(0, model.Counts.Concepts);
        Assert.NotEmpty(model.Name);
    }

    [Fact]
    public void TheFilterPayloadCarriesEveryFieldTheTilesFilterOn()
    {
        using var fixture = new SiteFixture();
        var plan = fixture.Plan();
        var data = plan.Files.Single(file => file.Path == OkfSiteAssets.DataPath).Content;

        var json = data["window.OKF_SITE = ".Length..].TrimEnd('\n', ';');
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Fixture", root.GetProperty("name").GetString());
        Assert.Equal(7, root.GetProperty("counts").GetProperty("concepts").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("stale").GetInt32());

        var concepts = root.GetProperty("concepts").EnumerateArray().ToList();
        Assert.Equal(7, concepts.Count);

        var hub = concepts.Single(concept => concept.GetProperty("id").GetString() == "kb/hub");
        Assert.Equal("kb", hub.GetProperty("bundle").GetString());
        Assert.Equal("Hub", hub.GetProperty("title").GetString());
        Assert.Equal("Concept", hub.GetProperty("type").GetString());
        Assert.Equal("unverified", hub.GetProperty("tier").GetString());
        Assert.Equal("stable", hub.GetProperty("status").GetString());
        Assert.False(hub.GetProperty("stale").GetBoolean());
        Assert.Equal("kb/hub.html", hub.GetProperty("href").GetString());
        // The tag filter runs off this array, so it is the one field whose absence would
        // make a clicked tag badge silently match nothing.
        Assert.Equal(["a", "b"], hub.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.All(
            concepts,
            concept => Assert.Equal(JsonValueKind.Array, concept.GetProperty("tags").ValueKind));

        var expired = concepts.Single(concept => concept.GetProperty("id").GetString() == "kb/expired");
        Assert.True(expired.GetProperty("stale").GetBoolean());

        var rough = concepts.Single(concept => concept.GetProperty("id").GetString() == "kb/rough");
        Assert.Equal("draft", rough.GetProperty("status").GetString());

        // The description survives the round trip intact even though the payload may not
        // contain either character raw — the check below would be vacuous without it.
        Assert.Equal("Draft notes on <tags> & ampersands.", rough.GetProperty("description").GetString());

        // Edges are pairs of concept ids, which is what the graph indexes on.
        var edges = root.GetProperty("edges").EnumerateArray()
            .Select(edge => (edge[0].GetString(), edge[1].GetString()))
            .ToList();
        Assert.Contains(("kb/hub", "kb/reviewed"), edges);
        Assert.Contains(("kb/reviewed", "kb/hub"), edges);
        Assert.DoesNotContain(edges, edge => edge.Item1!.EndsWith("index", StringComparison.Ordinal));

        // The payload must not be able to close the <script> element that carries it, nor
        // break the XML parse the smoke test performs.
        Assert.DoesNotContain("<", json, StringComparison.Ordinal);
        Assert.DoesNotContain("&", json, StringComparison.Ordinal);
    }

    /// <summary>Every value of one attribute in a fragment of markup.</summary>
    /// <param name="html">The markup to read.</param>
    /// <param name="attribute">The attribute's name and its opening quote.</param>
    /// <returns>The values, in document order.</returns>
    private static IEnumerable<string> Values(string html, string attribute)
    {
        var index = html.IndexOf(attribute, StringComparison.Ordinal);
        while (index >= 0)
        {
            var start = index + attribute.Length;
            var end = html.IndexOf('"', start);
            if (end < 0)
            {
                yield break;
            }

            yield return html[start..end];
            index = html.IndexOf(attribute, end, StringComparison.Ordinal);
        }
    }
}
