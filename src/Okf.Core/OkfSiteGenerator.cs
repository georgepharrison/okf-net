using System.Text;

namespace Okf.Core;

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

        var model = OkfSiteBuilder.Build(workingSet, options);
        var files = model.SingleFile ? SingleFile(model) : MultiPage(model);
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

        var root = Path.GetFullPath(outputDirectory);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var written = new List<string>(plan.Files.Count);

        foreach (var file in plan.Files)
        {
            var path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Content, encoding);
            written.Add(path);
        }

        return written;
    }

    private static IReadOnlyList<OkfSiteFile> MultiPage(OkfSiteModel model)
    {
        var files = new List<OkfSiteFile>
        {
            new(OkfSiteAssets.StyleSheetPath, OkfSiteAssets.StyleSheet),
            new(OkfSiteAssets.ScriptPath, OkfSiteAssets.Script),
            new(OkfSiteAssets.DataPath, OkfSiteHtml.SiteData(model, page => page.Href)),
        };

        files.Add(new OkfSiteFile(
            OkfSiteBuilder.IndexHref,
            Page(
                model,
                OkfSiteBuilder.IndexHref,
                model.Name,
                $"Trust dashboard for {model.Name}: {model.Counts.Concepts} concepts across {model.Counts.Bundles} bundles.",
                "dashboard",
                OkfSiteHtml.Dashboard(model, page => page.Href),
                withData: true)));

        files.Add(new OkfSiteFile(
            OkfSiteBuilder.GraphHref,
            Page(
                model,
                OkfSiteBuilder.GraphHref,
                $"Graph — {model.Name}",
                $"Force-directed graph of {model.Counts.Concepts} concepts and {model.Edges.Count} cross-links.",
                "graph",
                OkfSiteHtml.Graph(model),
                withData: true)));

        foreach (var page in model.Pages)
        {
            var root = OkfSiteHtml.Root(page.Href);
            var body = OkfSiteHtml.Article(
                page,
                model,
                other => OkfSiteBuilder.RelativeHref(page.Href, other.Href),
                root + OkfSiteBuilder.IndexHref);

            files.Add(new OkfSiteFile(
                page.Href,
                Page(model, page.Href, $"{page.Title} — {model.Name}", page.Description, null, body, withData: false)));
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return files;
    }

    private static string Page(
        OkfSiteModel model,
        string href,
        string title,
        string? description,
        string? current,
        string body,
        bool withData)
    {
        var root = OkfSiteHtml.Root(href);
        var head = $"<link rel=\"stylesheet\" href=\"{root}{OkfSiteAssets.StyleSheetPath}\" />\n";

        var scripts = new StringBuilder();
        if (withData)
        {
            scripts.Append("<script src=\"").Append(root).Append(OkfSiteAssets.DataPath)
                .Append("\" defer=\"defer\"></script>\n");
        }

        scripts.Append("<script src=\"").Append(root).Append(OkfSiteAssets.ScriptPath)
            .Append("\" defer=\"defer\"></script>\n");

        var content = new StringBuilder()
            .Append(OkfSiteHtml.TopBar(model, root, current))
            .Append("<main class=\"shell\">\n")
            .Append(body)
            .Append(OkfSiteHtml.Footer(model))
            .Append("</main>\n")
            .ToString();

        return OkfSiteHtml.Document(title, description, head, content, scripts.ToString());
    }

    private static IReadOnlyList<OkfSiteFile> SingleFile(OkfSiteModel model)
    {
        var articles = new List<KeyValuePair<string, string>>();
        foreach (var page in model.Pages)
        {
            articles.Add(new KeyValuePair<string, string>(
                page.Id,
                OkfSiteHtml.Article(page, model, other => "#c=" + Uri.EscapeDataString(other.Id), "#")));
        }

        var head = "<style>" + OkfSiteHtml.Cdata(OkfSiteAssets.StyleSheet) + "</style>\n";

        var body = new StringBuilder()
            .Append(OkfSiteHtml.TopBar(model, string.Empty, null))
            .Append("<main class=\"shell\">\n")
            .Append("<div class=\"view active\" data-view=\"dashboard\">\n")
            .Append(OkfSiteHtml.Dashboard(model, page => "#c=" + Uri.EscapeDataString(page.Id)))
            .Append(OkfSiteHtml.Graph(model))
            .Append("</div>\n")
            .Append("<div class=\"view\" data-view=\"concept\"><div id=\"article-host\"></div></div>\n")
            .Append(OkfSiteHtml.Footer(model))
            .Append("</main>\n")
            .ToString();

        var tail = new StringBuilder()
            .Append("<script type=\"application/json\" id=\"okf-articles\">")
            .Append(OkfSiteHtml.Articles(articles))
            .Append("</script>\n")
            .Append("<script>").Append(OkfSiteHtml.Cdata(OkfSiteHtml.SiteData(model, page => "#c=" + Uri.EscapeDataString(page.Id)))).Append("</script>\n")
            .Append("<script>").Append(OkfSiteHtml.Cdata(OkfSiteAssets.Script)).Append("</script>\n")
            .ToString();

        return
        [
            new OkfSiteFile(
                OkfSiteBuilder.IndexHref,
                OkfSiteHtml.Document(
                    model.Name,
                    $"Trust dashboard for {model.Name}: {model.Counts.Concepts} concepts across {model.Counts.Bundles} bundles.",
                    head,
                    body,
                    tail)),
        ];
    }
}
