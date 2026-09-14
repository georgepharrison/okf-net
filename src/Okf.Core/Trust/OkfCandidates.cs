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
/// <see cref="FrontmatterUnreadable" /> is a file whose frontmatter could not be read at all —
/// and a caller triaging one wants an editor, while a caller triaging the other wants a linter.
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
    /// The file's frontmatter could not be read at all — no opening fence, a fence that never
    /// closed, or a delimited block that is not a YAML mapping — so nothing about the concept,
    /// including its verification history, could be established (#76).
    /// </summary>
    /// <remarks>
    /// A fenceless file is in this arm deliberately, and that supersedes the note #74 left about
    /// it reading as provably-absent history: the parser still returns an empty mapping for it,
    /// because <c>okf lint</c> and <c>okf search</c> need that leniency, but the scanner no
    /// longer treats "no frontmatter was found" as "no <c>verified</c> key". Deleting three
    /// dashes must not move a file between candidate and quarantined.
    /// </remarks>
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
/// <remarks>
/// The two kinds of quarantine are carried the same way on purpose. A file whose frontmatter
/// could not be read has no <see cref="OkfConcept" /> to carry — there was no mapping to read
/// <c>type</c>, <c>title</c> or a trust tier out of — and forcing one into existence would mean
/// inventing frontmatter to describe a file that has none. So a quarantine names a file by
/// paths and carries its tier only when the file earned one.
/// </remarks>
/// <param name="bundle">The bundle the file sits in.</param>
/// <param name="path">The bundle-relative path, with <c>/</c> separators.</param>
/// <param name="absolutePath">The file's absolute path — the one a caller opens.</param>
/// <param name="reason">Why its history could not be established.</param>
/// <param name="detail">
/// What was found, as written, for the human who has to fix it. The reason's wire spelling is
/// the machine-readable half; this is the half a person reads.
/// </param>
/// <param name="concept">
/// The concept behind the file, when there was one: a file that read as a concept but whose
/// <c>verified</c> block could not be read. A file whose frontmatter could not be read at all
/// has no concept, and is named by its paths instead.
/// </param>
public sealed class OkfQuarantinedConcept(
    OkfBundle bundle,
    string path,
    string absolutePath,
    OkfQuarantineReason reason,
    string detail,
    OkfConcept? concept = null)
{
    /// <summary>The concept whose history could not be established, when it had one.</summary>
    public OkfConcept? Concept { get; } = concept;

    /// <summary>The bundle the file sits in.</summary>
    public OkfBundle Bundle { get; } = bundle ?? throw new ArgumentNullException(nameof(bundle));

    /// <summary>The bundle-relative path, with <c>/</c> separators.</summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The file's absolute path.</summary>
    public string AbsolutePath { get; } = absolutePath ?? throw new ArgumentNullException(nameof(absolutePath));

    /// <summary>The concept id: the bundle-relative path minus <c>.md</c> (spec §2).</summary>
    public string Id => Path.EndsWith(".md", StringComparison.Ordinal) ? Path[..^3] : Path;

    /// <summary>Why it could not be established.</summary>
    public OkfQuarantineReason Reason { get; } = reason;

    /// <summary>What was found, as written.</summary>
    public string Detail { get; } = detail ?? throw new ArgumentNullException(nameof(detail));

    /// <summary>
    /// The trust tier the file appears to earn, or <see langword="null" /> when the file never
    /// got far enough to earn one. Reported where it exists because the disagreement between a
    /// tier and a verdict is itself the finding (#84).
    /// </summary>
    public OkfTrustTier? TrustTier => Concept?.TrustTier;

    /// <inheritdoc />
    public override string ToString() => Path;
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

    /// <summary>
    /// The <c>stale_after</c> value as written (§5.5), or <see langword="null" /> when the concept
    /// names no date. Read the way the inbox reads it, so the two surfaces cannot print different
    /// text for one file. <see cref="OkfConcept.Stale" /> is the derived half; this is the half a
    /// consumer ranks review priority with.
    /// </summary>
    public string? StaleAfter => FrontmatterValues.Scalar(Concept.Frontmatter, "stale_after");

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
/// The outcome of a candidate scan: the candidates, the quarantined files, and the counts
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
    /// <param name="quarantined">The files whose history could not be established, in the same order.</param>
    /// <param name="bundles">The bundles that were scanned.</param>
    /// <param name="conceptCount">
    /// How many markdown files the walk reached and placed, unreadable ones included. See
    /// <see cref="ConceptCount" />.
    /// </param>
    public OkfCandidateResult(
        IReadOnlyList<OkfCandidate> candidates,
        IReadOnlyList<OkfQuarantinedConcept> quarantined,
        IReadOnlyList<OkfBundle> bundles,
        int conceptCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(quarantined);
        ArgumentNullException.ThrowIfNull(bundles);

        Candidates = candidates;
        Quarantined = quarantined;
        Bundles = bundles;
        ConceptCount = conceptCount;
    }

    /// <summary>The candidates, ordered by bundle, then by bundle-relative path.</summary>
    public IReadOnlyList<OkfCandidate> Candidates { get; }

    /// <summary>The files whose history could not be established, in the same order as the candidates.</summary>
    public IReadOnlyList<OkfQuarantinedConcept> Quarantined { get; }

    /// <summary>The bundles that were scanned, in the order they were named.</summary>
    public IReadOnlyList<OkfBundle> Bundles { get; }

    /// <summary>
    /// How many markdown files the scan reached and placed, so a caller can state the size of
    /// what it looked at. Both kinds of quarantine are counted, including a file whose
    /// frontmatter never read — it is a file the scan reached and set aside, not one it did not
    /// see, and leaving it out would make <c>Scanned N concepts</c> understate a broken bundle.
    /// The name is kept for the concept-shaped majority of that set; the spec's reserved files are
    /// the only markdown excluded, and they are not concepts by §3.1.
    /// </summary>
    public int ConceptCount { get; }

    /// <summary>Whether any file was quarantined, which is what makes the inventory incomplete.</summary>
    public bool IsComplete => Quarantined.Count == 0;

    /// <summary>Whether nothing was found: no candidates and nothing quarantined.</summary>
    public bool IsEmpty => Candidates.Count == 0 && Quarantined.Count == 0;

    /// <summary>Counts the quarantined files carrying a reason.</summary>
    /// <param name="reason">The reason to count.</param>
    /// <returns>How many carry it.</returns>
    public int Count(OkfQuarantineReason reason) => Quarantined.Count(item => item.Reason == reason);

    /// <summary>The quarantined files carrying a reason, in scan order.</summary>
    /// <param name="reason">The reason to select.</param>
    /// <returns>The matching concepts.</returns>
    public IEnumerable<OkfQuarantinedConcept> For(OkfQuarantineReason reason) =>
        Quarantined.Where(item => item.Reason == reason);
}

