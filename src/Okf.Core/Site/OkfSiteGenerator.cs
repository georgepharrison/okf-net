using System.Text;

namespace Okf.Core.Site;

/// <summary>One file of a generated site.</summary>
/// <param name="Path">The site-relative path, with <c>/</c> separators.</param>
/// <param name="Content">The file's text.</param>
public sealed record OkfSiteFile(string Path, string Content);

/// <summary>A site, rendered but not yet written.</summary>
public sealed class OkfSitePlan
{
    /// <summary>Initializes a plan.</summary>
    /// <param name="model">The model the files were rendered from.</param>
    /// <param name="files">The files, ordered by path.</param>
    public OkfSitePlan(OkfSiteModel model, IReadOnlyList<OkfSiteFile> files)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(files);
        Model = model;
        Files = files;
    }

    /// <summary>The model the files were rendered from.</summary>
    public OkfSiteModel Model { get; }

    /// <summary>The files, ordered by path.</summary>
    public IReadOnlyList<OkfSiteFile> Files { get; }
}

/// <summary>
/// Renders a vault as a static site (PRD: the Obsidian replacement). Nothing here needs a
/// server: every link is relative, so the output works identically from GitLab Pages and
/// from a <c>file://</c> path, and no page makes a network request at runtime.
/// </summary>
/// <remarks>
/// <para><b>Two emitters over one model.</b> The default is multi-page — one HTML file per
/// markdown file, mirroring the bundle tree, so a URL names a concept, the browser's back
/// button works, and a reader can be handed a deep link. <c>--single-file</c> emits the same
/// site as one <c>index.html</c> that carries every article as embedded JSON and routes on
/// the fragment, for handing someone a knowledge base as one attachment. Both consume
/// <see cref="OkfSiteModel" /> and share every HTML fragment; only link resolution differs,
/// which is why the model is built per mode rather than reused across both.</para>
/// <para><b>Nothing is deleted.</b> Generating into a directory overwrites the files it
/// generates and leaves everything else alone: an output directory is the caller's, and a
/// generator that removed files it did not write would be one <c>--out ~/</c> away from a
/// disaster.</para>
/// </remarks>
public static class OkfSiteGenerator
{
    /// <summary>Renders a site without writing anything.</summary>
    /// <param name="workingSet">The bundles to render.</param>
    /// <param name="options">The site name, the staleness date, and the emitter mode.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="IOException">A file in a bundle could not be read.</exception>
    public static OkfSitePlan Plan(OkfWorkingSet workingSet, OkfSiteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workingSet);
        options ??= new OkfSiteOptions();

        OkfSiteModel model = OkfSiteBuilder.Build(workingSet, options);
        IReadOnlyList<OkfSiteFile> files = model.SingleFile ? SingleFile(model) : MultiPage(model);
        return new OkfSitePlan(model, files);
    }

    /// <summary>Writes a plan's files under an output directory.</summary>
    /// <param name="plan">The plan to write.</param>
    /// <param name="outputDirectory">The directory to write into; created when absent.</param>
    /// <returns>The absolute paths written, in plan order.</returns>
    /// <exception cref="IOException">A file could not be written.</exception>
    public static IReadOnlyList<string> Apply(OkfSitePlan plan, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

        string root = Path.GetFullPath(outputDirectory);
        List<string> written = new List<string>(plan.Files.Count);

        foreach (OkfSiteFile file in plan.Files)
        {
            string path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Content, FileText.Utf8NoBom);
            written.Add(path);
        }

        return written;
    }

#pragma warning disable CA1859 // return flows straight into OkfSitePlan.Files, frozen public API (2026-08-15)
    private static IReadOnlyList<OkfSiteFile> MultiPage(OkfSiteModel model)
