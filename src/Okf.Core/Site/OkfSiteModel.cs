namespace Okf.Core.Site;

/// <summary>What a generated page was made from (spec §3.1).</summary>
public enum OkfSitePageKind
{
    /// <summary>An ordinary concept document (§4). Only these are counted, filtered and graphed.</summary>
    Concept = 0,

    /// <summary>A reserved <c>index.md</c> (§8). Navigation, not knowledge.</summary>
    Index,

    /// <summary>A reserved <c>log.md</c> (§9).</summary>
    Log,
}

/// <summary>One step of a page's index-hierarchy trail.</summary>
/// <param name="Label">The crumb's text.</param>
/// <param name="Href">Where the crumb points, root-relative, or <see langword="null" /> for the current page.</param>
public sealed record OkfSiteCrumb(string Label, string? Href);

/// <summary>A <c>generated</c> or <c>verified</c> event (§5.2).</summary>
/// <param name="By">The actor, in the §7 convention.</param>
/// <param name="At">When it happened, or <see langword="null" />.</param>
public sealed record OkfSiteEvent(string By, string? At)
{
    /// <summary>Whether the actor is a human (§7): the <c>human:</c> prefix.</summary>
    public bool IsHuman => By.StartsWith(OkfDocument.HumanActorPrefix, StringComparison.Ordinal);
}

