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
        var depth = href.Count(character => character == '/');
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
        var builder = new StringBuilder();
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
        var (home, dashboard, graph) = Views(model, root);

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
        var counts = model.Counts;
        var (_, dashboard, graph) = Views(model, root);
        var builder = new StringBuilder();

        builder.Append("<div class=\"page-head\"><h1>").Append(Escape(model.Name)).Append("</h1>\n")
            .Append("<p class=\"lede\">")
            .Append(Plural(counts.Concepts, "concept")).Append(" across ")
            .Append(Plural(counts.Bundles, "bundle"))
            .Append(". Start where the vault starts — the index below. ")
            .Append("Each tile opens the dashboard with that filter already applied.")
            .Append("</p></div>\n");

        builder.Append("<section aria-labelledby=\"strip-heading\">\n")
            .Append("<h2 id=\"strip-heading\" class=\"sr-only\">Trust at a glance</h2>\n")
            .Append("<div class=\"tiles tiles-strip\">\n")
            .Append(TileLink(dashboard, "bundles", "accent", "▦", "Bundles", counts.Bundles))
            .Append(TileLink(dashboard, "all", "accent", "◆", "Concepts", counts.Concepts))
            .Append(TileLink(dashboard, "human-reviewed", "good", "✔", "Human-reviewed", counts.HumanReviewed))
            .Append(TileLink(dashboard, "machine-confirmed", "warning", "◎", "Machine-confirmed", counts.MachineConfirmed))
            .Append(TileLink(dashboard, "unverified", "neutral", "○", "Unverified", counts.Unverified))
            .Append(TileLink(dashboard, "stale", "critical", "△", "Stale", counts.Stale))
            .Append(TileLink(dashboard, "draft", "serious", "✎", "Draft", counts.Draft))
            .Append("</div>\n</section>\n");

        var indexes = model.Pages.Where(page => page.IsBundleIndex)
            .ToDictionary(page => page.BundleSlug, StringComparer.Ordinal);

        builder.Append(model.Bundles.Count > 1
            ? "<div class=\"bundle-picker\">\n"
            : "<div class=\"bundle-picker single\">\n");

        foreach (var bundle in model.Bundles)
        {
            var index = indexes.TryGetValue(bundle.Slug, out var found) ? found : null;

            builder.Append("<section class=\"bundle-index\">\n").Append("<h2>");

            // A bundle whose root index.md is missing (§8 reserves the name; it does not
            // require the file) has no page to link its own heading at.
            if (index is not null)
            {
                builder.Append("<a href=\"").Append(hrefFor(index)).Append("\">")
                    .Append(Escape(bundle.Name)).Append("</a>");
            }
            else
            {
                builder.Append(Escape(bundle.Name));
            }

            builder.Append(" <span class=\"count\">")
                .Append(Plural(bundle.Concepts, "concept")).Append("</span></h2>\n")
                .Append("<div class=\"prose landing-index\">\n");

            if (index is not null && index.RootBodyHtml.Length > 0)
            {
                builder.Append(index.RootBodyHtml);
            }
            else
            {
                // A bundle with no root index.md still has to be openable from the front door;
                // §8 makes index.md reserved, not mandatory.
                builder.Append("<ul>\n");
                foreach (var page in model.Concepts.Where(page => page.BundleSlug == bundle.Slug))
                {
                    builder.Append("<li><a href=\"").Append(hrefFor(page)).Append("\">")
                        .Append(Escape(page.Title)).Append("</a></li>\n");
                }

                builder.Append("</ul>\n");
            }

            builder.Append("</div>\n</section>\n");
        }

        if (model.Bundles.Count == 0)
        {
            builder.Append("<p class=\"empty\">This site covers no bundles.</p>\n");
        }

        builder.Append("</div>\n");

        builder.Append("<p class=\"landing-more\">")
            .Append("<a href=\"").Append(dashboard).Append("\">Browse and filter every concept</a> · ")
            .Append("<a href=\"").Append(graph).Append("\">See the graph</a>")
            .Append("</p>\n");

        return builder.ToString();
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
        var counts = model.Counts;
        var builder = new StringBuilder();

        builder.Append("<div class=\"page-head\"><h1>").Append(Escape(model.Name)).Append("</h1>\n")
            .Append("<p class=\"lede\">")
            .Append(Plural(counts.Concepts, "concept")).Append(" across ")
            .Append(Plural(counts.Bundles, "bundle"))
            .Append(". Every tile below is a filter — click one to narrow the list, click it again to clear it. ")
            .Append("Tags filter too, wherever they appear.")
            .Append("</p></div>\n");

        builder.Append("<section aria-labelledby=\"tiles-heading\">\n")
            .Append("<h2 id=\"tiles-heading\" class=\"sr-only\">Trust dashboard</h2>\n")
            .Append("<div class=\"tiles\">\n");

        builder.Append(Tile("bundles", "accent", "▦", "Bundles", counts.Bundles, "drill down per bundle"));
        builder.Append(Tile("all", "accent", "◆", "Concepts", counts.Concepts, "everything in this site"));
        builder.Append(Tile("human-reviewed", "good", "✔", "Human-reviewed", counts.HumanReviewed, "a human: actor verified it"));
        builder.Append(Tile("machine-confirmed", "warning", "◎", "Machine-confirmed", counts.MachineConfirmed, "verified, but not by a human"));
        builder.Append(Tile("unverified", "neutral", "○", "Unverified", counts.Unverified, "no verified event"));
        builder.Append(Tile("stale", "critical", "△", "Stale", counts.Stale, "past its stale_after"));
        builder.Append(Tile("draft", "serious", "✎", "Draft", counts.Draft, "status: draft"));

        builder.Append("</div>\n<div class=\"bundle-drill\" id=\"bundle-drill\">\n");
        foreach (var bundle in model.Bundles)
        {
            builder.Append("<button type=\"button\" class=\"chip\" data-bundle=\"")
                .Append(Escape(bundle.Name)).Append("\" aria-pressed=\"false\">")
                .Append(Escape(bundle.Name))
                .Append(" <span class=\"count\">")
                .Append(bundle.Concepts.ToString(CultureInfo.InvariantCulture))
                .Append("</span></button>\n");
        }

        builder.Append("</div>\n</section>\n");

        builder.Append("<div class=\"filterbar\">\n")
            .Append("<input id=\"concept-search\" type=\"search\" placeholder=\"Search title, type, tag or description\" aria-label=\"Search concepts\" />\n")
            .Append("<button type=\"button\" class=\"btn\" id=\"filter-reset\">Clear filters</button>\n")
            .Append("</div>\n")
            // Filled by the client with one removable chip per active cross-cutting filter.
            // A tag filter can arrive from any page in the site, so the dashboard has to say
            // out loud which one it is holding and offer a way out of it.
            .Append("<div class=\"active-filters\" id=\"active-filters\" hidden=\"hidden\"></div>\n")
            .Append("<p class=\"filter-state\" id=\"filter-state\"><b>")
            .Append(counts.Concepts.ToString(CultureInfo.InvariantCulture))
            .Append("</b> of ").Append(counts.Concepts.ToString(CultureInfo.InvariantCulture))
            .Append(" concepts</p>\n");

        builder.Append("<ul class=\"card-list\" id=\"concept-list\">\n");
        foreach (var page in model.Concepts)
        {
            builder.Append(Card(page, hrefFor(page), tagBase));
        }

        builder.Append("</ul>\n")
            .Append("<p class=\"empty\" id=\"concept-empty\" hidden=\"hidden\">No concept matches this filter.</p>\n");

        return builder.ToString();
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
        var builder = new StringBuilder();

        builder.Append("<nav class=\"crumbs\" aria-label=\"Breadcrumb\"><ol>\n")
            .Append("<li><a href=\"").Append(homeHref).Append("\">").Append(Escape(model.Name)).Append("</a></li>\n");

        foreach (var crumb in page.Crumbs)
        {
            builder.Append("<li>");
            if (crumb.Href is { Length: > 0 } href)
            {
                builder.Append("<a href=\"").Append(href).Append("\">").Append(Escape(crumb.Label)).Append("</a>");
            }
            else
            {
                builder.Append(Escape(crumb.Label));
            }

            builder.Append("</li>\n");
        }

        builder.Append("</ol></nav>\n");

        builder.Append("<div class=\"page-head\"><h1>").Append(Escape(page.Title)).Append("</h1>\n");
        if (page.Description is { Length: > 0 } description)
        {
            builder.Append("<p class=\"lede\">").Append(Escape(description)).Append("</p>\n");
        }

        // Tags live in their own panel, not here as well: they are one list, and a concept
        // that repeated it under its own title would make the reader check whether the two
        // agree. What sits here is classification — stated, never clicked.
        builder.Append("<dl class=\"facts\">").Append(Facts(page)).Append("</dl>\n</div>\n");

        builder.Append("<div class=\"concept-layout\">\n<article class=\"prose\">\n")
            .Append(page.BodyHtml)
            .Append("</article>\n<aside class=\"meta-panel\">\n");

        builder.Append(AboutPanel(page));
        builder.Append(TagsPanel(page, tagBase));
        builder.Append(SourcesPanel(page));
        builder.Append(BacklinksPanel(page, model, hrefFor));

        builder.Append("</aside>\n</div>\n");
        return builder.ToString();
    }

    /// <summary>Renders <c>window.OKF_SITE</c>, the data the dashboard and graph run on.</summary>
    /// <param name="model">The site model.</param>
    /// <param name="hrefFor">Where a concept's page lives, as seen from the site root.</param>
    /// <returns>The JavaScript assignment.</returns>
    public static string SiteData(OkfSiteModel model, Func<OkfSitePage, string> hrefFor)
    {
        using var buffer = new MemoryStream();

        // The default encoder, deliberately, not UnsafeRelaxedJsonEscaping: it escapes `<`,
        // `>` and `&`, which is what keeps this payload from closing the <script> element it
        // is embedded in and what keeps every generated page parseable as XML.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("name", model.Name);
            writer.WriteString("generated", model.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteBoolean("singleFile", model.SingleFile);

            writer.WriteStartObject("counts");
            writer.WriteNumber("bundles", model.Counts.Bundles);
            writer.WriteNumber("concepts", model.Counts.Concepts);
            writer.WriteNumber("humanReviewed", model.Counts.HumanReviewed);
            writer.WriteNumber("machineConfirmed", model.Counts.MachineConfirmed);
            writer.WriteNumber("unverified", model.Counts.Unverified);
            writer.WriteNumber("stale", model.Counts.Stale);
            writer.WriteNumber("draft", model.Counts.Draft);
            writer.WriteEndObject();

            writer.WriteStartArray("bundles");
            foreach (var bundle in model.Bundles)
            {
                writer.WriteStartObject();
                writer.WriteString("name", bundle.Name);
                writer.WriteString("slug", bundle.Slug);
                writer.WriteString("href", bundle.Href);
                writer.WriteNumber("concepts", bundle.Concepts);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("concepts");
            foreach (var page in model.Concepts)
            {
                writer.WriteStartObject();
                writer.WriteString("id", page.Id);
                writer.WriteString("bundle", page.BundleName);
                writer.WriteString("path", page.Path);
                writer.WriteString("href", hrefFor(page));
                writer.WriteString("title", page.Title);
                writer.WriteString("type", page.Type ?? string.Empty);
                writer.WriteString("description", page.Description ?? string.Empty);
                writer.WriteString("tier", page.TrustTier.ToSpecString());
                writer.WriteString("status", page.Status);
                writer.WriteBoolean("stale", page.Stale);
                writer.WriteStartArray("tags");
                foreach (var tag in page.Tags)
                {
                    writer.WriteStringValue(tag);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("edges");
            foreach (var edge in model.Edges)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(edge.Source);
                writer.WriteStringValue(edge.Target);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return "window.OKF_SITE = " + Encoding.UTF8.GetString(buffer.ToArray()) + ";\n";
    }

    /// <summary>Renders the id-to-article map the single-file router swaps between.</summary>
    /// <param name="articles">Each concept's id and its rendered article.</param>
    /// <returns>The JSON object.</returns>
    public static string Articles(IEnumerable<KeyValuePair<string, string>> articles)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (var article in articles)
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
        var builder = new StringBuilder();
        foreach (var tag in page.Tags)
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
        var builder = new StringBuilder();
        builder.Append("<li class=\"card tier-").Append(page.TrustTier.ToSpecString())
            .Append("\" data-id=\"").Append(Escape(page.Id)).Append("\">")
            .Append("<a class=\"card-title\" href=\"").Append(href).Append("\">")
            .Append(Escape(page.Title)).Append("</a>");

        if (page.Description is { Length: > 0 } description)
        {
            builder.Append("<p>").Append(Escape(description)).Append("</p>");
        }

        // Two rows, because they are two different offers: what the concept *is*, which is
        // read-only, then the tags, every one of which is a filter the reader can press.
        builder.Append("<div class=\"card-meta\"><dl class=\"facts facts-tight\">").Append(Facts(page))
            .Append("</dl><span class=\"path\">").Append(Escape($"{page.BundleName}/{page.Path}")).Append("</span>")
            .Append("</div>");

        if (page.Tags.Count > 0)
        {
            builder.Append("<div class=\"card-tags\"><span class=\"facts-key\">Tags</span>")
                .Append(TagChips(page, tagBase)).Append("</div>");
        }

        return builder.Append("</li>\n").ToString();
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
        var builder = new StringBuilder();

        if (page.Type is { Length: > 0 } type)
        {
            Fact(builder, "Type", tone: null, glyph: null, type);
        }

        Fact(builder, "Trust", Tone(page.TrustTier), Glyph(page.TrustTier), page.TrustTier.ToSpecString());

        if (page.Stale)
        {
            Fact(builder, "Stale", "critical", "△", page.StaleAfter is { Length: > 0 } after ? $"since {after}" : "yes");
        }
        else if (page.StaleAfter is { Length: > 0 } staleAfter)
        {
            Fact(builder, "Fresh until", "neutral", "△", staleAfter);
        }

        if (string.Equals(page.Status, "draft", StringComparison.Ordinal))
        {
            Fact(builder, "Status", "serious", "✎", "draft");
        }
        else if (string.Equals(page.Status, "deprecated", StringComparison.Ordinal))
        {
            Fact(builder, "Status", "neutral", "✖", "deprecated");
        }

        return builder.ToString();
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

        var builder = new StringBuilder().Append("<span class=\"signal");
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
        var builder = new StringBuilder()
            .Append("<section class=\"panel\"><h2>About this page</h2><dl>\n");

        Row(builder, "Type", page.Type is { Length: > 0 } type ? Escape(type) : Missing());
        Row(builder, "Status", Signal(StatusTone(page.Status), StatusGlyph(page.Status), page.Status));
        Row(builder, "Trust", Signal(Tone(page.TrustTier), Glyph(page.TrustTier), page.TrustTier.ToSpecString()));

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

        Row(builder, "Bundle", Escape(page.BundleName));
        Row(builder, "Path", $"<span class=\"path mono\">{Escape(page.Path)}</span>");

        return builder.Append("</dl></section>\n").ToString();
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

        var tone = actor.IsHuman ? "good" : "neutral";
        var glyph = actor.IsHuman ? "✔" : "◎";
        var text = actor.At is { Length: > 0 } at ? $"{actor.By} · {at}" : actor.By;
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

        var builder = new StringBuilder()
            .Append("<section class=\"panel\"><h2>Sources</h2><ul class=\"sources\">\n");

        foreach (var source in page.Sources)
        {
            builder.Append("<li class=\"source\"");
            if (source.Id.Length > 0)
            {
                builder.Append(" id=\"src-").Append(Escape(source.Id)).Append('"');
            }

            builder.Append('>');

            var label = source.Title ?? source.Resource ?? source.Id;
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

            if (!source.IsUrl && source.Resource is { Length: > 0 } resource && !string.Equals(resource, label, StringComparison.Ordinal))
            {
                builder.Append("<div class=\"path mono\">").Append(Escape(resource)).Append("</div>");
            }

            builder.Append("<div class=\"signals\">");

            if (source.Author is { Length: > 0 } author)
            {
                builder.Append(Signal(
                    source.IsHumanAuthored ? "good" : "neutral",
                    source.IsHumanAuthored ? "✔" : "◎",
                    author));
            }

            if (source.LastModified is { Length: > 0 } lastModified)
            {
                builder.Append(Signal("neutral", "↻", $"modified {lastModified}"));
            }

            if (source.UsageCount is { Length: > 0 } usageCount)
            {
                builder.Append(Signal("neutral", "∑", $"used {usageCount}"));
            }

            // §5.1: the footnote label is the join key into `sources`. When the body cites
            // this entry, the panel points at the footnote and the footnote's own back-arrow
            // points at the citation, so the join a reader has to make in their head is one
            // click in each direction.
            if (source.FootnoteOrder is { } order)
            {
                builder.Append("<a class=\"cite\" href=\"#fn:")
                    .Append(order.ToString(CultureInfo.InvariantCulture))
                    .Append("\">cited as [^").Append(Escape(source.Id)).Append("]</a>");
            }
            else if (source.Id.Length > 0)
            {
                builder.Append(Signal("neutral", null, "not cited in the body"));
            }

            builder.Append("</div></li>\n");
        }

        return builder.Append("</ul></section>\n").ToString();
    }

    private static string BacklinksPanel(
        OkfSitePage page,
        OkfSiteModel model,
        Func<OkfSitePage, string> hrefFor)
    {
        if (page.CitedBy.Count == 0)
        {
            return string.Empty;
        }

        var byId = model.Pages.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);
        var builder = new StringBuilder()
            .Append("<section class=\"panel\"><h2>Cited by</h2><ul class=\"backlinks\">\n");

        foreach (var id in page.CitedBy)
        {
            if (!byId.TryGetValue(id, out var citing))
            {
                continue;
            }

            builder.Append("<li><a href=\"").Append(hrefFor(citing)).Append("\">")
                .Append(Escape(citing.Title)).Append("</a>")
                .Append("<span class=\"path\">").Append(Escape(id)).Append("</span></li>\n");
        }

        return builder.Append("</ul></section>\n").ToString();
    }

    private static void Row(StringBuilder builder, string term, string definition) =>
        builder.Append("<dt>").Append(Escape(term)).Append("</dt><dd>").Append(definition).Append("</dd>\n");

    private static string Missing() => "<span class=\"empty\">none</span>";
}
