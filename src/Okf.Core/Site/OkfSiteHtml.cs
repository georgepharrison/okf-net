using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Okf.Core.Site;

/// <summary>
/// The site's HTML. Every fragment is built here and shared by both emitters, so a concept
/// reads identically whether it arrived as its own page or as a slice of one file.
/// </summary>
/// <remarks>
/// <para><b>Everything is XML-well-formed.</b> Attributes are always quoted, boolean
/// attributes carry a value, void elements are self-closed, and inline <c>&lt;style&gt;</c>
/// and <c>&lt;script&gt;</c> bodies are wrapped in a comment-hidden CDATA section. That is
/// not pedantry: it is what lets the test suite parse every generated page with a real XML
/// parser and fail on a page okf-net broke, instead of eyeballing output.</para>
/// <para><b>Colour is a status channel.</b> The five trust and lifecycle tiles carry the
/// reserved status palette; every one of them also carries a glyph and a word, so nothing on
/// the dashboard is legible only to a reader who can separate the hues.</para>
/// <para><b>Shape says whether a thing is clickable.</b> The site shows a reader two
/// families that used to look alike: tags, which are filter links, and classification —
/// type, trust tier, staleness, lifecycle — which is a reading of frontmatter and does
/// nothing when clicked. Tags render as chips (<see cref="TagChips" />): pill, <c>#</c>,
/// hover, pointer, always an <c>&lt;a&gt;</c>. Classification renders as facts
/// (<see cref="Facts" />): a real <c>&lt;dl&gt;</c> of key and value, no fill, no border,
/// no hover, no element that takes focus. Nothing carries the distinction in colour alone.
/// </para>
/// </remarks>
internal static class OkfSiteHtml
{
    /// <summary>Escapes text for use as element content or an attribute value.</summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped text.</returns>
    public static string Escape(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>The relative prefix from a page back to the site root.</summary>
    /// <param name="href">The page's root-relative href.</param>
    /// <returns>A prefix of <c>../</c> segments, empty for a page at the root.</returns>
    public static string Root(string href)
    {
        int depth = href.Count(character => character == '/');
        return string.Concat(Enumerable.Repeat("../", depth));
    }

    /// <summary>Wraps CSS or JavaScript so the inline element stays well-formed XML.</summary>
    /// <param name="code">The code to inline.</param>
    /// <returns>The wrapped code.</returns>
    public static string Cdata(string code) => $"/*<![CDATA[*/\n{code}\n/*]]>*/";

    /// <summary>Renders a whole document.</summary>
    /// <param name="title">The document title.</param>
    /// <param name="description">The meta description, or <see langword="null" />.</param>
    /// <param name="head">Extra markup for <c>&lt;head&gt;</c>.</param>
    /// <param name="body">The document body's markup.</param>
    /// <param name="tail">Markup emitted after the body content, typically scripts.</param>
    /// <returns>The document.</returns>
    public static string Document(string title, string? description, string head, string body, string tail)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n")
            .Append("<meta charset=\"utf-8\" />\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n")
            .Append("<title>").Append(Escape(title)).Append("</title>\n");

        if (description is { Length: > 0 })
        {
            builder.Append("<meta name=\"description\" content=\"").Append(Escape(description)).Append("\" />\n");
        }

        builder.Append(head).Append("</head>\n<body>\n").Append(body).Append(tail).Append("</body>\n</html>\n");
        return builder.ToString();
    }

    /// <summary>Where the site's three standing views live, as seen from one page.</summary>
    /// <param name="model">The site model.</param>
    /// <param name="root">The relative prefix back to the site root.</param>
    /// <returns>The home, dashboard and graph hrefs, ready to put in an attribute.</returns>
    /// <remarks>
    /// Single-file mode routes on the fragment rather than on a path, so the same three
    /// destinations exist there under <c>#v=</c>. An <c>&amp;</c> in an attribute value has
    /// to arrive as an entity, or the page stops being well-formed XML.
    /// </remarks>
    public static (string Home, string Dashboard, string Graph) Views(OkfSiteModel model, string root) =>
        model.SingleFile
            ? ("#", "#v=dashboard", "#v=graph")
            : (root + OkfSiteBuilder.IndexHref,
               root + OkfSiteBuilder.DashboardHref,
               root + OkfSiteBuilder.GraphHref);

    /// <summary>
    /// The prefix a tag chip's href is built from: appending an escaped tag to it lands on
    /// the dashboard with that tag filter applied.
    /// </summary>
    /// <param name="model">The site model.</param>
    /// <param name="root">The relative prefix back to the site root.</param>
    /// <returns>The prefix, already safe to put in an attribute.</returns>
    public static string TagBase(OkfSiteModel model, string root) =>
        model.SingleFile
            ? "#v=dashboard&amp;t="
            : root + OkfSiteBuilder.DashboardHref + "#t=";

    /// <summary>Renders the sticky top bar.</summary>
    /// <param name="model">The site model.</param>
    /// <param name="root">The relative prefix back to the site root.</param>
    /// <param name="current">Which nav entry is the current page, or <see langword="null" />.</param>
    /// <returns>The markup.</returns>
    public static string TopBar(OkfSiteModel model, string root, string? current)
    {
        (string home, string dashboard, string graph) = Views(model, root);

        return new StringBuilder()
            .Append("<header class=\"topbar\"><div class=\"topbar-inner\">\n")
            .Append("<a class=\"brand\" href=\"").Append(home).Append("\">").Append(Escape(model.Name)).Append("</a>\n")
            .Append("<span class=\"brand-sub\">OKF v0.2 knowledge site</span>\n")
            .Append("<nav class=\"topnav\" aria-label=\"Site\">")
            .Append("<a href=\"").Append(home).Append('"')
            .Append(current == "home" ? " aria-current=\"page\"" : string.Empty).Append(">Home</a>")
            .Append("<a href=\"").Append(dashboard).Append('"')
            .Append(current == "dashboard" ? " aria-current=\"page\"" : string.Empty).Append(">Dashboard</a>")
            .Append("<a href=\"").Append(graph).Append('"')
            .Append(current == "graph" ? " aria-current=\"page\"" : string.Empty).Append(">Graph</a>")
            .Append("</nav>\n</div></header>\n")
            .ToString();
    }