/// <summary>
/// One <c>sources</c> entry with its credibility signals (§5.1), plus the footnote it is
/// joined to when the body cites it.
/// </summary>
/// <param name="Id">The <c>id</c>, the join key into body footnotes; empty when absent.</param>
/// <param name="Title">The <c>title</c>, or <see langword="null" />.</param>
/// <param name="Resource">The <c>resource</c> — a URL, a path, or a scope descriptor.</param>
/// <param name="Author">The <c>author</c> credibility signal, in the §7 actor convention.</param>
/// <param name="LastModified">The <c>last_modified</c> recency signal.</param>
/// <param name="UsageCount">The <c>usage_count</c> adoption signal, as written.</param>
/// <param name="FootnoteOrder">
/// The 1-based footnote number the body cites this source with, or <see langword="null" />
/// when the body never cites it.
/// </param>
public sealed record OkfSiteSource(
    string Id,
    string? Title,
    string? Resource,
    string? Author,
    string? LastModified,
    string? UsageCount,
    int? FootnoteOrder)
{
    /// <summary>Whether <see cref="Resource" /> is a followable absolute URL.</summary>
    public bool IsUrl =>
        Resource is { Length: > 0 } resource
        && (resource.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || resource.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the <c>author</c> signal names a human (§7).</summary>
    public bool IsHumanAuthored =>
        Author is { Length: > 0 } author
        && author.StartsWith(OkfDocument.HumanActorPrefix, StringComparison.Ordinal);
}

/// <summary>A directed cross-link between two concepts — one edge of the bundle graph (§6.1).</summary>
/// <param name="Source">The citing concept's site id.</param>
/// <param name="Target">The cited concept's site id.</param>
public sealed record OkfSiteEdge(string Source, string Target);

/// <summary>One bundle in the site, with the counts the dashboard drills into.</summary>
/// <param name="Name">The bundle's directory name.</param>
/// <param name="Slug">The site path segment the bundle's pages live under.</param>
/// <param name="Href">The bundle's landing page, root-relative.</param>
/// <param name="Concepts">How many concepts the bundle contributed.</param>
public sealed record OkfSiteBundle(string Name, string Slug, string Href, int Concepts);

/// <summary>
/// The trust dashboard's numbers (§5.3, §5.4, §5.5). Every count is over concepts only:
/// generated indexes and logs are navigation, not knowledge, and counting them would
/// inflate every tile.
/// </summary>
/// <param name="Bundles">How many bundles the site covers.</param>
/// <param name="Concepts">How many concepts, across every bundle.</param>
/// <param name="HumanReviewed">Concepts at the human-reviewed tier (§5.3).</param>
/// <param name="MachineConfirmed">Concepts at the machine-confirmed tier (§5.3).</param>
/// <param name="Unverified">Concepts with no <c>verified</c> event (§5.3).</param>
/// <param name="Stale">Concepts past their <c>stale_after</c> (§5.5).</param>
/// <param name="Draft">Concepts with <c>status: draft</c> (§5.4).</param>
public sealed record OkfSiteCounts(
    int Bundles,
    int Concepts,
    int HumanReviewed,
    int MachineConfirmed,
    int Unverified,
    int Stale,
    int Draft);

/// <summary>
/// One page of the generated site: a markdown file from a bundle, rendered, with its
/// frontmatter read into the fields the metadata panel shows and its links already
/// resolved against the rest of the site.
/// </summary>
public sealed class OkfSitePage
{
    /// <summary>Initializes a page.</summary>
    /// <param name="id">The site-wide id, <c>&lt;bundle slug&gt;/&lt;concept id&gt;</c>.</param>
    /// <param name="bundleName">The owning bundle's directory name.</param>
    /// <param name="bundleSlug">The owning bundle's site path segment.</param>
    /// <param name="path">The bundle-relative markdown path.</param>
    /// <param name="href">The page's own location, root-relative.</param>
    /// <param name="title">The display title.</param>
    /// <param name="kind">What the page was made from.</param>
    /// <param name="concept">The parsed concept behind the page.</param>
    public OkfSitePage(
        string id,
        string bundleName,
        string bundleSlug,
        string path,
        string href,
        string title,
        OkfSitePageKind kind,
        OkfConcept concept)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(href);
        ArgumentNullException.ThrowIfNull(concept);

        Id = id;
        BundleName = bundleName;
        BundleSlug = bundleSlug;
        Path = path;
        Href = href;
        Title = title;
        Kind = kind;
        Concept = concept;
    }

    /// <summary>The site-wide id: the bundle slug, a slash, and the bundle-relative path minus <c>.md</c>.</summary>
    public string Id { get; }

    /// <summary>The owning bundle's directory name.</summary>
    public string BundleName { get; }

    /// <summary>The owning bundle's site path segment.</summary>
    public string BundleSlug { get; }

    /// <summary>The bundle-relative markdown path, with <c>/</c> separators.</summary>
    public string Path { get; }

    /// <summary>Where the page sits in the site, root-relative and already URL-escaped.</summary>
    public string Href { get; }

    /// <summary>The display title.</summary>
    public string Title { get; }

    /// <summary>What the page was made from.</summary>
    public OkfSitePageKind Kind { get; }

    /// <summary>Whether the page is a concept — the only kind the dashboard and graph see.</summary>
    public bool IsConcept => Kind == OkfSitePageKind.Concept;

    /// <summary>
    /// Whether the page is a bundle's own root <c>index.md</c> — the hierarchy the landing
    /// page opens on.
    /// </summary>
    public bool IsBundleIndex =>
        Kind == OkfSitePageKind.Index && !Path.Contains('/', StringComparison.Ordinal);

    /// <summary>The parsed concept behind the page.</summary>
    public OkfConcept Concept { get; }

    /// <summary>The <c>type</c> (§4.1), or <see langword="null" />.</summary>
    public string? Type => Concept.Type;

    /// <summary>The <c>description</c> (§4.1), or <see langword="null" />.</summary>
    public string? Description => Concept.Description;

    /// <summary>The <c>tags</c> (§4.1).</summary>
    public IReadOnlyList<string> Tags => Concept.Tags;

    /// <summary>The derived trust tier (§5.3).</summary>
    public OkfTrustTier TrustTier => Concept.TrustTier;

    /// <summary>Whether the concept is past its <c>stale_after</c> (§5.5).</summary>
    public bool Stale => Concept.Stale;

    /// <summary>The lifecycle <c>status</c> (§5.4); <c>stable</c> when absent.</summary>
    public string Status { get; init; } = OkfSiteBuilder.DefaultStatus;

    /// <summary>The <c>stale_after</c> date as written, or <see langword="null" />.</summary>
    public string? StaleAfter { get; init; }

    /// <summary>The <c>generated</c> event (§5.2), or <see langword="null" />.</summary>
    public OkfSiteEvent? Generated { get; init; }

    /// <summary>The <c>verified</c> events (§5.2), normalized to a list.</summary>
    public IReadOnlyList<OkfSiteEvent> Verified { get; init; } = [];

    /// <summary>The <c>sources</c> entries with their credibility signals (§5.1).</summary>
    public IReadOnlyList<OkfSiteSource> Sources { get; internal set; } = [];

    /// <summary>The rendered body, with internal links already pointed at site pages.</summary>
    public string BodyHtml { get; internal set; } = string.Empty;

    /// <summary>
    /// The same body with its links resolved from the site root, for the copy the landing page
    /// carries inline. Empty for every page but a bundle's root index.
    /// </summary>
    public string RootBodyHtml { get; internal set; } = string.Empty;

    /// <summary>The site ids this page links to, deduplicated, in first-appearance order.</summary>
    public IReadOnlyList<string> LinksTo { get; internal set; } = [];

    /// <summary>The concept ids that link here — the "Cited by" list.</summary>
    public IReadOnlyList<string> CitedBy { get; internal set; } = [];

    /// <summary>The index-hierarchy trail, the bundle first and this page last.</summary>
    public IReadOnlyList<OkfSiteCrumb> Crumbs { get; internal set; } = [];

    /// <inheritdoc />
    public override string ToString() => Id;
}

