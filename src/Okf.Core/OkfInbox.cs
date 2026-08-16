namespace Okf.Core;

/// <summary>Why a concept is on the inbox (PRD CORE-15, CLI-12).</summary>
public enum OkfInboxReason
{
    /// <summary>
    /// Nobody has acknowledged the current content: <c>generated.at</c> is newer than the
    /// latest <c>verified[].at</c>, there is no verification at all behind a non-human
    /// generation stamp, or the concept carries <c>status: draft</c> (decisions.md §7).
    /// </summary>
    Unacknowledged,

    /// <summary>The concept has reached its <c>stale_after</c> date (§5.5).</summary>
    Stale,

    /// <summary>
    /// A cited source moved after the concept was written — some
    /// <c>sources[].last_modified</c> is later than <c>generated.at</c> (PRD CORE-8).
    /// </summary>
    SourceDrift,
}

/// <summary>Conversions for <see cref="OkfInboxReason" />.</summary>
public static class OkfInboxReasonExtensions
{
    /// <summary>Every reason, in report order.</summary>
    public static IReadOnlyList<OkfInboxReason> All { get; } =
    [
        OkfInboxReason.Unacknowledged,
        OkfInboxReason.Stale,
        OkfInboxReason.SourceDrift,
    ];

    /// <summary>The wire spelling of a reason, which is the JSON contract's value.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Its wire spelling.</returns>
    public static string ToWireString(this OkfInboxReason reason) => reason switch
    {
        OkfInboxReason.Unacknowledged => "unacknowledged",
        OkfInboxReason.Stale => "stale",
        OkfInboxReason.SourceDrift => "source-drift",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>The heading a reason's group is reported under.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Its heading.</returns>
    public static string ToHeading(this OkfInboxReason reason) => reason switch
    {
        OkfInboxReason.Unacknowledged => "Unacknowledged",
        OkfInboxReason.Stale => "Stale",
        OkfInboxReason.SourceDrift => "Source drift",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}

/// <summary>One cited source that moved after its citing concept was written.</summary>
public sealed class OkfDriftedSource
{
    /// <summary>Initializes a drifted source.</summary>
    /// <param name="id">The <c>sources[].id</c>, when the entry declares one.</param>
    /// <param name="resource">The <c>sources[].resource</c>, when the entry declares one.</param>
    /// <param name="lastModified">The <c>last_modified</c> value, as written.</param>
    public OkfDriftedSource(string? id, string? resource, string lastModified)
    {
        ArgumentException.ThrowIfNullOrEmpty(lastModified);
        Id = id;
        Resource = resource;
        LastModified = lastModified;
    }

    /// <summary>The source's citation key (§5.1), or <see langword="null" /> when it has none.</summary>
    public string? Id { get; }

    /// <summary>The source's <c>resource</c> pointer, or <see langword="null" /> when it has none.</summary>
    public string? Resource { get; }

    /// <summary>The <c>last_modified</c> value, as written in the document.</summary>
    public string LastModified { get; }

    /// <summary>What to call the source in a report: its id, else its resource, else "source".</summary>
    public string Display => Id ?? Resource ?? "source";
}

/// <summary>
/// One concept that needs human attention, with everything a reader needs to decide
/// whether to act on it without opening the file.
/// </summary>
public sealed class OkfInboxItem
{
    /// <summary>Initializes an item.</summary>
    /// <param name="concept">The concept the item describes.</param>
    /// <param name="reasons">Why it is on the inbox, in report order.</param>
    /// <param name="generatedBy">The <c>generated.by</c> actor, when recorded.</param>
    /// <param name="generatedAt">The <c>generated.at</c> value, as written.</param>
    /// <param name="verifiedBy">The latest verification's actor, when there is one.</param>
    /// <param name="verifiedAt">The latest verification's timestamp, as written.</param>
    /// <param name="staleAfter">The <c>stale_after</c> value, as written.</param>
    /// <param name="status">The <c>status</c> value, as written.</param>
    /// <param name="ageDays">Whole days from <c>generated.at</c> to today.</param>
    /// <param name="staleDays">Whole days from <c>stale_after</c> to today.</param>
    /// <param name="driftedSources">The sources that moved after the concept was written.</param>
    public OkfInboxItem(
        OkfConcept concept,
        IReadOnlyList<OkfInboxReason> reasons,
        string? generatedBy,
        string? generatedAt,
        string? verifiedBy,
        string? verifiedAt,
        string? staleAfter,
        string? status,
        int? ageDays,
        int? staleDays,
        IReadOnlyList<OkfDriftedSource> driftedSources)
    {
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(driftedSources);

        Concept = concept;
        Reasons = reasons;
        GeneratedBy = generatedBy;
        GeneratedAt = generatedAt;
        VerifiedBy = verifiedBy;
        VerifiedAt = verifiedAt;
        StaleAfter = staleAfter;
        Status = status;
        AgeDays = ageDays;
        StaleDays = staleDays;
        DriftedSources = driftedSources;
    }

    /// <summary>The concept itself, carrying its id, paths, title, type and derived tier.</summary>
    public OkfConcept Concept { get; }

    /// <summary>Why the concept is on the inbox, in <see cref="OkfInboxReasonExtensions.All" /> order.</summary>
    public IReadOnlyList<OkfInboxReason> Reasons { get; }

    /// <summary>The <c>generated.by</c> actor, or <see langword="null" /> when unrecorded.</summary>
    public string? GeneratedBy { get; }

    /// <summary>The <c>generated.at</c> value as written, or <see langword="null" /> when unrecorded.</summary>
    public string? GeneratedAt { get; }

    /// <summary>The latest verification's actor, or <see langword="null" /> when never verified.</summary>
    public string? VerifiedBy { get; }

    /// <summary>The latest verification's timestamp as written, or <see langword="null" />.</summary>
    public string? VerifiedAt { get; }

    /// <summary>The <c>stale_after</c> value as written, or <see langword="null" />.</summary>
    public string? StaleAfter { get; }

    /// <summary>The <c>status</c> value as written, or <see langword="null" />.</summary>
    public string? Status { get; }

    /// <summary>Whole days from <c>generated.at</c> to today, or <see langword="null" /> when there is no stamp.</summary>
    public int? AgeDays { get; }

    /// <summary>Whole days since <c>stale_after</c>, or <see langword="null" /> when the concept is not stale.</summary>
    public int? StaleDays { get; }

    /// <summary>The cited sources that moved after the concept was written.</summary>
    public IReadOnlyList<OkfDriftedSource> DriftedSources { get; }

    /// <summary>Whether the item carries a reason.</summary>
    /// <param name="reason">The reason to test for.</param>
    /// <returns><see langword="true" /> when the item is on the inbox for that reason.</returns>
    public bool Has(OkfInboxReason reason) => Reasons.Contains(reason);

    /// <inheritdoc />
    public override string ToString() => Concept.Path;
}

/// <summary>Everything the inbox scan needs beyond the bundles themselves.</summary>
public sealed class OkfInboxOptions
{
    /// <summary>
    /// The date staleness is judged against (§5.5). Injected rather than read from the
    /// clock, so a scan is deterministic in tests (PRD CORE-7).
    /// </summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>The outcome of an inbox scan.</summary>
public sealed class OkfInboxResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="items">The concepts needing attention, already ordered.</param>
    /// <param name="bundles">The bundles that were scanned.</param>
    /// <param name="conceptCount">How many concepts were read.</param>
    /// <param name="skippedCount">How many files were skipped because their frontmatter does not parse.</param>
    public OkfInboxResult(
        IReadOnlyList<OkfInboxItem> items,
        IReadOnlyList<OkfBundle> bundles,
        int conceptCount,
        int skippedCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(bundles);
        Items = items;
        Bundles = bundles;
        ConceptCount = conceptCount;
        SkippedCount = skippedCount;
    }

    /// <summary>The concepts needing attention, ordered by bundle then bundle-relative path.</summary>
    public IReadOnlyList<OkfInboxItem> Items { get; }

    /// <summary>The bundles that were scanned.</summary>
    public IReadOnlyList<OkfBundle> Bundles { get; }

    /// <summary>How many concepts were read.</summary>
    public int ConceptCount { get; }

    /// <summary>
    /// How many files were skipped because their frontmatter does not parse. They are an
    /// <c>okf lint</c> error (<c>OKF0001</c>), not an inbox item, but a silent skip would
    /// make a broken vault look like a clean one.
    /// </summary>
    public int SkippedCount { get; }

    /// <summary>Whether anything needs attention.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Counts the items carrying a reason.</summary>
    /// <param name="reason">The reason to count.</param>
    /// <returns>How many items carry it.</returns>
    public int Count(OkfInboxReason reason) => Items.Count(item => item.Has(reason));

    /// <summary>The items carrying a reason, in scan order.</summary>
    /// <param name="reason">The reason to select.</param>
    /// <returns>The matching items.</returns>
    public IEnumerable<OkfInboxItem> For(OkfInboxReason reason) => Items.Where(item => item.Has(reason));
}

/// <summary>
/// Derives acknowledgment, staleness, and source-drift state across a working set (PRD
/// CORE-15, CLI-12). It lives in <c>Okf.Core</c> so that <c>okf inbox</c>, a future MCP
/// tool, and any consumer embedding the library answer the question identically
/// (decisions.md §4).
/// </summary>
/// <remarks>
/// Nothing here is a diagnostic. <c>okf lint</c> already reports staleness (<c>OKF0202</c>)
/// and drift (<c>OKF0103</c>) as configurable severities against a bundle's conformance;
/// the inbox answers a different question — what is waiting for a person — and answers it
/// per concept rather than per finding, so one concept that is draft, expired, and citing
/// a moved source is one row with three reasons.
/// </remarks>
public static class OkfInboxScanner
{
    /// <summary>The <c>status</c> value that makes a concept unacknowledged by definition.</summary>
    public const string DraftStatus = "draft";

    /// <summary>Scans bundles for concepts needing human attention.</summary>
    /// <param name="bundles">The bundles to walk.</param>
    /// <param name="options">The date staleness is judged against.</param>
    /// <returns>The items, in deterministic order, plus the scan counts.</returns>
    /// <exception cref="IOException">A file in a bundle could not be read.</exception>
    public static OkfInboxResult Scan(IEnumerable<OkfBundle> bundles, OkfInboxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        options ??= new OkfInboxOptions();

        var list = bundles.ToList();
        var items = new List<OkfInboxItem>();
        var concepts = 0;
        var skipped = 0;

        foreach (var bundle in list)
        {
            // MarkdownFiles() is already ordinal by path and skips links out of the bundle,
            // so the bundle order the working set fixed is the whole ordering story.
            foreach (var file in bundle.MarkdownFiles())
            {
                if (OkfBundle.IsReservedFile(file))
                {
                    continue;
                }

                OkfDocument document;
                try
                {
                    document = OkfDocument.Parse(File.ReadAllText(file));
                }
                catch (OkfDocumentException)
                {
                    skipped++;
                    continue;
                }

                concepts++;
                if (Classify(new OkfConcept(bundle, file, document, options.Today), options.Today) is { } item)
                {
                    items.Add(item);
                }
            }
        }

        return new OkfInboxResult(items, list, concepts, skipped);
    }

    /// <summary>Classifies one concept.</summary>
    /// <param name="concept">The concept to judge.</param>
    /// <param name="today">The date staleness is judged against.</param>
    /// <returns>The inbox item, or <see langword="null" /> when the concept needs no attention.</returns>
    public static OkfInboxItem? Classify(OkfConcept concept, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(concept);

        var frontmatter = concept.Frontmatter;
        var generated = Nested(frontmatter, "generated");
        var generatedBy = generated is null ? null : FrontmatterValues.Scalar(generated, "by");
        var generatedAtText = generated is null ? null : FrontmatterValues.Scalar(generated, "at");
        var generatedAt = OkfLifecycleInstant.Parse(generatedAtText);

        var (verifiedBy, verifiedAtText, verifiedAt) = LatestVerification(frontmatter);
        var status = FrontmatterValues.Scalar(frontmatter, "status");
        var staleAfter = FrontmatterValues.Scalar(frontmatter, "stale_after");

        var reasons = new List<OkfInboxReason>();
        if (IsUnacknowledged(concept, generated, generatedBy, generatedAt, verifiedAt, status))
        {
            reasons.Add(OkfInboxReason.Unacknowledged);
        }

        if (concept.Stale)
        {
            reasons.Add(OkfInboxReason.Stale);
        }

        var drifted = DriftedSources(frontmatter, generatedAt);
        if (drifted.Count > 0)
        {
            reasons.Add(OkfInboxReason.SourceDrift);
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        return new OkfInboxItem(
            concept,
            reasons,
            generatedBy,
            generatedAtText,
            verifiedBy,
            verifiedAtText,
            staleAfter,
            status,
            generatedAt?.DaysUntil(today),
            concept.Stale ? OkfLifecycleInstant.Parse(staleAfter)?.DaysUntil(today) : null,
            drifted);
    }

    /// <summary>
    /// Whether a concept's current content is unacknowledged (PRD CORE-15). Three ways in,
    /// and the third is the one that makes an untouched agent-written vault honest:
    /// <list type="number">
    /// <item><c>status: draft</c> — the author said so.</item>
    /// <item>A verification exists but predates the content: <c>generated.at</c> is
    /// strictly newer than the latest <c>verified[].at</c>. Equal timestamps are
    /// acknowledged — a verification stamped in the same second as the write is the
    /// verification *of* that write.</item>
    /// <item>No verification at all behind a generation stamp whose actor is not a
    /// <c>human:</c> one. An unrecorded actor counts as non-human: what a concept does not
    /// record, it cannot claim.</item>
    /// </list>
    /// A concept with no <c>generated</c> block and no <c>verified</c> is not on the inbox:
    /// there is no stamp to be newer than a verification, and hand-written prose nobody
    /// dated is not something this command can say anything true about.
    /// </summary>
    private static bool IsUnacknowledged(
        OkfConcept concept,
        OkfMapping? generated,
        string? generatedBy,
        OkfLifecycleInstant? generatedAt,
        OkfLifecycleInstant? verifiedAt,
        string? status)
    {
        if (string.Equals(status, DraftStatus, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (verifiedAt is { } acknowledged)
        {
            return OkfLifecycleInstant.IsAfter(generatedAt, acknowledged);
        }

        // Verified, but by an event carrying no readable `at`. Someone stood behind this;
        // there is no timestamp to prove they stood behind *this* version, and inventing
        // one to report against would be worse than saying nothing.
        if (concept.TrustTier != OkfTrustTier.Unverified)
        {
            return false;
        }

        return generated is not null && !OkfActor.IsHuman(generatedBy);
    }

    /// <summary>
    /// The latest verification event by timestamp: its actor, its raw timestamp text, and
    /// the parsed value. Events with no readable <c>at</c> never win, so a malformed one
    /// cannot mask a good one.
    /// </summary>
    private static (string? By, string? At, OkfLifecycleInstant? Parsed) LatestVerification(OkfMapping frontmatter)
    {
        string? by = null;
        string? at = null;
        OkfLifecycleInstant? latest = null;

        foreach (var verification in OkfDocument.NormalizeVerified(frontmatter))
        {
            var text = FrontmatterValues.Scalar(verification, "at");
            if (OkfLifecycleInstant.Parse(text) is not { } parsed)
            {
                continue;
            }

            if (latest is null || OkfLifecycleInstant.IsAfter(parsed, latest.Value))
            {
                latest = parsed;
                at = text;
                by = FrontmatterValues.Scalar(verification, "by");
            }
        }

        return (by, at, latest);
    }

    /// <summary>
    /// The cited sources whose <c>last_modified</c> is later than <c>generated.at</c>
    /// (PRD CORE-8). Without a readable <c>generated.at</c> there is nothing to compare
    /// against, and the spec's own answer — produce no signal — is the one taken.
    /// </summary>
    /// <remarks>
    /// The comparison is deliberately the wider one: the inbox must never be quieter about
    /// drift than <c>OKF0103</c>, which compares the written dates alone, or a concept lint
    /// reports would be missing from the list of what needs a person.
    /// </remarks>
    private static List<OkfDriftedSource> DriftedSources(OkfMapping frontmatter, OkfLifecycleInstant? generatedAt)
    {
        if (generatedAt is not { } generated
            || !frontmatter.TryGetValue("sources", out var value)
            || value is not OkfSequence sources)
        {
            return [];
        }

        var drifted = new List<OkfDriftedSource>();
        foreach (var source in sources.OfType<OkfMapping>())
        {
            var lastModified = FrontmatterValues.Scalar(source, "last_modified");
            if (OkfLifecycleInstant.Parse(lastModified) is { } modified
                && OkfLifecycleInstant.IsAfterAtEitherPrecision(modified, generated))
            {
                drifted.Add(new OkfDriftedSource(FrontmatterValues.Scalar(source, "id"), FrontmatterValues.Scalar(source, "resource"), lastModified!));
            }
        }

        return drifted;
    }

    private static OkfMapping? Nested(OkfMapping mapping, string key) =>
        mapping.TryGetValue(key, out var value) ? value as OkfMapping : null;
}