    /// <summary>Renders a two-step breadcrumb whose root is the landing page.</summary>
    /// <param name="model">The site model.</param>
    /// <param name="homeHref">Where the landing page lives, as seen from this page.</param>
    /// <param name="label">The current page's crumb.</param>
    /// <returns>The markup.</returns>
    public static string HomeCrumbs(OkfSiteModel model, string homeHref, string label) =>
        new StringBuilder()
            .Append("<nav class=\"crumbs\" aria-label=\"Breadcrumb\"><ol>\n")
            .Append("<li><a href=\"").Append(homeHref).Append("\">").Append(Escape(model.Name)).Append("</a></li>\n")
            .Append("<li>").Append(Escape(label)).Append("</li>\n")
            .Append("</ol></nav>\n")
            .ToString();

    /// <summary>
    /// Renders the landing page: a compact strip of trust tiles over each bundle's rendered
    /// root index.
    /// </summary>
    /// <param name="model">The site model.</param>
    /// <param name="root">The relative prefix back to the site root (empty; the landing page is the root).</param>
    /// <param name="hrefFor">Where a page lives, as seen from the landing page.</param>
    /// <returns>The markup.</returns>
    /// <remarks>
    /// The front door is the index hierarchy, not the dashboard: OKF's own doctrine is that a
    /// reader arrives at a vault through progressive disclosure, and the dashboard answers a
    /// different question ("what in here can I trust?"). Every tile is still one click away,
    /// and each one arrives at the dashboard with its own filter already applied.
    /// </remarks>
    public static string Landing(OkfSiteModel model, string root, Func<OkfSitePage, string> hrefFor)
    {
        (string _, string dashboard, string graph) = Views(model, root);
        Dictionary<string, OkfSitePage> indexes = BundleIndexes(model);

        return new StringBuilder()
            .Append(LandingHeader(model))
            .Append(LandingTiles(model.Counts, dashboard))
            .Append(BundleSections(model, indexes, hrefFor))
            .Append(LandingMore(dashboard, graph))
            .ToString();
    }

    /// <summary>Renders the footer.</summary>
    /// <param name="model">The site model.</param>
    /// <returns>The markup.</returns>
    public static string Footer(OkfSiteModel model) =>
        "<footer class=\"site-foot\"><p>Generated by okf from " +
        Plural(model.Counts.Bundles, "bundle") + " on " +
        Escape(model.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) +
        ". Trust tiers, staleness and lifecycle are read from each concept's frontmatter " +
        "(OKF v0.2 §5); nothing here is a score.</p></footer>\n";

    /// <summary>Renders the trust dashboard: the tiles, the filter bar and the concept list.</summary>
    /// <param name="model">The site model.</param>
    /// <param name="hrefFor">Where a concept's page lives, from the dashboard.</param>
    /// <param name="tagBase">The prefix a tag chip's href is built from.</param>
    /// <returns>The markup.</returns>
    public static string Dashboard(OkfSiteModel model, Func<OkfSitePage, string> hrefFor, string tagBase)
    {
        return new StringBuilder()
            .Append(DashboardHeader(model.Counts, model.Name))
            .Append(DashboardTiles(model.Counts))
            .Append(BundleDrill(model.Bundles))
            .Append(FilterBar(model.Counts.Concepts))
            .Append(ConceptList(model.Concepts, hrefFor, tagBase))
            .ToString();
    }

    /// <summary>Renders the graph section.</summary>
    /// <param name="model">The site model.</param>
    /// <returns>The markup.</returns>
    public static string Graph(OkfSiteModel model) =>
        new StringBuilder()
            .Append("<section id=\"graph\">\n")
            .Append("<div class=\"page-head\"><h1>Graph</h1><p class=\"lede\">")
            .Append(Plural(model.Concepts.Count, "concept")).Append(", ")
            .Append(Plural(model.Edges.Count, "cross-link"))
            .Append(". Nodes are coloured by trust tier and ringed when stale; drag to move one, ")
            .Append("click one to open it.</p></div>\n")
            .Append("<div class=\"toolbar\">\n")
            .Append("<button type=\"button\" class=\"btn\" id=\"graph-color\">Colour by type</button>\n")
            .Append("<button type=\"button\" class=\"btn\" id=\"graph-fit\">Fit to view</button>\n")
            .Append("<button type=\"button\" class=\"btn\" id=\"graph-relayout\">Re-run layout</button>\n")
            .Append("</div>\n")
            .Append("<div class=\"graph-wrap\">\n")
            .Append("<canvas id=\"graph-canvas\" role=\"img\" aria-label=\"Force-directed graph of concepts and their cross-links\"></canvas>\n")
            .Append("<div class=\"graph-tip\" id=\"graph-tip\"></div>\n")
            .Append("<div class=\"graph-legend\" id=\"graph-legend\"></div>\n")
            .Append("</div>\n")
            .Append("<noscript><p class=\"noscript-note\">The graph needs JavaScript. Every concept is still reachable from the dashboard list and from each bundle's index.</p></noscript>\n")
            .Append("</section>\n")
            .ToString();

