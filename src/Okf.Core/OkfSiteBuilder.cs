using System.Text;

namespace Okf.Core;

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

    /// <summary>The site's landing page, and the only page that carries the dashboard.</summary>
    public const string IndexHref = "index.html";

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

        var pages = new List<OkfSitePage>();
        var bundles = new List<OkfSiteBundle>();

        // Seeded with the names the site itself occupies at its root, so a bundle called
        // `assets` gets `assets-2` instead of having its pages overwrite the stylesheet.
        var slugs = new HashSet<string>(StringComparer.Ordinal)
        {
            "assets",
            IndexHref,
            GraphHref,
        };
        var conceptOptions = new OkfConceptOptions { Today = options.Today, ReadText = options.ReadText };

        foreach (var bundle in workingSet.Bundles)
        {
            var slug = UniqueSlug(bundle.Name, slugs);
            var first = pages.Count;

            foreach (var file in bundle.MarkdownFiles())
            {
                var relative = bundle.RelativePath(file);
                var result = OkfConceptReader.Read(bundle, relative, conceptOptions);
                if (result.Concept is not { } concept)
                {
                    // Frontmatter that does not parse is already an OKF0001 error; a page
                    // built from it would render the error, not the knowledge.
                    continue;
                }

                pages.Add(Page(slug, bundle.Name, relative, concept));
            }

            var conceptCount = pages.Skip(first).Count(page => page.IsConcept);
            bundles.Add(new OkfSiteBundle(bundle.Name, slug, Href(slug, OkfBundle.IndexFileName), conceptCount));
        }

        pages.Sort(static (left, right) => string.CompareOrdinal(left.Href, right.Href));

        var byId = pages.ToDictionary(page => page.Id, StringComparer.Ordinal);
        var edges = new List<OkfSiteEdge>();
        var backlinks = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            var body = OkfSiteMarkdown.Render(
                Strip(page.Concept.Body, page.Kind),
                url => Resolve(url, page, byId, options.SingleFile));

            page.BodyHtml = body.Html;
            page.LinksTo = body.LinksTo;
            page.Sources = Sources(page.Concept.Frontmatter, body.FootnoteOrders);
            page.Crumbs = Crumbs(page, byId, options.SingleFile);

            foreach (var target in body.LinksTo)
            {
                if (!page.IsConcept || !byId.TryGetValue(target, out var other) || !other.IsConcept)
                {
                    continue;
                }

                edges.Add(new OkfSiteEdge(page.Id, target));
                if (!backlinks.TryGetValue(target, out var citing))
                {
                    backlinks[target] = citing = [];
                }

                citing.Add(page.Id);
            }
        }

        foreach (var page in pages)
        {
            page.CitedBy = backlinks.TryGetValue(page.Id, out var citing) ? citing : [];
        }

        var concepts = pages.Where(page => page.IsConcept).ToList();
        var counts = new OkfSiteCounts(
            bundles.Count,
            concepts.Count,
            concepts.Count(page => page.TrustTier == OkfTrustTier.HumanReviewed),
            concepts.Count(page => page.TrustTier == OkfTrustTier.MachineConfirmed),
            concepts.Count(page => page.TrustTier == OkfTrustTier.Unverified),
            concepts.Count(page => page.Stale),
            concepts.Count(page => string.Equals(page.Status, "draft", StringComparison.Ordinal)));

        var name = options.Name is { Length: > 0 } given
            ? given
            : workingSet.VaultRoot is { } vault
                ? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(vault))
                : workingSet.Bundles.Count > 0
                    ? workingSet.Bundles[0].Name
                    : string.Empty;

        return new OkfSiteModel(
            name is { Length: > 0 } ? name : "okf",
            options.Today,
            options.SingleFile,
            bundles,
            pages,
            edges,
            counts);
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

        var withoutExtension = relativePath.EndsWith(".md", StringComparison.Ordinal)
            ? relativePath[..^3]
            : relativePath;

        var segments = withoutExtension.Split('/').Select(Uri.EscapeDataString);
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

        var fromSegments = from.Split('/');
        var toSegments = to.Split('/');
        var shared = 0;
        while (shared < fromSegments.Length - 1
               && shared < toSegments.Length - 1
               && string.Equals(fromSegments[shared], toSegments[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        var builder = new StringBuilder();
        for (var i = shared; i < fromSegments.Length - 1; i++)
        {
            builder.Append("../");
        }

        builder.AppendJoin('/', toSegments.Skip(shared));
        return builder.ToString();
    }

    private static OkfSitePage Page(string slug, string bundleName, string relativePath, OkfConcept concept)
    {
        var kind = System.IO.Path.GetFileName(relativePath) switch
        {
            OkfBundle.IndexFileName => OkfSitePageKind.Index,
            OkfBundle.LogFileName => OkfSitePageKind.Log,
            _ => OkfSitePageKind.Concept,
        };

        var frontmatter = concept.Frontmatter;
        var title = kind switch
        {
            // An index's own name is its directory: "format", not "index".
            OkfSitePageKind.Index => Directory(relativePath) ?? bundleName,
            OkfSitePageKind.Log when Scalar(frontmatter, "title") is null => "Update log",
            _ => concept.Title,
        };

        return new OkfSitePage(
            $"{slug}/{(relativePath.EndsWith(".md", StringComparison.Ordinal) ? relativePath[..^3] : relativePath)}",
            bundleName,
            slug,
            relativePath,
            Href(slug, relativePath),
            title,
            kind,
            concept)
        {
            Status = Scalar(frontmatter, "status") ?? DefaultStatus,
            StaleAfter = Scalar(frontmatter, "stale_after"),
            Generated = Event(frontmatter.TryGetValue("generated", out var generated) ? generated : null),
            Verified = [.. OkfDocument.NormalizeVerified(frontmatter).Select(Event).OfType<OkfSiteEvent>()],
        };
    }

    private static string? Directory(string relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        if (separator < 0)
        {
            return null;
        }

        var parent = relativePath[..separator];
        var previous = parent.LastIndexOf('/');
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

        var lines = body.Split('\n');
        var kept = lines.Where(line => !string.Equals(line.Trim(), OkfIndexGenerator.GeneratedMarker, StringComparison.Ordinal));
        return string.Join('\n', kept);
    }

    private static OkfSiteLink Resolve(
        string url,
        OkfSitePage page,
        IReadOnlyDictionary<string, OkfSitePage> pages,
        bool singleFile)
    {
        if (url.StartsWith('#'))
        {
            return new OkfSiteLink(null, null, null, false);
        }

        if (url.Contains("://", StringComparison.Ordinal)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("//", StringComparison.Ordinal))
        {
            return new OkfSiteLink(null, null, "external", true);
        }

        var hash = url.IndexOf('#', StringComparison.Ordinal);
        var target = hash < 0 ? url : url[..hash];
        var fragment = hash < 0 ? string.Empty : url[hash..];

        if (target.Length == 0 || !target.EndsWith(".md", StringComparison.Ordinal))
        {
            // Not a concept link: an image, an attachment, a directory. Left exactly as
            // written — §6.1 obliges a consumer to tolerate what it cannot resolve.
            return new OkfSiteLink(null, null, null, false);
        }

        // §6.1: a leading `/` is bundle-root-relative, not host-root-relative.
        var basePath = target.StartsWith('/')
            ? string.Empty
            : ParentOf(page.Path);

        if (Normalize(basePath, Uri.UnescapeDataString(target.TrimStart('/'))) is not { } resolved)
        {
            // A destination that climbs out of its own bundle names no place the site
            // contains, and emitting it unchanged would point a page at whatever sits
            // beside the output directory. Escaped, it is one inert relative segment.
            return new OkfSiteLink(Uri.EscapeDataString(url), null, "broken", false);
        }

        var id = $"{page.BundleSlug}/{resolved[..^3]}";
        if (!pages.TryGetValue(id, out var destination))
        {
            // §6.1: "Consumers MUST tolerate broken links" — a link to knowledge that is
            // not written yet is marked, never dropped. It is still re-expressed relative
            // to this page when it was written bundle-root-relative: a leading `/` left
            // standing is a link to the filesystem root under file:// and to the domain
            // root under Pages, which is the one thing the site's hrefs may never be.
            return new OkfSiteLink(
                target.StartsWith('/') ? RelativeHref(page.Path, resolved) + fragment : null,
                null,
                "broken",
                false);
        }

        var href = singleFile
            // A single-file site spends its whole fragment on routing, so a deep link into
            // a concept's own heading cannot survive the trip. The concept does.
            ? "#c=" + Uri.EscapeDataString(destination.Id)
            : RelativeHref(page.Href, destination.Href) + fragment;

        return new OkfSiteLink(href, destination.Id, null, false);
    }

    private static string ParentOf(string relativePath)
    {
        var separator = relativePath.LastIndexOf('/');
        return separator < 0 ? string.Empty : relativePath[..separator];
    }

    private static string? Normalize(string basePath, string target)
    {
        var segments = new List<string>();
        if (basePath.Length > 0)
        {
            segments.AddRange(basePath.Split('/'));
        }

        foreach (var segment in target.Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;

                case "..":
                    if (segments.Count == 0)
                    {
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    break;

                default:
                    segments.Add(segment);
                    break;
            }
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static IReadOnlyList<OkfSiteCrumb> Crumbs(
        OkfSitePage page,
        IReadOnlyDictionary<string, OkfSitePage> pages,
        bool singleFile)
    {
        var crumbs = new List<OkfSiteCrumb>();
        var segments = page.Path.Split('/');
        var depth = page.Kind == OkfSitePageKind.Index ? segments.Length - 1 : segments.Length;

        // The bundle crumb, then one per directory above the page. An index page names its
        // own directory, so it stops one level short and takes that name for itself.
        for (var level = 0; level < depth; level++)
        {
            var directory = string.Join('/', segments.Take(level));
            var indexId = $"{page.BundleSlug}/{(directory.Length == 0 ? string.Empty : directory + "/")}index";
            string? href = null;
            if (pages.TryGetValue(indexId, out var index))
            {
                href = singleFile ? "#c=" + Uri.EscapeDataString(index.Id) : RelativeHref(page.Href, index.Href);
            }

            var label = level == 0 ? page.BundleName : segments[level - 1];
            crumbs.Add(new OkfSiteCrumb(label, href));
        }

        crumbs.Add(new OkfSiteCrumb(page.Title, null));
        return crumbs;
    }

    private static OkfSiteEvent? Event(OkfValue? value)
    {
        if (value is not OkfMapping mapping)
        {
            return null;
        }

        var by = Scalar(mapping, "by");
        return by is { Length: > 0 } ? new OkfSiteEvent(by, Scalar(mapping, "at")) : null;
    }

    private static IReadOnlyList<OkfSiteSource> Sources(
        OkfMapping frontmatter,
        IReadOnlyDictionary<string, int> footnotes)
    {
        if (!frontmatter.TryGetValue("sources", out var value))
        {
            return [];
        }

        // §5.1 shows a list; a producer writing one source as a bare mapping is the same
        // tolerance `verified` already gets (§5.2).
        IEnumerable<OkfMapping> entries = value switch
        {
            OkfMapping mapping => [mapping],
            OkfSequence sequence => sequence.OfType<OkfMapping>(),
            _ => [],
        };

        return
        [
            .. entries.Select(entry =>
            {
                var id = Scalar(entry, "id") ?? string.Empty;
                return new OkfSiteSource(
                    id,
                    Scalar(entry, "title"),
                    Scalar(entry, "resource"),
                    Scalar(entry, "author"),
                    Scalar(entry, "last_modified"),
                    Scalar(entry, "usage_count"),
                    id.Length > 0 && footnotes.TryGetValue(id, out var order) ? order : null);
            }),
        ];
    }

    private static string? Scalar(OkfMapping mapping, string key) =>
        mapping.TryGetValue(key, out var value) && value is OkfScalar scalar && scalar.IsTruthy
            ? scalar.Value
            : null;

    private static string UniqueSlug(string name, HashSet<string> taken)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '-');
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0 || slug is "." or "..")
        {
            slug = "bundle";
        }

        var candidate = slug;
        var suffix = 2;
        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            suffix++;
        }

        return candidate;
    }
}