/// <summary>
/// Enumerates the concepts in a working set whose verification history is provably absent,
/// and separately names every file whose history could not be established (work items #74 and
/// #76). It is an inventory, not a gate on volume: forty candidates is a success, and finding
/// them is the library's whole job — deciding what a reviewer should do about one is not.
/// </summary>
/// <remarks>
/// WHAT IS NOT A FILTER
/// --------------------
/// Draft concepts are included, stale concepts are included, and <c>about.md</c> is included:
/// all three are concepts, and a concept nobody has verified is a candidate whatever else is
/// true of it. The two exclusions are the two that follow from the verdict: history present
/// (already human-verified or machine-confirmed) and history that could not be established
/// (quarantined). The spec §3.1 reserved files never appear because <see cref="OkfConceptWalk" />
/// does not call them concepts, which is that primitive's answer rather than this scanner's.
///
/// TWO REASONS, ONE LIST, AND WHY A FENCELESS FILE IS QUARANTINED
/// --------------------------------------------------------------
/// A file is quarantined for one of two reasons, and they are different kinds of problem:
/// <see cref="OkfQuarantineReason.VerificationStructure" /> is a concept whose bytes read fine
/// and whose <c>verified</c> block cannot be read as events, while
/// <see cref="OkfQuarantineReason.FrontmatterUnreadable" /> is a file whose frontmatter was
/// never readable, so the question was never answerable. A fenceless <c>README.md</c> is the
/// second, not a candidate: <see cref="OkfDocument.Parse" /> hands back an empty mapping for it,
/// and reading that as "provably no <c>verified</c> key" made an author's three dashes an
/// eligibility switch (#76). The parser does not change — its leniency is <c>okf lint</c>'s and
/// <c>okf search</c>'s requirement — the scanner simply stops treating an absent block as a
/// proven absence.
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
        // path. Nothing is RE-sorted here, because a second ordering is a second opinion about
        // the corpus, and the walk exists so there is only one (AD-6). What the scanner does has
        // to be a MERGE and not an append: a `verified`-structure quarantine is a concept, so it
        // sorts among the unreadable files by path, and appending one kind after the other would
        // print `zulu.md` before `alpha.md`.
        //
        // The comparison is deliberately bundle-relative path ONLY, and that is exactly
        // sufficient. `MarkdownFiles()` orders by (bundle, relative path) — the separator-safe
        // sort work item #36 pinned — so a file is never reached before an earlier-named bundle's
        // file whatever its own path, and comparing paths alone reproduces the walk's order
        // across bundles as well as within one. Comparing bundle identity would be a second,
        // different answer: the walk's bundle arm is the CALLER's order, which no sort over
        // `Bundle.Name` or `Bundle.Root` can reproduce (hand it [bb, b] and any such sort
        // disagrees with the walk it claims to reproduce).
        int conceptIndex;
        int unreadableIndex;
        for (conceptIndex = 0, unreadableIndex = 0;
            conceptIndex < concepts.Count || unreadableIndex < unreadable.Count;)
        {
            // Equal paths cannot happen across the two lists of one bundle — they are disjoint by
            // construction — so the tie goes to the concept only to keep the comparison total,
            // never to prefer a kind. That tie is why Stryker's `<= 0` to `< 0` mutant survives: it
            // differs from the real comparison only when the paths are equal, and no bundle can
            // produce that input. It is an equivalent mutant, not an untested branch.
            bool takeConcept = conceptIndex < concepts.Count
                && (unreadableIndex >= unreadable.Count
                    || string.CompareOrdinal(
                        concepts[conceptIndex].Path, unreadable[unreadableIndex].Path) <= 0);

            if (takeConcept)
            {
                OkfConcept concept = concepts[conceptIndex++];
                OkfVerificationVerdict verdict = OkfVerificationHistory.Verdict(concept);
                if (verdict.IsCandidate())
                {
                    candidates.Add(new OkfCandidate(concept));
                }
                else if (verdict == OkfVerificationVerdict.Unreadable)
                {
                    // Named with the paths the concept carries, so both quarantine kinds print
                    // the same way and a consumer never branches on the reason to find a path.
                    quarantined.Add(new OkfQuarantinedConcept(
                        concept.Bundle,
                        concept.Path,
                        concept.AbsolutePath,
                        OkfQuarantineReason.VerificationStructure,
                        Describe(concept),
                        concept));
                }
            }
            else
            {
                OkfUnreadableConcept file = unreadable[unreadableIndex++];
                quarantined.Add(new OkfQuarantinedConcept(
                    file.Bundle,
                    file.Path,
                    file.AbsolutePath,
                    OkfQuarantineReason.FrontmatterUnreadable,
                    file.Reason));
            }
        }

        // An unreadable file is a file the scan reached and set aside, so counting it is what keeps
        // `Scanned N concepts` from understating a broken bundle.
        return new OkfCandidateResult(candidates, quarantined, list, concepts.Count + unreadable.Count);
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