/// <summary>
/// Everything a site emitter needs: the pages, the concept subset the dashboard filters,
/// the graph's edges, and the tile counts. Built once per run and rendered by exactly one
/// emitter (multi-page or single-file), because the two resolve internal links differently.
/// </summary>
public sealed class OkfSiteModel
{
    /// <summary>Initializes a model.</summary>
    /// <param name="name">The site's display name.</param>
    /// <param name="today">The date staleness was judged against.</param>
    /// <param name="singleFile">Whether the model was built for the single-file emitter.</param>
    /// <param name="bundles">The bundles covered.</param>
    /// <param name="pages">Every page, ordered by href.</param>
    /// <param name="edges">The concept-to-concept links.</param>
    /// <param name="counts">The dashboard's tile counts.</param>
    public OkfSiteModel(
        string name,
        DateOnly today,
        bool singleFile,
        IReadOnlyList<OkfSiteBundle> bundles,
        IReadOnlyList<OkfSitePage> pages,
        IReadOnlyList<OkfSiteEdge> edges,
        OkfSiteCounts counts)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(counts);

        Name = name;
        Today = today;
        SingleFile = singleFile;
        Bundles = bundles;
        Pages = pages;
        Edges = edges;
        Counts = counts;
        Concepts = [.. pages.Where(page => page.IsConcept)];
    }

    /// <summary>The site's display name.</summary>
    public string Name { get; }

    /// <summary>The date staleness was judged against.</summary>
    public DateOnly Today { get; }

    /// <summary>Whether internal links were resolved for the single-file emitter.</summary>
    public bool SingleFile { get; }

    /// <summary>The bundles covered, in working-set order.</summary>
    public IReadOnlyList<OkfSiteBundle> Bundles { get; }

    /// <summary>Every page, ordered by href.</summary>
    public IReadOnlyList<OkfSitePage> Pages { get; }

    /// <summary>The concept pages only — what the dashboard filters and the graph draws.</summary>
    public IReadOnlyList<OkfSitePage> Concepts { get; }

    /// <summary>The concept-to-concept links, deduplicated.</summary>
    public IReadOnlyList<OkfSiteEdge> Edges { get; }

    /// <summary>The dashboard's tile counts.</summary>
    public OkfSiteCounts Counts { get; }
}
