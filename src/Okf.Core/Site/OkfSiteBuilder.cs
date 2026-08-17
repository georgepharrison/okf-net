using System.Text;

namespace Okf.Core.Site;

/// <summary>Where site generation reads its inputs from, so tests need no clock and no disk.</summary>
public sealed class OkfSiteOptions
{
    /// <summary>The site's display name. Unset falls back to the vault or bundle name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The date staleness is judged against (§5.5). Injected rather than read from the
    /// clock, so a generated site is a function of the vault alone.
    /// </summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Whether to resolve internal links for the single-file emitter (<c>#c=&lt;id&gt;</c>)
    /// rather than for the multi-page one (a relative <c>.html</c> path).
    /// </summary>
    public bool SingleFile { get; set; }

    /// <summary>Supplies a file's text. Returning <see langword="null" /> falls back to reading the file.</summary>
    public Func<string, string?>? ReadText { get; set; }
}

/// <summary>
/// Turns a resolved working set into an <see cref="OkfSiteModel" />: one page per markdown
/// file, frontmatter read into the fields the metadata panel shows, cross-links resolved,
/// backlinks inverted, and the dashboard's counts totalled.
/// </summary>
/// <remarks>
/// <para>Everything here reuses <c>Okf.Core</c>'s own reading: <see cref="OkfBundle" /> walks
/// the tree, <see cref="OkfConceptReader" /> parses a document, and trust and staleness come
/// from <see cref="OkfDocument" />. The site never re-implements a judgement the library
/// already makes, so a page and <c>okf lint</c> can never disagree about a concept's tier.</para>
/// <para><b>Only concepts are counted, graphed and back-linked.</b> A generated
/// <c>index.md</c> links every concept in its directory; letting those links into the graph
/// would connect everything to everything, and letting them into "Cited by" would tell a
/// reader that the concept is cited by its own table of contents. Indexes and logs are still
/// rendered as pages, and their links are still rewired, because that is what makes the site
/// browsable.</para>
/// </remarks>
public static class OkfSiteBuilder
{
    /// <summary>The lifecycle status a concept has when <c>status</c> is absent (§5.4).</summary>
    public const string DefaultStatus = "stable";

    /// <summary>
    /// The site's landing page: each bundle's root index, rendered, under a compact strip of
    /// trust tiles. Progressive disclosure is the front door (§4 search doctrine) — the
    /// dashboard is where a tile takes you, not what greets you.
    /// </summary>
    public const string IndexHref = "index.html";

    /// <summary>The trust dashboard: the tiles as filters, the filter bar and the concept list.</summary>
    public const string DashboardHref = "dashboard.html";

    /// <summary>The graph page.</summary>
    public const string GraphHref = "graph.html";

    /// <summary>Builds the model for a working set.</summary>
    /// <param name="workingSet">The bundles to render.</param>
    /// <param name="options">The site name, the staleness date, and the emitter mode.</param>
    /// <returns>The model.</returns>
    /// <exception cref="IOException">A file in a bundle could not be read.</exception>
    public static OkfSiteModel Build(OkfWorkingSet workingSet, OkfSiteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workingSet);
        options ??= new OkfSiteOptions();

        (List<OkfSiteBundle> bundles, List<OkfSitePage> pages) = ReadBundles(workingSet, options);
        SortPages(pages);