    /// <summary>Renders one concept's article: breadcrumbs, facts, body, panels and backlinks.</summary>
    /// <param name="page">The page to render.</param>
    /// <param name="model">The site model.</param>
    /// <param name="hrefFor">Where another page lives, as seen from this one.</param>
    /// <param name="homeHref">Where the site's landing page lives, as seen from this one.</param>
    /// <param name="tagBase">The prefix a tag chip's href is built from.</param>
    /// <returns>The markup.</returns>
    public static string Article(
        OkfSitePage page,
        OkfSiteModel model,
        Func<OkfSitePage, string> hrefFor,
        string homeHref,
        string tagBase)
    {
        return new StringBuilder()
            .Append(ArticleCrumbs(page, model.Name, homeHref))
            .Append(ArticleHead(page))
            .Append(ArticleLayout(page, model, hrefFor, tagBase))
            .ToString();
    }

    /// <summary>Renders <c>window.OKF_SITE</c>, the data the dashboard and graph run on.</summary>
    /// <param name="model">The site model.</param>
    /// <param name="hrefFor">Where a concept's page lives, as seen from the site root.</param>
    /// <returns>The JavaScript assignment.</returns>
    public static string SiteData(OkfSiteModel model, Func<OkfSitePage, string> hrefFor)
    {
        using MemoryStream buffer = new MemoryStream();

        // The default encoder, deliberately, not UnsafeRelaxedJsonEscaping: it escapes `<`,
        // `>` and `&`, which is what keeps this payload from closing the <script> element it
        // is embedded in and what keeps every generated page parseable as XML.
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("name", model.Name);
            writer.WriteString("generated", model.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteBoolean("singleFile", model.SingleFile);
            WriteCounts(writer, model.Counts);
            WriteBundles(writer, model.Bundles);
            WriteConcepts(writer, model.Concepts, hrefFor);
            WriteEdges(writer, model.Edges);
            writer.WriteEndObject();
        }

        return "window.OKF_SITE = " + Encoding.UTF8.GetString(buffer.ToArray()) + ";\n";
    }

    /// <summary>Renders the id-to-article map the single-file router swaps between.</summary>
    /// <param name="articles">Each concept's id and its rendered article.</param>
    /// <returns>The JSON object.</returns>
    public static string Articles(IEnumerable<KeyValuePair<string, string>> articles)
    {
        using MemoryStream buffer = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> article in articles)
            {
                writer.WriteString(article.Key, article.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Renders "N thing" or "N things".</summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The singular noun.</param>
    /// <returns>The phrase.</returns>
    public static string Plural(int count, string singular) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : singular + "s")}";

    private static Dictionary<string, OkfSitePage> BundleIndexes(OkfSiteModel model) =>
        model.Pages.Where(page => page.IsBundleIndex)
            .ToDictionary(page => page.BundleSlug, StringComparer.Ordinal);

    private static string LandingHeader(OkfSiteModel model) =>
        new StringBuilder()
            .Append("<div class=\"page-head\"><h1>").Append(Escape(model.Name)).Append("</h1>\n")
            .Append("<p class=\"lede\">")
            .Append(Plural(model.Counts.Concepts, "concept")).Append(" across ")
            .Append(Plural(model.Counts.Bundles, "bundle"))
            .Append(". Start where the vault starts — the index below. ")
            .Append("Each tile opens the dashboard with that filter already applied.")
            .Append("</p></div>\n")
            .ToString();

    private static string LandingTiles(OkfSiteCounts counts, string dashboard) =>
        new StringBuilder()
            .Append("<section aria-labelledby=\"strip-heading\">\n")
            .Append("<h2 id=\"strip-heading\" class=\"sr-only\">Trust at a glance</h2>\n")
            .Append("<div class=\"tiles tiles-strip\">\n")
            .Append(TileLink(dashboard, "bundles", "accent", "▦", "Bundles", counts.Bundles))
            .Append(TileLink(dashboard, "all", "accent", "◆", "Concepts", counts.Concepts))
            .Append(TileLink(dashboard, "human-reviewed", "good", "✔", "Human-reviewed", counts.HumanReviewed))
            .Append(TileLink(dashboard, "machine-confirmed", "warning", "◎", "Machine-confirmed", counts.MachineConfirmed))
            .Append(TileLink(dashboard, "unverified", "neutral", "○", "Unverified", counts.Unverified))
            .Append(TileLink(dashboard, "stale", "critical", "△", "Stale", counts.Stale))
            .Append(TileLink(dashboard, "draft", "serious", "✎", "Draft", counts.Draft))
            .Append("</div>\n</section>\n")
            .ToString();

    private static string BundleSections(
        OkfSiteModel model,
        Dictionary<string, OkfSitePage> indexes,
        Func<OkfSitePage, string> hrefFor)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(model.Bundles.Count > 1
            ? "<div class=\"bundle-picker\">\n"
            : "<div class=\"bundle-picker single\">\n");

        foreach (OkfSiteBundle bundle in model.Bundles)
        {
            OkfSitePage? index = indexes.TryGetValue(bundle.Slug, out OkfSitePage? found) ? found : null;
            builder.Append(BundleSection(model, bundle, index, hrefFor));
        }

        if (model.Bundles.Count == 0)
        {
            builder.Append("<p class=\"empty\">This site covers no bundles.</p>\n");
        }