#pragma warning restore CA1859
    {
        List<OkfSiteFile> files = StaticFiles(model);
        AddRootPages(model, files);
        AddConceptPages(model, files);
        files.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return files;
    }

    private static string PageWithoutData(PageRequest request) =>
        Page(request, ScriptReference(OkfSiteHtml.Root(request.Href)));

    private static string PageWithData(PageRequest request)
    {
        string root = OkfSiteHtml.Root(request.Href);
        return Page(request, DataReference(root) + ScriptReference(root));
    }

    private static string Page(PageRequest request, string scripts)
    {
        string root = OkfSiteHtml.Root(request.Href);
        string head = $"<link rel=\"stylesheet\" href=\"{root}{OkfSiteAssets.StyleSheetPath}\" />\n";
        string content = PageContent(request.Model, root, request.Current, request.Body);
        return OkfSiteHtml.Document(request.Title, request.Description, head, content, scripts);
    }

    private static IReadOnlyList<OkfSiteFile> SingleFile(OkfSiteModel model)
    {
        List<KeyValuePair<string, string>> articles = SingleFileArticles(model);
        string head = "<style>" + OkfSiteHtml.Cdata(OkfSiteAssets.StyleSheet) + "</style>\n";
        string body = SingleFileBody(model);
        string tail = SingleFileTail(model, articles);

        return
        [
            new OkfSiteFile(
                OkfSiteBuilder.IndexHref,
                OkfSiteHtml.Document(
                    model.Name,
                    LandingDescription(model),
                    head,
                    body,
                    tail)),
        ];
    }

    private static List<OkfSiteFile> StaticFiles(OkfSiteModel model) =>
    [
        new OkfSiteFile(OkfSiteAssets.StyleSheetPath, OkfSiteAssets.StyleSheet),
        new OkfSiteFile(OkfSiteAssets.ScriptPath, OkfSiteAssets.Script),
        new OkfSiteFile(OkfSiteAssets.DataPath, OkfSiteHtml.SiteData(model, page => page.Href)),
    ];

    private static void AddRootPages(OkfSiteModel model, List<OkfSiteFile> files)
    {
        files.Add(LandingFile(model));
        files.Add(DashboardFile(model));
        files.Add(GraphFile(model));
    }

    private static OkfSiteFile LandingFile(OkfSiteModel model) =>
        new(
            OkfSiteBuilder.IndexHref,
            PageWithoutData(new PageRequest(
                model,
                OkfSiteBuilder.IndexHref,
                model.Name,
                LandingDescription(model),
                "home",
                OkfSiteHtml.Landing(model, string.Empty, page => page.Href))));

    private static string LandingDescription(OkfSiteModel model) =>
        $"{model.Name}: {OkfSiteHtml.Plural(model.Counts.Concepts, "concept")} across "
        + $"{OkfSiteHtml.Plural(model.Counts.Bundles, "bundle")}, "
        + "browsable from each bundle's index.";

    private static OkfSiteFile DashboardFile(OkfSiteModel model) =>
        new(
            OkfSiteBuilder.DashboardHref,
            PageWithData(new PageRequest(
                model,
                OkfSiteBuilder.DashboardHref,
                $"Dashboard — {model.Name}",
                $"Trust dashboard for {model.Name}: {OkfSiteHtml.Plural(model.Counts.Concepts, "concept")} "
                + $"across {OkfSiteHtml.Plural(model.Counts.Bundles, "bundle")}.",
                "dashboard",
                OkfSiteHtml.HomeCrumbs(model, OkfSiteBuilder.IndexHref, "Dashboard")
                + OkfSiteHtml.Dashboard(model, page => page.Href, "#t="))));

    private static OkfSiteFile GraphFile(OkfSiteModel model) =>
        new(
            OkfSiteBuilder.GraphHref,
            PageWithData(new PageRequest(
                model,
                OkfSiteBuilder.GraphHref,
                $"Graph — {model.Name}",
                $"Force-directed graph of {OkfSiteHtml.Plural(model.Counts.Concepts, "concept")} "
                + $"and {OkfSiteHtml.Plural(model.Edges.Count, "cross-link")}.",
                "graph",
                OkfSiteHtml.HomeCrumbs(model, OkfSiteBuilder.IndexHref, "Graph") + OkfSiteHtml.Graph(model))));

    private static void AddConceptPages(OkfSiteModel model, List<OkfSiteFile> files)
    {
        foreach (OkfSitePage page in model.Pages)
        {
            files.Add(ConceptFile(model, page));
        }
    }

    private static OkfSiteFile ConceptFile(OkfSiteModel model, OkfSitePage page)
    {
        string root = OkfSiteHtml.Root(page.Href);
        string body = OkfSiteHtml.Article(
            page,
            model,
            other => OkfSiteBuilder.RelativeHref(page.Href, other.Href),
            root + OkfSiteBuilder.IndexHref,
            OkfSiteHtml.TagBase(model, root));

        return new OkfSiteFile(
            page.Href,
            PageWithoutData(new PageRequest(
                model,
                page.Href,
                $"{page.Title} — {model.Name}",
                page.Description,
                null,
                body)));
    }

    private static string PageContent(OkfSiteModel model, string root, string? current, string body) =>
        new StringBuilder()
            .Append(OkfSiteHtml.TopBar(model, root, current))
            .Append("<main class=\"shell\">\n")
            .Append(body)
            .Append(OkfSiteHtml.Footer(model))
            .Append("</main>\n")
            .ToString();

    private static string DataReference(string root) =>
        $"<script src=\"{root}{OkfSiteAssets.DataPath}\" defer=\"defer\"></script>\n";

    private static string ScriptReference(string root) =>
        $"<script src=\"{root}{OkfSiteAssets.ScriptPath}\" defer=\"defer\"></script>\n";

    private static List<KeyValuePair<string, string>> SingleFileArticles(OkfSiteModel model)
    {
        string tagBase = OkfSiteHtml.TagBase(model, string.Empty);
        List<KeyValuePair<string, string>> articles = new List<KeyValuePair<string, string>>();
        foreach (OkfSitePage page in model.Pages)
        {
            articles.Add(new KeyValuePair<string, string>(
                page.Id,
                OkfSiteHtml.Article(page, model, other => "#c=" + Uri.EscapeDataString(other.Id), "#", tagBase)));
        }

        return articles;
    }

    private static string SingleFileBody(OkfSiteModel model)
    {
        string tagBase = OkfSiteHtml.TagBase(model, string.Empty);
        return new StringBuilder()
            .Append(OkfSiteHtml.TopBar(model, string.Empty, null))
            .Append("<main class=\"shell\">\n")
            .Append(SingleFileHome(model))
            .Append(SingleFileDashboard(model, tagBase))
            .Append(SingleFileGraph(model))
            .Append("<div class=\"view\" data-view=\"concept\"><div id=\"article-host\"></div></div>\n")
            .Append(OkfSiteHtml.Footer(model))
            .Append("</main>\n")
            .ToString();
    }

    private static string SingleFileHome(OkfSiteModel model) =>
        new StringBuilder()
            .Append("<div class=\"view active\" data-view=\"home\">\n")
            .Append(OkfSiteHtml.Landing(model, string.Empty, page => "#c=" + Uri.EscapeDataString(page.Id)))
            .Append("</div>\n")
            .ToString();

    private static string SingleFileDashboard(OkfSiteModel model, string tagBase) =>
        new StringBuilder()
            .Append("<div class=\"view\" data-view=\"dashboard\">\n")
            .Append(OkfSiteHtml.HomeCrumbs(model, "#", "Dashboard"))
            .Append(OkfSiteHtml.Dashboard(model, page => "#c=" + Uri.EscapeDataString(page.Id), tagBase))
            .Append("</div>\n")
            .ToString();

    private static string SingleFileGraph(OkfSiteModel model) =>
        new StringBuilder()
            .Append("<div class=\"view\" data-view=\"graph\">\n")
            .Append(OkfSiteHtml.HomeCrumbs(model, "#", "Graph"))
            .Append(OkfSiteHtml.Graph(model))
            .Append("</div>\n")
            .ToString();

    private static string SingleFileTail(OkfSiteModel model, IEnumerable<KeyValuePair<string, string>> articles) =>
        new StringBuilder()
            .Append("<script type=\"application/json\" id=\"okf-articles\">")
            .Append(OkfSiteHtml.Articles(articles))
            .Append("</script>\n")
            .Append("<script>")
            .Append(OkfSiteHtml.Cdata(OkfSiteHtml.SiteData(model, page => "#c=" + Uri.EscapeDataString(page.Id))))
            .Append("</script>\n")
            .Append("<script>").Append(OkfSiteHtml.Cdata(OkfSiteAssets.Script)).Append("</script>\n")
            .ToString();

    private sealed record PageRequest(
        OkfSiteModel Model,
        string Href,
        string Title,
        string? Description,
        string? Current,
        string Body);
}