        Dictionary<string, OkfSitePage> byId = PageLookup(pages);
        LinkGraph graph = RenderPages(pages, byId, options.SingleFile);
        ApplyBacklinks(pages, graph.Backlinks);
        List<OkfSitePage> concepts = pages.Where(page => page.IsConcept).ToList();
        OkfSiteCounts counts = Count(concepts, bundles.Count);
        return new OkfSiteModel(
            SiteName(workingSet, options.Name),
            options.Today,
            options.SingleFile,
            bundles,
            pages,
            graph.Edges,
            counts);
    }

    private static void SortPages(List<OkfSitePage> pages) =>
        pages.Sort(static (left, right) => string.CompareOrdinal(left.Href, right.Href));

    private static Dictionary<string, OkfSitePage> PageLookup(IEnumerable<OkfSitePage> pages) =>
        pages.ToDictionary(page => page.Id, StringComparer.Ordinal);

    private static (List<OkfSiteBundle> Bundles, List<OkfSitePage> Pages) ReadBundles(
        OkfWorkingSet workingSet,
        OkfSiteOptions options)
    {
        List<OkfSitePage> pages = new List<OkfSitePage>();
        List<OkfSiteBundle> bundles = new List<OkfSiteBundle>();
        HashSet<string> slugs = TakenSlugs();
        OkfConceptOptions conceptOptions = new OkfConceptOptions { Today = options.Today, ReadText = options.ReadText };

        foreach (OkfBundle bundle in workingSet.Bundles)
        {
            string slug = UniqueSlug(bundle.Name, slugs);
            int first = pages.Count;
            AddBundlePages(bundle, slug, pages, conceptOptions);
            bundles.Add(BundleSummary(bundle, slug, pages, first));
        }

        return (bundles, pages);
    }

    private static HashSet<string> TakenSlugs() => new(StringComparer.Ordinal)
    {
        "assets",
        IndexHref,
        DashboardHref,
        GraphHref,
    };

    private static void AddBundlePages(
        OkfBundle bundle,
        string slug,
        List<OkfSitePage> pages,
        OkfConceptOptions conceptOptions)
    {
        foreach (string file in bundle.MarkdownFiles())
        {
            string relative = bundle.RelativePath(file);
            OkfConceptResult result = OkfConceptReader.Read(bundle, relative, conceptOptions);
            if (result.Concept is { } concept)
            {
                pages.Add(Page(slug, bundle.Name, relative, concept));
            }
        }
    }

    private static OkfSiteBundle BundleSummary(
        OkfBundle bundle,
        string slug,
        List<OkfSitePage> pages,
        int firstPageIndex)
    {
        int conceptCount = pages.Skip(firstPageIndex).Count(page => page.IsConcept);
        return new OkfSiteBundle(bundle.Name, slug, Href(slug, OkfBundle.IndexFileName), conceptCount);
    }

    private static LinkGraph RenderPages(
        IEnumerable<OkfSitePage> pages,
        Dictionary<string, OkfSitePage> byId,
        bool singleFile)
    {
        GraphBuilder graph = new GraphBuilder(byId);
        foreach (OkfSitePage page in pages)
        {
            OkfSiteBody body = RenderPage(page, byId, singleFile);
            graph.Add(page, body.LinksTo);
        }

        return graph.Build();
    }

    private static OkfSiteBody RenderPage(
        OkfSitePage page,
        Dictionary<string, OkfSitePage> byId,
        bool singleFile)
    {
        string source = Strip(page.Concept.Body, page.Kind);
        LinkContext context = new LinkContext(page, byId, singleFile, page.Href);
        OkfSiteBody body = OkfSiteMarkdown.Render(source, url => Resolve(url, context));
        page.BodyHtml = body.Html;
        page.LinksTo = body.LinksTo;
        page.Sources = Sources(page.Concept.Frontmatter, body.FootnoteOrders);
        page.Crumbs = Crumbs(page, byId, singleFile);
        page.RootBodyHtml = RootBodyHtml(source, body, context);
        return body;
    }

    private static string RootBodyHtml(string source, OkfSiteBody body, LinkContext context)
    {
        if (!context.Page.IsBundleIndex)
        {
            return string.Empty;
        }

        if (context.SingleFile)
        {
            return body.Html;
        }

        return OkfSiteMarkdown.Render(source, url => Resolve(url, context with { FromHref = IndexHref })).Html;
    }

    private static void ApplyBacklinks(
        IEnumerable<OkfSitePage> pages,
        Dictionary<string, List<string>> backlinks)
    {
        foreach (OkfSitePage page in pages)
        {
            page.CitedBy = backlinks.TryGetValue(page.Id, out List<string>? citing) ? citing : [];
        }
    }

    private static OkfSiteCounts Count(List<OkfSitePage> concepts, int bundleCount) =>
        new(
            bundleCount,
            concepts.Count,
            concepts.Count(page => page.TrustTier == OkfTrustTier.HumanReviewed),
            concepts.Count(page => page.TrustTier == OkfTrustTier.MachineConfirmed),
            concepts.Count(page => page.TrustTier == OkfTrustTier.Unverified),
            concepts.Count(page => page.Stale),
            concepts.Count(page => string.Equals(page.Status, "draft", StringComparison.Ordinal)));

    private static string SiteName(OkfWorkingSet workingSet, string? configuredName)
    {
        string name = configuredName is { Length: > 0 } given
            ? given
            : workingSet.VaultRoot is { } vault
                ? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(vault))
                : workingSet.Bundles.Count > 0
                    ? workingSet.Bundles[0].Name
                    : string.Empty;

        return name is { Length: > 0 } ? name : "okf";
    }

    /// <summary>
    /// The site-relative href of a page's HTML file, with each path segment URL-escaped.
    /// </summary>
    /// <param name="slug">The bundle's site path segment.</param>
    /// <param name="relativePath">The bundle-relative markdown path.</param>
    /// <returns>The href.</returns>
    public static string Href(string slug, string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        string withoutExtension = relativePath.EndsWith(".md", StringComparison.Ordinal)
            ? relativePath[..^3]
            : relativePath;

        IEnumerable<string> segments = withoutExtension.Split('/').Select(Uri.EscapeDataString);
        return $"{Uri.EscapeDataString(slug)}/{string.Join('/', segments)}.html";
    }

    /// <summary>
    /// A relative href from one site page to another, so every link works from
    /// <c>file://</c> as well as from a web root.
    /// </summary>
    /// <param name="from">The linking page's href, root-relative.</param>
    /// <param name="to">The linked page's href, root-relative.</param>
    /// <returns>The relative href.</returns>
    public static string RelativeHref(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentException.ThrowIfNullOrEmpty(to);

        string[] fromSegments = from.Split('/');
        string[] toSegments = to.Split('/');
        int shared = SharedPrefixLength(fromSegments, toSegments);
        return Backtrack(fromSegments, shared) + string.Join('/', toSegments.Skip(shared));
    }

    private static int SharedPrefixLength(string[] fromSegments, string[] toSegments)
    {
        int shared = 0;
        while (shared < fromSegments.Length - 1
               && shared < toSegments.Length - 1
               && string.Equals(fromSegments[shared], toSegments[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return shared;
    }

    private static string Backtrack(string[] fromSegments, int shared)
    {
        StringBuilder builder = new StringBuilder();
        for (int index = shared; index < fromSegments.Length - 1; index++)
        {
            builder.Append("../");
        }

        return builder.ToString();
    }

    private static OkfSitePage Page(string slug, string bundleName, string relativePath, OkfConcept concept)
    {
        OkfSitePageKind kind = PageKind(relativePath);
        OkfMapping frontmatter = concept.Frontmatter;

        return new OkfSitePage(
            PageId(slug, relativePath),
            bundleName,
            slug,
            relativePath,
            Href(slug, relativePath),
            PageTitle(kind, bundleName, relativePath, concept),
            kind,
            concept)
        {
            Status = FrontmatterValues.Scalar(frontmatter, "status") ?? DefaultStatus,
            StaleAfter = FrontmatterValues.Scalar(frontmatter, "stale_after"),
            Generated = GeneratedEvent(frontmatter),
            Verified = VerifiedEvents(frontmatter),
        };
    }

    private static OkfSitePageKind PageKind(string relativePath) => System.IO.Path.GetFileName(relativePath) switch
    {
        OkfBundle.IndexFileName => OkfSitePageKind.Index,
        OkfBundle.LogFileName => OkfSitePageKind.Log,
        _ => OkfSitePageKind.Concept,
    };

    private static string PageId(string slug, string relativePath) =>
        $"{slug}/{(relativePath.EndsWith(".md", StringComparison.Ordinal) ? relativePath[..^3] : relativePath)}";

    private static string PageTitle(
        OkfSitePageKind kind,
        string bundleName,
        string relativePath,
        OkfConcept concept) => kind switch
        {
            // An index's own name is its directory: "format", not "index".
            OkfSitePageKind.Index => Directory(relativePath) ?? bundleName,
            OkfSitePageKind.Log when FrontmatterValues.Scalar(concept.Frontmatter, "title") is null => "Update log",
            _ => concept.Title,
        };

    private static OkfSiteEvent? GeneratedEvent(OkfMapping frontmatter) =>
        Event(frontmatter.TryGetValue("generated", out OkfValue? generated) ? generated : null);

    private static IReadOnlyList<OkfSiteEvent> VerifiedEvents(OkfMapping frontmatter) =>
        [.. OkfDocument.NormalizeVerified(frontmatter).Select(Event).OfType<OkfSiteEvent>()];

    private static string? Directory(string relativePath)
    {
        int separator = relativePath.LastIndexOf('/');
        if (separator < 0)
        {
            return null;
        }

        string parent = relativePath[..separator];
        int previous = parent.LastIndexOf('/');
        return previous < 0 ? parent : parent[(previous + 1)..];
    }

    private static string Strip(string body, OkfSitePageKind kind)
    {
        // Raw HTML is escaped rather than emitted, so okf-net's own generated-index marker
        // would otherwise appear on the page as literal text. It is a machine marker, and
        // this is the one HTML comment the generator knows the provenance of.
        if (kind != OkfSitePageKind.Index)
        {
            return body;
        }

        string[] lines = body.Split('\n');
        IEnumerable<string> kept = lines.Where(line => !string.Equals(line.Trim(), OkfIndexGenerator.GeneratedMarker, StringComparison.Ordinal));
        return string.Join('\n', kept);
    }

    private static OkfSiteLink Resolve(string url, LinkContext context)
    {
        if (IsInlineTarget(url))
        {
            return new OkfSiteLink(null, null, null, false);
        }

        if (IsExternalTarget(url))
        {
            return new OkfSiteLink(null, null, "external", true);
        }

        LinkTarget target = LinkTarget.Parse(url);
        if (!target.IsMarkdown)
        {
            return new OkfSiteLink(null, null, null, false);
        }

        return ResolvedPath(target, context.Page.Path) is { } resolved
            ? ResolveMarkdownLink(target, resolved, context)
            : new OkfSiteLink(Uri.EscapeDataString(url), null, "broken", false);
    }

    private static string? ResolvedPath(LinkTarget target, string pagePath)
    {
        string basePath = target.IsBundleRootRelative ? string.Empty : ParentOf(pagePath);
        string relative = Uri.UnescapeDataString(target.Path.TrimStart('/'));
        return Normalize(basePath, relative);
    }

    private static bool IsInlineTarget(string url) => url.StartsWith('#');

    private static bool IsExternalTarget(string url) =>
        url.Contains("://", StringComparison.Ordinal)
        || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("//", StringComparison.Ordinal);

    private static OkfSiteLink ResolveMarkdownLink(LinkTarget target, string resolved, LinkContext context)
    {
        string id = $"{context.Page.BundleSlug}/{resolved[..^3]}";
        if (!context.Pages.TryGetValue(id, out OkfSitePage? destination))
        {
            return BrokenLink(target, resolved, context.Page);
        }

        string href = context.SingleFile
            ? "#c=" + Uri.EscapeDataString(destination.Id)
            : RelativeHref(context.FromHref, destination.Href) + target.Fragment;

        return new OkfSiteLink(href, destination.Id, null, false);
    }

    private static OkfSiteLink BrokenLink(LinkTarget target, string resolved, OkfSitePage page) =>
        new(
            target.IsBundleRootRelative ? RelativeHref(page.Path, resolved) + target.Fragment : null,
            null,
            "broken",
            false);

    private static string ParentOf(string relativePath)
    {
        int separator = relativePath.LastIndexOf('/');
        return separator < 0 ? string.Empty : relativePath[..separator];
    }

    private static string? Normalize(string basePath, string target)
    {
        List<string> segments = BaseSegments(basePath);
        foreach (string segment in target.Split('/'))
        {
            if (!ApplySegment(segments, segment))
            {
                return null;
            }
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static List<string> BaseSegments(string basePath) =>
        basePath.Length > 0 ? [.. basePath.Split('/')] : [];

    private static bool ApplySegment(List<string> segments, string segment)
    {
        switch (segment)
        {
            case "" or ".":
                return true;

            case "..":
                return PopSegment(segments);

            default:
                segments.Add(segment);
                return true;
        }
    }

    private static bool PopSegment(List<string> segments)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        segments.RemoveAt(segments.Count - 1);
        return true;
    }

#pragma warning disable CA1859 // return flows straight into OkfSitePage.Crumbs, frozen public API (2026-08-15)
    private static IReadOnlyList<OkfSiteCrumb> Crumbs(
        OkfSitePage page,
        Dictionary<string, OkfSitePage> pages,
        bool singleFile)
#pragma warning restore CA1859
    {
        List<OkfSiteCrumb> crumbs = new List<OkfSiteCrumb>();
        CrumbContext context = new CrumbContext(page, pages, singleFile, page.Path.Split('/'));

        for (int level = 0; level < CrumbDepth(context); level++)
        {
            crumbs.Add(Crumb(level, context));
        }

        crumbs.Add(new OkfSiteCrumb(page.Title, null));
        return crumbs;
    }

    private static int CrumbDepth(CrumbContext context) =>
        context.Page.Kind == OkfSitePageKind.Index ? context.Segments.Length - 1 : context.Segments.Length;

    private static OkfSiteCrumb Crumb(int level, CrumbContext context)
    {
        string label = level == 0 ? context.Page.BundleName : context.Segments[level - 1];
        string? href = CrumbHref(level, context);
        return new OkfSiteCrumb(label, href);
    }

    private static string? CrumbHref(int level, CrumbContext context)
    {
        string directory = string.Join('/', context.Segments.Take(level));
        string indexId = $"{context.Page.BundleSlug}/{(directory.Length == 0 ? string.Empty : directory + "/")}index";
        if (!context.Pages.TryGetValue(indexId, out OkfSitePage? index))
        {
            return null;
        }

        return context.SingleFile
            ? "#c=" + Uri.EscapeDataString(index.Id)
            : RelativeHref(context.Page.Href, index.Href);
    }

    private static OkfSiteEvent? Event(OkfValue? value)
    {
        if (value is not OkfMapping mapping)
        {
            return null;
        }

        string? by = FrontmatterValues.Scalar(mapping, "by");
        return by is { Length: > 0 } ? new OkfSiteEvent(by, FrontmatterValues.Scalar(mapping, "at")) : null;
    }

    private static IReadOnlyList<OkfSiteSource> Sources(
        OkfMapping frontmatter,
        IReadOnlyDictionary<string, int> footnotes)
    {
        if (!frontmatter.TryGetValue("sources", out OkfValue? value))
        {
            return [];
        }

        return [.. SourceEntries(value).Select(entry => Source(entry, footnotes))];
    }

    private static IEnumerable<OkfMapping> SourceEntries(OkfValue value) => value switch
    {
        // §5.1 shows a list; a producer writing one source as a bare mapping is the same
        // tolerance `verified` already gets (§5.2).
        OkfMapping mapping => [mapping],
        OkfSequence sequence => sequence.OfType<OkfMapping>(),
        _ => [],
    };

    private static OkfSiteSource Source(OkfMapping entry, IReadOnlyDictionary<string, int> footnotes)
    {
        string id = FrontmatterValues.Scalar(entry, "id") ?? string.Empty;
        return new OkfSiteSource(
            id,
            FrontmatterValues.Scalar(entry, "title"),
            FrontmatterValues.Scalar(entry, "resource"),
            FrontmatterValues.Scalar(entry, "author"),
            FrontmatterValues.Scalar(entry, "last_modified"),
            FrontmatterValues.Scalar(entry, "usage_count"),
            id.Length > 0 && footnotes.TryGetValue(id, out int order) ? order : null);
    }

    private static string UniqueSlug(string name, HashSet<string> taken)
    {
        string slug = SlugStem(name);
        string candidate = slug;
        int suffix = 2;

        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            suffix++;
        }

        return candidate;
    }

    private static string SlugStem(string name)
    {
        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-');
        }

        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 || slug is "." or ".." ? "bundle" : slug;
    }

    private sealed record LinkGraph(List<OkfSiteEdge> Edges, Dictionary<string, List<string>> Backlinks);

    private sealed record LinkContext(
        OkfSitePage Page,
        Dictionary<string, OkfSitePage> Pages,
        bool SingleFile,
        string FromHref);

    private sealed record CrumbContext(
        OkfSitePage Page,
        Dictionary<string, OkfSitePage> Pages,
        bool SingleFile,
        string[] Segments);

    private sealed class GraphBuilder(Dictionary<string, OkfSitePage> byId)
    {
        private readonly List<OkfSiteEdge> _edges = [];
        private readonly Dictionary<string, List<string>> _backlinks = new(StringComparer.Ordinal);

        public void Add(OkfSitePage page, IEnumerable<string> targets)
        {
            foreach (string target in targets)
            {
                Add(page, target);
            }
        }

        public LinkGraph Build() => new LinkGraph(_edges, _backlinks);

        private void Add(OkfSitePage page, string target)
        {
            if (!page.IsConcept || !byId.TryGetValue(target, out OkfSitePage? other) || !other.IsConcept)
            {
                return;
            }

            _edges.Add(new OkfSiteEdge(page.Id, target));
            if (!_backlinks.TryGetValue(target, out List<string>? citing))
            {
                _backlinks[target] = citing = [];
            }

            citing.Add(page.Id);
        }
    }

    private sealed record LinkTarget(string Path, string Fragment, bool IsBundleRootRelative)
    {
        public bool IsMarkdown => Path.Length > 0 && Path.EndsWith(".md", StringComparison.Ordinal);

        public static LinkTarget Parse(string url)
        {
            int hash = url.IndexOf('#', StringComparison.Ordinal);
            string path = hash < 0 ? url : url[..hash];
            string fragment = hash < 0 ? string.Empty : url[hash..];
            return new LinkTarget(path, fragment, path.StartsWith('/'));
        }
    }
}