        return builder.Append("</div>\n").ToString();
    }

    private static string BundleSection(
        OkfSiteModel model,
        OkfSiteBundle bundle,
        OkfSitePage? index,
        Func<OkfSitePage, string> hrefFor)
    {
        StringBuilder builder = new StringBuilder()
            .Append("<section class=\"bundle-index\">\n")
            .Append("<h2>");

        AppendBundleHeading(builder, bundle, index, hrefFor);
        builder.Append(" <span class=\"count\">")
            .Append(Plural(bundle.Concepts, "concept")).Append("</span></h2>\n")
            .Append("<div class=\"prose landing-index\">\n")
            .Append(BundleBody(model, bundle, index, hrefFor))
            .Append("</div>\n</section>\n");

        return builder.ToString();
    }

    private static void AppendBundleHeading(
        StringBuilder builder,
        OkfSiteBundle bundle,
        OkfSitePage? index,
        Func<OkfSitePage, string> hrefFor)
    {
        if (index is not null)
        {
            builder.Append("<a href=\"").Append(hrefFor(index)).Append("\">")
                .Append(Escape(bundle.Name)).Append("</a>");
            return;
        }

        builder.Append(Escape(bundle.Name));
    }

    private static string BundleBody(
        OkfSiteModel model,
        OkfSiteBundle bundle,
        OkfSitePage? index,
        Func<OkfSitePage, string> hrefFor)
    {
        if (index is not null && index.RootBodyHtml.Length > 0)
        {
            return index.RootBodyHtml;
        }

        return BundleFallbackList(model, bundle, hrefFor);
    }

    private static string BundleFallbackList(OkfSiteModel model, OkfSiteBundle bundle, Func<OkfSitePage, string> hrefFor)
    {
        StringBuilder builder = new StringBuilder().Append("<ul>\n");
        foreach (OkfSitePage page in model.Concepts.Where(page => page.BundleSlug == bundle.Slug))
        {
            builder.Append("<li><a href=\"").Append(hrefFor(page)).Append("\">")
                .Append(Escape(page.Title)).Append("</a></li>\n");
        }

        return builder.Append("</ul>\n").ToString();
    }

    private static string LandingMore(string dashboard, string graph) =>
        new StringBuilder()
            .Append("<p class=\"landing-more\">")
            .Append("<a href=\"").Append(dashboard).Append("\">Browse and filter every concept</a> · ")
            .Append("<a href=\"").Append(graph).Append("\">See the graph</a>")
            .Append("</p>\n")
            .ToString();

    private static string DashboardHeader(OkfSiteCounts counts, string name) =>
        new StringBuilder()
            .Append("<div class=\"page-head\"><h1>").Append(Escape(name)).Append("</h1>\n")
            .Append("<p class=\"lede\">")
            .Append(Plural(counts.Concepts, "concept")).Append(" across ")
            .Append(Plural(counts.Bundles, "bundle"))
            .Append(". Every tile below is a filter — click one to narrow the list, click it again to clear it. ")
            .Append("Tags filter too, wherever they appear.")
            .Append("</p></div>\n")
            .ToString();

    private static string DashboardTiles(OkfSiteCounts counts) =>
        new StringBuilder()
            .Append("<section aria-labelledby=\"tiles-heading\">\n")
            .Append("<h2 id=\"tiles-heading\" class=\"sr-only\">Trust dashboard</h2>\n")
            .Append("<div class=\"tiles\">\n")
            .Append(Tile("bundles", "accent", "▦", "Bundles", counts.Bundles, "drill down per bundle"))
            .Append(Tile("all", "accent", "◆", "Concepts", counts.Concepts, "everything in this site"))
            .Append(Tile("human-reviewed", "good", "✔", "Human-reviewed", counts.HumanReviewed, "a human: actor verified it"))
            .Append(Tile("machine-confirmed", "warning", "◎", "Machine-confirmed", counts.MachineConfirmed, "verified, but not by a human"))
            .Append(Tile("unverified", "neutral", "○", "Unverified", counts.Unverified, "no verified event"))
            .Append(Tile("stale", "critical", "△", "Stale", counts.Stale, "past its stale_after"))
            .Append(Tile("draft", "serious", "✎", "Draft", counts.Draft, "status: draft"))
            .Append("</div>\n")
            .ToString();

    private static string BundleDrill(IEnumerable<OkfSiteBundle> bundles)
    {
        StringBuilder builder = new StringBuilder().Append("<div class=\"bundle-drill\" id=\"bundle-drill\">\n");
        foreach (OkfSiteBundle bundle in bundles)
        {
            builder.Append("<button type=\"button\" class=\"chip\" data-bundle=\"")
                .Append(Escape(bundle.Name)).Append("\" aria-pressed=\"false\">")
                .Append(Escape(bundle.Name))
                .Append(" <span class=\"count\">")
                .Append(bundle.Concepts.ToString(CultureInfo.InvariantCulture))
                .Append("</span></button>\n");
        }

        return builder.Append("</div>\n</section>\n").ToString();
    }

    private static string FilterBar(int conceptCount) =>
        new StringBuilder()
            .Append("<div class=\"filterbar\">\n")
            .Append("<input id=\"concept-search\" type=\"search\" placeholder=\"Search title, type, tag or description\" aria-label=\"Search concepts\" />\n")
            .Append("<button type=\"button\" class=\"btn\" id=\"filter-reset\">Clear filters</button>\n")
            .Append("</div>\n")
            .Append("<div class=\"active-filters\" id=\"active-filters\" hidden=\"hidden\"></div>\n")
            .Append("<p class=\"filter-state\" id=\"filter-state\"><b>")
            .Append(conceptCount.ToString(CultureInfo.InvariantCulture))
            .Append("</b> of ").Append(conceptCount.ToString(CultureInfo.InvariantCulture))
            .Append(" concepts</p>\n")
            .ToString();

    private static string ConceptList(
        IEnumerable<OkfSitePage> concepts,
        Func<OkfSitePage, string> hrefFor,
        string tagBase)
    {
        StringBuilder builder = new StringBuilder().Append("<ul class=\"card-list\" id=\"concept-list\">\n");
        foreach (OkfSitePage page in concepts)
        {
            builder.Append(Card(page, hrefFor(page), tagBase));
        }

        return builder.Append("</ul>\n")
            .Append("<p class=\"empty\" id=\"concept-empty\" hidden=\"hidden\">No concept matches this filter.</p>\n")
            .ToString();
    }

    private static string ArticleCrumbs(OkfSitePage page, string siteName, string homeHref)
    {
        StringBuilder builder = new StringBuilder()
            .Append("<nav class=\"crumbs\" aria-label=\"Breadcrumb\"><ol>\n")
            .Append("<li><a href=\"").Append(homeHref).Append("\">").Append(Escape(siteName)).Append("</a></li>\n");

        foreach (OkfSiteCrumb crumb in page.Crumbs)
        {
            builder.Append(ArticleCrumb(crumb));
        }

        return builder.Append("</ol></nav>\n").ToString();
    }

    private static string ArticleCrumb(OkfSiteCrumb crumb)
    {
        StringBuilder builder = new StringBuilder().Append("<li>");
        if (crumb.Href is { Length: > 0 } href)
        {
            builder.Append("<a href=\"").Append(href).Append("\">").Append(Escape(crumb.Label)).Append("</a>");
        }
        else
        {
            builder.Append(Escape(crumb.Label));
        }

        return builder.Append("</li>\n").ToString();
    }

    private static string ArticleHead(OkfSitePage page)
    {
        StringBuilder builder = new StringBuilder()
            .Append("<div class=\"page-head\"><h1>").Append(Escape(page.Title)).Append("</h1>\n");

        if (page.Description is { Length: > 0 } description)
        {
            builder.Append("<p class=\"lede\">").Append(Escape(description)).Append("</p>\n");
        }

        return builder.Append("<dl class=\"facts\">").Append(Facts(page)).Append("</dl>\n</div>\n").ToString();
    }

    private static string ArticleLayout(
        OkfSitePage page,
        OkfSiteModel model,
        Func<OkfSitePage, string> hrefFor,
        string tagBase) =>
        new StringBuilder()
            .Append("<div class=\"concept-layout\">\n<article class=\"prose\">\n")
            .Append(page.BodyHtml)
            .Append("</article>\n<aside class=\"meta-panel\">\n")
            .Append(AboutPanel(page))
            .Append(TagsPanel(page, tagBase))
            .Append(SourcesPanel(page))
            .Append(BacklinksPanel(page, model, hrefFor))
            .Append("</aside>\n</div>\n")
            .ToString();

    private static void WriteCounts(Utf8JsonWriter writer, OkfSiteCounts counts)
    {
        writer.WriteStartObject("counts");
        writer.WriteNumber("bundles", counts.Bundles);
        writer.WriteNumber("concepts", counts.Concepts);
        writer.WriteNumber("humanReviewed", counts.HumanReviewed);
        writer.WriteNumber("machineConfirmed", counts.MachineConfirmed);
        writer.WriteNumber("unverified", counts.Unverified);
        writer.WriteNumber("stale", counts.Stale);
        writer.WriteNumber("draft", counts.Draft);
        writer.WriteEndObject();
    }

    private static void WriteBundles(Utf8JsonWriter writer, IEnumerable<OkfSiteBundle> bundles)
    {
        writer.WriteStartArray("bundles");
        foreach (OkfSiteBundle bundle in bundles)
        {
            writer.WriteStartObject();
            writer.WriteString("name", bundle.Name);
            writer.WriteString("slug", bundle.Slug);
            writer.WriteString("href", bundle.Href);
            writer.WriteNumber("concepts", bundle.Concepts);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteConcepts(
        Utf8JsonWriter writer,
        IEnumerable<OkfSitePage> concepts,
        Func<OkfSitePage, string> hrefFor)
    {
        writer.WriteStartArray("concepts");
        foreach (OkfSitePage page in concepts)
        {
            WriteConcept(writer, page, hrefFor(page));
        }

        writer.WriteEndArray();
    }

    private static void WriteConcept(Utf8JsonWriter writer, OkfSitePage page, string href)
    {
        writer.WriteStartObject();
        writer.WriteString("id", page.Id);
        writer.WriteString("bundle", page.BundleName);
        writer.WriteString("path", page.Path);
        writer.WriteString("href", href);
        writer.WriteString("title", page.Title);
        writer.WriteString("type", page.Type ?? string.Empty);
        writer.WriteString("description", page.Description ?? string.Empty);
        writer.WriteString("tier", page.TrustTier.ToSpecString());
        writer.WriteString("status", page.Status);
        writer.WriteBoolean("stale", page.Stale);
        WriteTags(writer, page.Tags);
        writer.WriteEndObject();
    }

    private static void WriteTags(Utf8JsonWriter writer, IEnumerable<string> tags)
    {
        writer.WriteStartArray("tags");
        foreach (string tag in tags)
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();
    }

    private static void WriteEdges(Utf8JsonWriter writer, IEnumerable<OkfSiteEdge> edges)
    {
        writer.WriteStartArray("edges");
        foreach (OkfSiteEdge edge in edges)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(edge.Source);
            writer.WriteStringValue(edge.Target);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Renders each tag as a chip that is also a filter link: the same destination the
    /// dashboard's tiles use, with a tag filter in the fragment.
    /// </summary>
    /// <remarks>
    /// <para>A chip is the site's shape for "you can press this", shared with the dashboard's
    /// bundle drill-down; the <c>#</c> in front of a tag is drawn by the stylesheet, so the
    /// element's text stays the tag and nothing but the tag.</para>
    /// <para>A tag is data okf-net did not write (PRD ACC-1), so it goes into the href through
    /// <see cref="Uri.EscapeDataString(string)" /> — which leaves no <c>&lt;</c>, <c>&amp;</c>
    /// or quote behind to break out of the attribute or the fragment grammar — and into text
    /// and <c>data-tag</c> through the HTML escaper. The client reads <c>data-tag</c> as an
    /// attribute value and writes it back with <c>textContent</c>, never as markup.</para>
    /// </remarks>
    /// <param name="page">The page whose tags to render.</param>
    /// <param name="tagBase">The prefix a tag chip's href is built from.</param>
    /// <returns>The markup.</returns>
    private static string TagChips(OkfSitePage page, string tagBase)
    {
        StringBuilder builder = new StringBuilder();
        foreach (string tag in page.Tags)
        {
            builder.Append("<a class=\"chip chip-tag\" data-tag=\"").Append(Escape(tag))
                .Append("\" href=\"").Append(tagBase).Append(Uri.EscapeDataString(tag)).Append("\">")
                .Append(Escape(tag)).Append("</a>");
        }

        return builder.ToString();
    }

    private static string TileLink(string dashboardHref, string filter, string tone, string glyph, string label, int value) =>
        new StringBuilder()
            .Append("<a class=\"tile tile-compact tone-").Append(tone)
            .Append("\" data-tone=\"").Append(tone)
            .Append("\" href=\"").Append(dashboardHref)
            .Append(dashboardHref.StartsWith('#') ? "&amp;f=" : "#f=").Append(filter).Append("\">")
            .Append("<span class=\"tile-value\">").Append(value.ToString(CultureInfo.InvariantCulture)).Append("</span>")
            .Append("<span class=\"tile-label\"><span class=\"dot\" aria-hidden=\"true\"></span>")
            .Append("<span class=\"glyph\" aria-hidden=\"true\">").Append(Escape(glyph)).Append("</span>")
            .Append("<span class=\"tile-name\">").Append(Escape(label)).Append("</span></span>")
            .Append("</a>\n")
            .ToString();

    private static string Tile(string filter, string tone, string glyph, string label, int value, string note) =>
        new StringBuilder()
            .Append("<button type=\"button\" class=\"tile tone-").Append(tone)
            .Append("\" data-tone=\"").Append(tone)
            .Append("\" data-filter=\"").Append(filter).Append("\" aria-pressed=\"false\">")
            .Append("<span class=\"tile-value\">").Append(value.ToString(CultureInfo.InvariantCulture)).Append("</span>")
            .Append("<span class=\"tile-label\"><span class=\"dot\" aria-hidden=\"true\"></span>")
            .Append("<span class=\"glyph\" aria-hidden=\"true\">").Append(Escape(glyph)).Append("</span>")
            .Append("<span class=\"tile-name\">").Append(Escape(label)).Append("</span></span>")
            .Append("<span class=\"tile-note\">").Append(Escape(note)).Append("</span>")
            .Append("</button>\n")
            .ToString();

    private static string Card(OkfSitePage page, string href, string tagBase)
    {
        StringBuilder builder = new StringBuilder();
        AppendCardHeading(builder, page, href);
        AppendCardDescription(builder, page);
        AppendCardMeta(builder, page);
        AppendCardTags(builder, page, tagBase);
        return builder.Append("</li>\n").ToString();
    }

    private static void AppendCardHeading(StringBuilder builder, OkfSitePage page, string href)
    {
        builder.Append("<li class=\"card tier-").Append(page.TrustTier.ToSpecString())
            .Append("\" data-id=\"").Append(Escape(page.Id)).Append("\">")
            .Append("<a class=\"card-title\" href=\"").Append(href).Append("\">")
            .Append(Escape(page.Title)).Append("</a>");
    }

    private static void AppendCardDescription(StringBuilder builder, OkfSitePage page)
    {
        if (page.Description is { Length: > 0 } description)
        {
            builder.Append("<p>").Append(Escape(description)).Append("</p>");
        }
    }

    private static void AppendCardMeta(StringBuilder builder, OkfSitePage page)
    {
        // Two rows, because they are two different offers: what the concept *is*, which is
        // read-only, then the tags, every one of which is a filter the reader can press.
        builder.Append("<div class=\"card-meta\"><dl class=\"facts facts-tight\">")
            .Append(Facts(page))
            .Append("</dl><span class=\"path\">")
            .Append(Escape($"{page.BundleName}/{page.Path}"))
            .Append("</span></div>");
    }

    private static void AppendCardTags(StringBuilder builder, OkfSitePage page, string tagBase)
    {
        if (page.Tags.Count > 0)
        {
            builder.Append("<div class=\"card-tags\"><span class=\"facts-key\">Tags</span>")
                .Append(TagChips(page, tagBase)).Append("</div>");
        }
    }

    /// <summary>
    /// Renders a concept's classification — type, trust tier, staleness, lifecycle — as
    /// key-and-value pairs for a <c>&lt;dl class="facts"&gt;</c>.
    /// </summary>
    /// <remarks>
    /// These are readings of frontmatter, not controls: no anchor, no button, no
    /// <c>tabindex</c> and no <c>role</c>, so nothing here can be reached by the keyboard or
    /// announced as interactive. The status hues stay — trust and staleness are the reserved
    /// palette's whole job — but each one still arrives with its own key, glyph and word.
    /// </remarks>
    /// <param name="page">The page to describe.</param>
    /// <returns>The markup.</returns>
    private static string Facts(OkfSitePage page)
    {
        StringBuilder builder = new StringBuilder();
        AppendTypeFact(builder, page);
        AppendTrustFact(builder, page);
        AppendStalenessFact(builder, page);
        AppendStatusFact(builder, page);
        return builder.ToString();
    }

    private static void AppendTypeFact(StringBuilder builder, OkfSitePage page)
    {
        if (page.Type is { Length: > 0 } type)
        {
            Fact(builder, "Type", tone: null, glyph: null, type);
        }
    }

    private static void AppendTrustFact(StringBuilder builder, OkfSitePage page) =>
        Fact(builder, "Trust", Tone(page.TrustTier), Glyph(page.TrustTier), page.TrustTier.ToSpecString());

    private static void AppendStalenessFact(StringBuilder builder, OkfSitePage page)
    {
        if (page.Stale)
        {
            Fact(builder, "Stale", "critical", "△", page.StaleAfter is { Length: > 0 } after ? $"since {after}" : "yes");
            return;
        }

        if (page.StaleAfter is { Length: > 0 } staleAfter)
        {
            Fact(builder, "Fresh until", "neutral", "△", staleAfter);
        }
    }

    private static void AppendStatusFact(StringBuilder builder, OkfSitePage page)
    {
        if (string.Equals(page.Status, "draft", StringComparison.Ordinal))
        {
            Fact(builder, "Status", "serious", "✎", "draft");
            return;
        }

        if (string.Equals(page.Status, "deprecated", StringComparison.Ordinal))
        {
            Fact(builder, "Status", "neutral", "✖", "deprecated");
        }
    }

    private static void Fact(StringBuilder builder, string key, string? tone, string? glyph, string value)
    {
        builder.Append("<div class=\"fact");
        if (tone is { Length: > 0 })
        {
            builder.Append(" tone-").Append(tone);
        }

        builder.Append("\"><dt>").Append(Escape(key)).Append("</dt><dd>")
            .Append(Signal(tone, glyph, value)).Append("</dd></div>");
    }

    /// <summary>Renders a value that carries a status hue: a dot, a glyph and the word.</summary>
    /// <param name="tone">The status tone, or <see langword="null" /> for plain ink.</param>
    /// <param name="glyph">The glyph that repeats the tone without colour.</param>
    /// <param name="text">The value itself.</param>
    /// <returns>The markup.</returns>
    private static string Signal(string? tone, string? glyph, string text)
    {
        if (tone is not { Length: > 0 } && glyph is not { Length: > 0 })
        {
            return Escape(text);
        }

        StringBuilder builder = new StringBuilder().Append("<span class=\"signal");
        if (tone is { Length: > 0 })
        {
            builder.Append(" tone-").Append(tone);
        }

        builder.Append("\"><span class=\"dot\" aria-hidden=\"true\"></span>");

        if (glyph is { Length: > 0 })
        {
            builder.Append("<span class=\"glyph\" aria-hidden=\"true\">").Append(Escape(glyph)).Append("</span>");
        }

        return builder.Append(Escape(text)).Append("</span>").ToString();
    }

    private static string Tone(OkfTrustTier tier) => tier switch
    {
        OkfTrustTier.HumanReviewed => "good",
        OkfTrustTier.MachineConfirmed => "warning",
        _ => "neutral",
    };

    private static string Glyph(OkfTrustTier tier) => tier switch
    {
        OkfTrustTier.HumanReviewed => "✔",
        OkfTrustTier.MachineConfirmed => "◎",
        _ => "○",
    };

    /// <summary>Renders the panel that states what the page is, in full.</summary>
    /// <remarks>
    /// Tags used to be a row inside this list, which is how they came to look like the
    /// classification around them; they now have their own panel with its own heading, so a
    /// narrow layout — where this whole column stacks under the body — says out loud where
    /// the readings stop and the filters start.
    /// </remarks>
    /// <param name="page">The page to describe.</param>
    /// <returns>The markup.</returns>
    private static string AboutPanel(OkfSitePage page)
    {
        StringBuilder builder = new StringBuilder()
            .Append("<section class=\"panel\"><h2>About this page</h2><dl>\n");

        AppendAboutClassification(builder, page);
        AppendAboutLifecycle(builder, page);
        AppendAboutLocation(builder, page);
        return builder.Append("</dl></section>\n").ToString();
    }

    private static void AppendAboutClassification(StringBuilder builder, OkfSitePage page)
    {
        Row(builder, "Type", page.Type is { Length: > 0 } type ? Escape(type) : Missing());
        Row(builder, "Status", Signal(StatusTone(page.Status), StatusGlyph(page.Status), page.Status));
        Row(builder, "Trust", Signal(Tone(page.TrustTier), Glyph(page.TrustTier), page.TrustTier.ToSpecString()));
    }

    private static void AppendAboutLifecycle(StringBuilder builder, OkfSitePage page)
    {
        if (page.StaleAfter is { Length: > 0 } staleAfter)
        {
            Row(
                builder,
                page.Stale ? "Stale since" : "Fresh until",
                Signal(page.Stale ? "critical" : "neutral", "△", staleAfter));
        }

        Row(builder, "Generated", Actor(page.Generated));
        Row(
            builder,
            "Verified",
            page.Verified.Count == 0 ? Missing() : string.Join("<br />", page.Verified.Select(Actor)));
    }

    private static void AppendAboutLocation(StringBuilder builder, OkfSitePage page)
    {
        Row(builder, "Bundle", Escape(page.BundleName));
        Row(builder, "Path", $"<span class=\"path mono\">{Escape(page.Path)}</span>");
    }

    /// <summary>Renders the tag panel: the page's cross-cutting axis (§4.1), every one a filter.</summary>
    /// <param name="page">The page whose tags to render.</param>
    /// <param name="tagBase">The prefix a tag chip's href is built from.</param>
    /// <returns>The markup, empty when the page carries no tags.</returns>
    private static string TagsPanel(OkfSitePage page, string tagBase) =>
        page.Tags.Count == 0
            ? string.Empty
            : new StringBuilder()
                .Append("<section class=\"panel\"><h2>Tags</h2><div class=\"chip-row\">")
                .Append(TagChips(page, tagBase))
                .Append("</div><p class=\"panel-note\">Each one opens the dashboard filtered to it.</p>")
                .Append("</section>\n")
                .ToString();

    private static string Actor(OkfSiteEvent? actor)
    {
        if (actor is null)
        {
            return Missing();
        }

        string tone = actor.IsHuman ? "good" : "neutral";
        string glyph = actor.IsHuman ? "✔" : "◎";
        string text = actor.At is { Length: > 0 } at ? $"{actor.By} · {at}" : actor.By;
        return Signal(tone, glyph, text);
    }

    // `stable` is the default every concept without a `status` key already has (§5.4), so it
    // gets plain ink: spending a status hue on "nothing was said here" would leave the reader
    // with a green tick that means less than the one beside the trust tier.
    private static string? StatusTone(string status) => status switch
    {
        "draft" => "serious",
        "deprecated" => "neutral",
        _ => null,
    };

    private static string? StatusGlyph(string status) => status switch
    {
        "draft" => "✎",
        "deprecated" => "✖",
        _ => null,
    };

    private static string SourcesPanel(OkfSitePage page)
    {
        if (page.Sources.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder()
            .Append("<section class=\"panel\"><h2>Sources</h2><ul class=\"sources\">\n");

        foreach (OkfSiteSource source in page.Sources)
        {
            AppendSource(builder, source);
        }

        return builder.Append("</ul></section>\n").ToString();
    }

    private static void AppendSource(StringBuilder builder, OkfSiteSource source)
    {
        AppendSourceStart(builder, source);
        AppendSourceTitle(builder, source);
        AppendSourceResource(builder, source);
        AppendSourceSignals(builder, source);
        builder.Append("</li>\n");
    }

    private static void AppendSourceStart(StringBuilder builder, OkfSiteSource source)
    {
        builder.Append("<li class=\"source\"");
        if (source.Id.Length > 0)
        {
            builder.Append(" id=\"src-").Append(Escape(source.Id)).Append('"');
        }

        builder.Append('>');
    }

    private static void AppendSourceTitle(StringBuilder builder, OkfSiteSource source)
    {
        string label = SourceLabel(source);
        builder.Append("<div class=\"source-title\">");
        if (source.IsUrl)
        {
            builder.Append("<a class=\"external\" target=\"_blank\" rel=\"noopener noreferrer\" href=\"")
                .Append(Escape(source.Resource)).Append("\">").Append(Escape(label)).Append("</a>");
        }
        else
        {
            builder.Append(Escape(label));
        }

        builder.Append("</div>");
    }

    private static void AppendSourceResource(StringBuilder builder, OkfSiteSource source)
    {
        string label = SourceLabel(source);
        if (!source.IsUrl
            && source.Resource is { Length: > 0 } resource
            && !string.Equals(resource, label, StringComparison.Ordinal))
        {
            builder.Append("<div class=\"path mono\">").Append(Escape(resource)).Append("</div>");
        }
    }

    private static void AppendSourceSignals(StringBuilder builder, OkfSiteSource source)
    {
        builder.Append("<div class=\"signals\">");
        AppendSourceAuthorSignal(builder, source);
        AppendSourceModifiedSignal(builder, source);
        AppendSourceUsageSignal(builder, source);
        AppendSourceCitationSignal(builder, source);
        builder.Append("</div>");
    }

    private static void AppendSourceAuthorSignal(StringBuilder builder, OkfSiteSource source)
    {
        if (source.Author is { Length: > 0 } author)
        {
            builder.Append(Signal(
                source.IsHumanAuthored ? "good" : "neutral",
                source.IsHumanAuthored ? "✔" : "◎",
                author));
        }
    }

    private static void AppendSourceModifiedSignal(StringBuilder builder, OkfSiteSource source)
    {
        if (source.LastModified is { Length: > 0 } lastModified)
        {
            builder.Append(Signal("neutral", "↻", $"modified {lastModified}"));
        }
    }

    private static void AppendSourceUsageSignal(StringBuilder builder, OkfSiteSource source)
    {
        if (source.UsageCount is { Length: > 0 } usageCount)
        {
            builder.Append(Signal("neutral", "∑", $"used {usageCount}"));
        }
    }

    private static void AppendSourceCitationSignal(StringBuilder builder, OkfSiteSource source)
    {
        // §5.1: the footnote label is the join key into `sources`. When the body cites this
        // entry, the panel points at the footnote and the footnote's own back-arrow points
        // at the citation, so the join a reader has to make in their head is one click in
        // each direction.
        if (source.FootnoteOrder is { } order)
        {
            builder.Append("<a class=\"cite\" href=\"#fn:")
                .Append(order.ToString(CultureInfo.InvariantCulture))
                .Append("\">cited as [^").Append(Escape(source.Id)).Append("]</a>");
            return;
        }

        if (source.Id.Length > 0)
        {
            builder.Append(Signal("neutral", null, "not cited in the body"));
        }
    }

    private static string SourceLabel(OkfSiteSource source) => source.Title ?? source.Resource ?? source.Id;

    private static string BacklinksPanel(
        OkfSitePage page,
        OkfSiteModel model,
        Func<OkfSitePage, string> hrefFor)
    {
        if (page.CitedBy.Count == 0)
        {
            return string.Empty;
        }

        return BacklinksList(page, model.Pages.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal), hrefFor);
    }

    private static string BacklinksList(
        OkfSitePage page,
        Dictionary<string, OkfSitePage> byId,
        Func<OkfSitePage, string> hrefFor)
    {
        StringBuilder builder = new StringBuilder()
            .Append("<section class=\"panel\"><h2>Cited by</h2><ul class=\"backlinks\">\n");

        foreach (string id in page.CitedBy)
        {
            if (byId.TryGetValue(id, out OkfSitePage? citing))
            {
                AppendBacklink(builder, id, citing, hrefFor);
            }
        }

        return builder.Append("</ul></section>\n").ToString();
    }

    private static void AppendBacklink(
        StringBuilder builder,
        string id,
        OkfSitePage citing,
        Func<OkfSitePage, string> hrefFor)
    {
        builder.Append("<li><a href=\"").Append(hrefFor(citing)).Append("\">")
            .Append(Escape(citing.Title)).Append("</a>")
            .Append("<span class=\"path\">").Append(Escape(id)).Append("</span></li>\n");
    }

    private static void Row(StringBuilder builder, string term, string definition) =>
        builder.Append("<dt>").Append(Escape(term)).Append("</dt><dd>").Append(definition).Append("</dd>\n");

    private static string Missing() => "<span class=\"empty\">none</span>";
}
