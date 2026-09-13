namespace Okf.Core.Trust;

/// <summary>
/// Why a concept could not be classified as having, or provably lacking, verification history
/// (work item #74). A quarantine is not a verdict about the concept's history; it is the
/// scanner admitting it could not establish one.
/// </summary>
/// <remarks>
/// The enumeration exists so the reason has a stable wire spelling that #75's renderer and
/// #77's JSON contract both print, rather than a string each surface invents. The two reasons
/// are deliberately distinct in kind: <see cref="VerificationStructure" /> is a concept whose
/// bytes were read and whose <c>verified</c> block cannot be read as events, while
/// <see cref="FrontmatterUnreadable" /> is a concept whose bytes could not be read at all —
/// and a caller triaging one wants a linter, while a caller triaging the other wants an editor.
/// </remarks>
public enum OkfQuarantineReason
{
    /// <summary>
    /// The <c>verified</c> block claims verification but cannot be safely read as events: a
    /// scalar, an empty mapping, a mapping naming no author, or a sequence with non-mapping
    /// items in it.
    /// </summary>
    VerificationStructure,

    /// <summary>
    /// The file's frontmatter does not parse, so nothing about the concept — including its
    /// verification history — could be established. Reserved here so #76 can add it without
    /// renaming the reason #74 ships; the scanner does not populate it yet.
    /// </summary>
    FrontmatterUnreadable,
}

/// <summary>Conversions for <see cref="OkfQuarantineReason" />.</summary>
public static class OkfQuarantineReasonExtensions
{
    /// <summary>Every reason, in report order.</summary>
    public static IReadOnlyList<OkfQuarantineReason> All { get; } =
    [
        OkfQuarantineReason.VerificationStructure,
        OkfQuarantineReason.FrontmatterUnreadable,
    ];

    /// <summary>The wire spelling of a reason, which is the JSON contract's value (#77).</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Its wire spelling.</returns>
    public static string ToWireString(this OkfQuarantineReason reason) => reason switch
    {
        OkfQuarantineReason.VerificationStructure => "verification-structure",
        OkfQuarantineReason.FrontmatterUnreadable => "frontmatter-unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}

/// <summary>
/// One concept the scanner could not classify, named with what it found. Naming is the point:
/// an enumeration that quietly omits concepts is worse than one that admits a gap (#71), so
/// every quarantined concept is here with its path, its id, and the reason.
/// </summary>
/// <param name="concept">The concept whose history could not be established.</param>
/// <param name="reason">Why it could not be established.</param>
/// <param name="detail">
/// What the block actually looked like, as written, for the human who has to fix it. The
/// reason's wire spelling is the machine-readable half; this is the half a person reads.
/// </param>
public sealed class OkfQuarantinedConcept(OkfConcept concept, OkfQuarantineReason reason, string detail)
{
    /// <summary>The concept whose history could not be established.</summary>
    public OkfConcept Concept { get; } = concept ?? throw new ArgumentNullException(nameof(concept));

    /// <summary>Why it could not be established.</summary>
    public OkfQuarantineReason Reason { get; } = reason;

    /// <summary>What the block looked like as written.</summary>
    public string Detail { get; } = detail ?? throw new ArgumentNullException(nameof(detail));

    /// <inheritdoc />
    public override string ToString() => Concept.Path;
}

/// <summary>
/// One concept whose verification history is provably absent — a review candidate. It carries
/// everything a caller needs to open the exact file without guessing how a path was formed
/// (#71 story 5), and nothing about what the reviewer should conclude.
/// </summary>
/// <param name="concept">The candidate concept.</param>
public sealed class OkfCandidate(OkfConcept concept)
{
    /// <summary>The candidate concept, carrying its id, paths, title, type and derived tier.</summary>
    public OkfConcept Concept { get; } = concept ?? throw new ArgumentNullException(nameof(concept));

    /// <summary>The bundle the concept sits in.</summary>
    public OkfBundle Bundle => Concept.Bundle;

    /// <summary>The bundle-relative path, with <c>/</c> separators.</summary>
    public string Path => Concept.Path;

    /// <summary>The concept id: the bundle-relative path minus <c>.md</c> (spec §2).</summary>
    public string Id => Concept.Id;

    /// <summary>The concept's absolute path.</summary>
    public string AbsolutePath => Concept.AbsolutePath;

    /// <summary>The derived trust tier (§5.3), reported as-is even where it disagrees with the verdict.</summary>
    public OkfTrustTier TrustTier => Concept.TrustTier;

    /// <inheritdoc />
    public override string ToString() => Concept.Path;
}

/// <summary>Everything a candidate scan needs beyond the bundles themselves.</summary>
public sealed class OkfCandidateOptions
{
    /// <summary>
    /// The date staleness is judged against. Injected rather than read from the clock, so a
    /// scan is deterministic in tests (PRD CORE-7), mirroring <see cref="OkfInboxOptions" />.
    /// </summary>
    /// <remarks>
    /// Staleness is not an eligibility test — a stale concept with no history is still a
    /// candidate — but the date reaches <see cref="OkfConcept" />'s constructor, so a scan
    /// without one would silently judge staleness against whenever it happened to run.
    /// </remarks>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>
/// The outcome of a candidate scan: the candidates, the quarantined concepts, and the counts
/// that let a caller say the inventory is complete.
/// </summary>
/// <remarks>
/// The two lists are disjoint by construction and neither is a subset of the other's absence:
/// <see cref="Candidates" /> is exhaustive over what could be classified, and
/// <see cref="IsComplete" /> says whether that "could be classified" left anything out. A
/// caller that ignores the second and prints the first is exactly the silent omission this
/// result shape exists to make impossible.
/// </remarks>
public sealed class OkfCandidateResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="candidates">The concepts with provably absent history, already ordered.</param>
    /// <param name="quarantined">The concepts whose history could not be established, in the same order.</param>
    /// <param name="bundles">The bundles that were scanned.</param>
    /// <param name="conceptCount">How many concepts were read and classified.</param>
    /// <param name="unreadableFileCount">How many files' frontmatter did not parse. Reported, not yet quarantined (#76).</param>
    public OkfCandidateResult(
        IReadOnlyList<OkfCandidate> candidates,
        IReadOnlyList<OkfQuarantinedConcept> quarantined,
        IReadOnlyList<OkfBundle> bundles,
        int conceptCount,
        int unreadableFileCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(quarantined);
        ArgumentNullException.ThrowIfNull(bundles);

        Candidates = candidates;
        Quarantined = quarantined;
        Bundles = bundles;
        ConceptCount = conceptCount;
        UnreadableFileCount = unreadableFileCount;
    }

    /// <summary>The candidates, ordered by bundle, then by bundle-relative path.</summary>
    public IReadOnlyList<OkfCandidate> Candidates { get; }

    /// <summary>The concepts whose history could not be established, in the same order as the candidates.</summary>
    public IReadOnlyList<OkfQuarantinedConcept> Quarantined { get; }

    /// <summary>The bundles that were scanned, in the order they were named.</summary>
    public IReadOnlyList<OkfBundle> Bundles { get; }

    /// <summary>How many concepts were read and classified. Quarantined concepts are counted here.</summary>
    public int ConceptCount { get; }

    /// <summary>
    /// How many files' frontmatter did not parse. #74 does not quarantine them — that is #76 —
    /// but it must not report them as candidates either, so the count is carried here for the
    /// command to surface and for #76 to convert into quarantines.
    /// </summary>
    public int UnreadableFileCount { get; }

    /// <summary>Whether any concept was quarantined, which is what makes the inventory incomplete.</summary>
    public bool IsComplete => Quarantined.Count == 0;

    /// <summary>Whether nothing was found: no candidates and nothing quarantined.</summary>
    public bool IsEmpty => Candidates.Count == 0 && Quarantined.Count == 0;

    /// <summary>Counts the quarantined concepts carrying a reason.</summary>
    /// <param name="reason">The reason to count.</param>
    /// <returns>How many carry it.</returns>
    public int Count(OkfQuarantineReason reason) => Quarantined.Count(item => item.Reason == reason);

    /// <summary>The quarantined concepts carrying a reason, in scan order.</summary>
    /// <param name="reason">The reason to select.</param>
    /// <returns>The matching concepts.</returns>
    public IEnumerable<OkfQuarantinedConcept> For(OkfQuarantineReason reason) =>
        Quarantined.Where(item => item.Reason == reason);
}

/// <summary>
/// Enumerates the concepts in a working set whose verification history is provably absent,
/// and separately names the concepts whose history could not be established (work item #74).
/// It is an inventory, not a gate on volume: forty candidates is a success, and finding them
/// is the library's whole job — deciding what a reviewer should do about one is not.
/// </summary>
/// <remarks>
/// WHAT IS NOT A FILTER
/// --------------------
/// Draft concepts are included, stale concepts are included, and <c>about.md</c> is included:
/// all three are concepts, and a concept nobody has verified is a candidate whatever else is
/// true of it. The two exclusions are the two that follow from the verdict: history present
/// (already human-verified or machine-confirmed) and history unreadable (quarantined). The
/// spec §3.1 reserved files never appear because <see cref="OkfConceptWalk" /> does not call
/// them concepts, which is that primitive's answer rather than this scanner's.
///
/// WHY THE VERDICT AND THE TIER ARE NOT THE SAME QUESTION
/// ------------------------------------------------------
/// The tier is reported on every candidate and is never consulted for eligibility. A concept
/// whose <c>verified</c> block cannot be read may therefore print a tier that disagrees with
/// its own verdict — <c>verified: {}</c> reads <c>machine-confirmed</c> to §5.3 while this
/// scanner quarantines it. That is intended, and #84 tracks the reconciliation; the reason it
/// is safe is that a quarantined concept is named on the incomplete list either way, so a
/// disagreeing tier can never hide a concept from review.
///
/// READ-ONLY
/// ---------
/// Nothing here writes, launches a process, or touches a network. A file that cannot be read
/// off the disk at all propagates, exactly as it does through the walk: that is an environment
/// failure the command reports as one, not a fact about a concept.
/// </remarks>
public static class OkfCandidateScanner
{
    /// <summary>Scans bundles for concepts with provably absent verification history.</summary>
    /// <param name="bundles">The bundles to walk, in the order their candidates are reported.</param>
    /// <param name="options">The date staleness is judged against.</param>
    /// <returns>The candidates and quarantined concepts, in deterministic order, plus the scan counts.</returns>
    /// <exception cref="IOException">A file in a bundle could not be read.</exception>
    public static OkfCandidateResult Scan(IEnumerable<OkfBundle> bundles, OkfCandidateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        options ??= new OkfCandidateOptions();

        List<OkfBundle> list = bundles.ToList();
        (List<OkfConcept> concepts, List<OkfUnreadableConcept> unreadable) = OkfConceptWalk.Read(list, options.Today);

        List<OkfCandidate> candidates = [];
        List<OkfQuarantinedConcept> quarantined = [];

        // The walk's order is the report's order: bundle order, then ordinal bundle-relative
        // path. Nothing is re-sorted here, because a second ordering is a second opinion about
        // the corpus, and the walk exists so there is only one (AD-6).
        foreach (OkfConcept concept in concepts)
        {
            OkfVerificationVerdict verdict = OkfVerificationHistory.Verdict(concept);
            if (verdict.IsCandidate())
            {
                candidates.Add(new OkfCandidate(concept));
            }
            else if (verdict == OkfVerificationVerdict.Unreadable)
            {
                quarantined.Add(new OkfQuarantinedConcept(
                    concept,
                    OkfQuarantineReason.VerificationStructure,
                    Describe(concept)));
            }
        }

        return new OkfCandidateResult(candidates, quarantined, list, concepts.Count, unreadable.Count);
    }

    /// <summary>
    /// What the block looked like as written, for the line a human reads. The frontmatter text
    /// is re-emitted and grepped rather than carried from the parse, because the parse
    /// deliberately keeps no source offsets; the value's shape is named alongside it so a
    /// reader who has to fix the file knows which line to look at.
    /// </summary>
    /// <remarks>
    /// A YAML re-emit can fail on a value model the parser produced — an unresolvable merge key
    /// is the known case — and the reason line must never be the thing that stops a scan, so a
    /// failure degrades to naming the shape alone.
    /// </remarks>
    private static string Describe(OkfConcept concept)
    {
        // A quarantine always carries the key — the verdict only refuses to classify a concept
        // whose `verified` is a structure that exists and cannot be read, and an absent or null
        // key is Absent rather than Unreadable — so this branch is defensive against
        // TryGetValue's nullable out-parameter, not a case a scan reaches. It is named rather
        // than assumed away: a caller reading the detail should never see an empty reason.
        if (!concept.Frontmatter.TryGetValue(OkfVerificationHistory.Key, out OkfValue? value) || value is null)
        {
            return "no 'verified' key";
        }

        string shape = Shape(value);
        return TryRender(value, out string? text)
            ? $"'verified: {text}' is {shape}, which cannot be read as verification events"
            : $"the 'verified' key is {shape}, which cannot be read as verification events";
    }

    private static bool TryRender(OkfValue value, out string? text)
    {
        try
        {
            // Emit appends a newline and quotes what needs quoting; `by: ` renders as the
            // shape it is, which is the point.
            text = YamlBridge.Emit(value).Trim();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
        {
            text = null;
            return false;
        }
    }

    private static string Shape(OkfValue? value) => value switch
    {
        null => "nothing",
        OkfScalar scalar when scalar.IsNull => "null",
        OkfScalar scalar when scalar.IsQuoted => $"the quoted string '{scalar.Value}'",
        OkfScalar scalar => $"the scalar '{scalar.Value}'",
        OkfMapping mapping when mapping.Count == 0 => "an empty mapping",
        OkfMapping => "a mapping",
        OkfSequence sequence when sequence.Count == 0 => "an empty sequence",
        OkfSequence sequence => $"a sequence of {sequence.Count} item(s)",
        _ => "an unreadable value",
    };
}
