using System.Globalization;

namespace Okf.Core.Trust;

/// <summary>
/// One verification event the reader could recognise: an author, and the instant it recorded if
/// it recorded one (work item #75).
/// </summary>
/// <param name="By">The <c>by</c> value, as written.</param>
/// <param name="At">
/// The <c>at</c> value. The canonical spelling when the file carried a parseable instant, and
/// the file's own text when it did not — a report prints what the concept says, and an
/// unparseable stamp is still the stamp somebody wrote.
/// </param>
/// <param name="Instant">
/// The parsed instant, or <see langword="null" /> when the value is not one. Ordering consults
/// this; printing never does.
/// </param>
public readonly record struct OkfVerificationEvent(string By, string? At, OkfLifecycleInstant? Instant)
{
    /// <summary>Whether the author names a person (§7).</summary>
    public bool IsHuman => OkfActor.IsHuman(By);

    /// <inheritdoc />
    public override string ToString() => At is { Length: > 0 } at ? $"{By} {at}" : By;
}

/// <summary>
/// The <c>verified</c> block read as the history it records: every event, the first, the latest,
/// and how many items could not be read (work item #75).
/// </summary>
/// <remarks>
/// WHY THIS SITS BESIDE THE VERDICT RATHER THAN UNDER IT
/// -----------------------------------------------------
/// <see cref="OkfVerificationHistory" /> answers "is there any readable history?" and answers it
/// three-valued, because a candidate enumeration has to distinguish provable absence from "could
/// not tell". A report row needs the other half of the same block — <em>who</em> stood behind a
/// concept and <em>when</em> — which is the detail that explains a concept missing from the
/// list. Both readings consult <see cref="OkfVerificationHistory" /> for whether a block is
/// readable, so they cannot disagree about whether an event counts, which is the property the
/// renderer depends on: every concept the scanner calls a candidate prints "never verified", and
/// every concept it does not prints a name.
///
/// WHICH EVENT IS "THE LATEST"
/// ---------------------------
/// Not the last line of the block. The order events were appended is nobody's claim about which
/// is newest, and a reviewer briefed with a stale name is being briefed badly. The ranking is
/// §5.3's, reused rather than re-derived: a person outranks a process, a process outranks a
/// producer, and a producer outranks an author written in none of §7's forms — because the tier
/// printed beside the row already ranks that way, and a row whose "latest verification"
/// contradicted its own tier would read as a bug. Within one rank, the newest parseable instant
/// wins; an unparseable one is treated as the oldest thing in the block rather than dropped, so
/// a signed event is never demoted out of the row because somebody wrote <c>at: last tuesday</c>;
/// and when nothing in the rank parses, the most recently written event wins, which is the only
/// ordering left.
///
/// A MALFORMED ITEM IS COUNTED, NOT HUSHED
/// ---------------------------------------
/// <see cref="MalformedEventCount" /> is why a row can say "1 event recorded, 1 could not be
/// read" for <c>[{ by: human:ringo }, {}]</c>. Dropping the unreadable item silently would print
/// a shorter history than the file holds — the same quiet omission #74 quarantines against, in a
/// place where nobody is looking at a quarantine list to notice.
/// </remarks>
public sealed class OkfVerificationStamp
{
    private const int RankHuman = 0;
    private const int RankProcess = 1;
    private const int RankProducer = 2;
    private const int RankOther = 3;

    private OkfVerificationEvent? LatestEvent { get; }

    private OkfVerificationEvent? FirstEvent { get; }

    /// <summary>
    /// The one sentence that says why a concept with this stamp is a candidate, naming the shape
    /// the file actually holds. It lives here rather than in the renderer because it is a reading
    /// of the block, and AD-6 puts readings in the library: <c>okf candidates</c> prints this and
    /// any later surface can say the same words about the same bytes.
    /// </summary>
    /// <remarks>
    /// "Unverified" alone is not an answer a person can act on — the fix for a missing
    /// <c>verified</c> key and the fix for a stray <c>[]</c> are different edits, and the whole
    /// point of the row is to send someone to the right line.
    /// </remarks>
    public string WhyCandidate =>
        MalformedEventCount > 0
            ? $"{Plural(Events.Count, "verification event")} readable; " +
                $"{MalformedEventCount.ToString(CultureInfo.InvariantCulture)} could not be read"
            : AbsentShape;

    private string AbsentShape => VerifiedValue switch
    {
        null => "no `verified` key — nobody has ever stood behind this content",
        OkfScalar scalar when scalar.IsNull => "`verified` is null — nobody has ever stood behind this content",
        OkfSequence { Count: 0 } => "`verified: []` — an explicit empty list, which says nobody has yet",
        _ => "`verified` records nothing",
    };

    // Read straight off the mapping rather than through FrontmatterValues.Scalar: that helper
    // applies §11 truthiness, which reads `verified:` and `verified: []` as "no value" and would
    // collapse three distinct shapes into the same sentence. The distinction is the sentence's
    // whole purpose — the fix for a missing key and the fix for a stray `[]` are different edits.
    private OkfValue? VerifiedValue =>
        _frontmatter is { } frontmatter
        && frontmatter.TryGetValue(OkfVerificationHistory.Key, out OkfValue? value)
            ? value
            : null;

    private readonly OkfMapping? _frontmatter;

    /// <summary>Initializes a stamp from the events it read and the items it could not.</summary>
    /// <param name="events">The recognizable events, in the order they were written.</param>
    /// <param name="malformedEventCount">How many items in the block were not recognizable.</param>
    /// <param name="frontmatter">
    /// The block the events came from, kept so <see cref="WhyCandidate" /> can describe the shape
    /// rather than guess at it. Null only for <see cref="None" />, which describes an absent key.
    /// </param>
    internal OkfVerificationStamp(
        IReadOnlyList<OkfVerificationEvent> events,
        int malformedEventCount,
        OkfMapping? frontmatter = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentOutOfRangeException.ThrowIfNegative(malformedEventCount);

        Events = events;
        MalformedEventCount = malformedEventCount;
        _frontmatter = frontmatter;
        FirstEvent = events.Count > 0 ? events[0] : null;
        LatestEvent = events.Count > 0 ? Newest(events) : null;
    }

    /// <summary>The empty stamp: a concept with no <c>verified</c> key, or one that claims nothing.</summary>
    public static OkfVerificationStamp None { get; } = new([], 0);

    /// <summary>The recognizable events, in the order they were written.</summary>
    public IReadOnlyList<OkfVerificationEvent> Events { get; }

    /// <summary>
    /// How many items in the block could not be read as events. Non-zero exactly when
    /// <see cref="OkfVerificationHistory" /> quarantines the concept, which is what lets a
    /// renderer explain a quarantined row instead of printing a truncated history beside it.
    /// </summary>
    public int MalformedEventCount { get; }

    /// <summary>Whether the block records any history at all — the verdict's answer, reused.</summary>
    public bool HasHistory => Events.Count > 0 || MalformedEventCount > 0;

    /// <summary>The newest recognizable event, or <see langword="null" /> when none is readable.</summary>
    public OkfVerificationEvent? Latest => LatestEvent;

    /// <summary>The earliest recognizable event, or <see langword="null" /> when none is readable.</summary>
    public OkfVerificationEvent? First => FirstEvent;

    /// <summary>Reads a concept's verification history.</summary>
    /// <param name="concept">The concept to read.</param>
    /// <returns>The stamp.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="concept" /> is null.</exception>
    public static OkfVerificationStamp Read(OkfConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return Read(concept.Frontmatter);
    }

    /// <summary>Reads the verification history a frontmatter block records.</summary>
    /// <param name="frontmatter">The frontmatter to read.</param>
    /// <returns>The stamp.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frontmatter" /> is null.</exception>
    public static OkfVerificationStamp Read(OkfMapping frontmatter)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);

        // One reading of readability, from one place: the verdict decides whether this block is
        // readable history, and this type only asks what it says once it has been allowed to say
        // anything. Absent still carries the frontmatter, because "no key", `verified: null` and
        // `verified: []` are three different answers to give back, and a shared empty singleton
        // could only ever print one of them.
        if (OkfVerificationHistory.Verdict(frontmatter) == OkfVerificationVerdict.Absent)
        {
            return new OkfVerificationStamp([], 0, frontmatter);
        }

        if (!frontmatter.TryGetValue(OkfVerificationHistory.Key, out OkfValue? value) || value is null)
        {
            return new OkfVerificationStamp([], 0, frontmatter);
        }

        IReadOnlyList<OkfMapping> entries = OkfDocument.NormalizeVerified(frontmatter);
        List<OkfVerificationEvent> events = [];
        foreach (OkfMapping entry in entries)
        {
            if (Event(entry) is { } verification)
            {
                events.Add(verification);
            }
        }

        // `NormalizeVerified` drops a scalar block and any non-mapping item in a sequence, so
        // whatever the block holds beyond the events it returned is unreadable by definition.
        // Counting it as `items - events` is the honest arithmetic and needs no second shape test.
        int items = value switch
        {
            OkfMapping => 1,
            OkfSequence sequence => sequence.Count,
            _ => 1,
        };

        return new OkfVerificationStamp(events, items - events.Count, frontmatter);
    }

    /// <summary>
    /// One event as the block wrote it, or <see langword="null" /> when the mapping is not one.
    /// The author test is #74's — a non-empty scalar author, structural rather than grammatical —
    /// reached through the one place that applies it, so a shape the verdict blesses is a shape
    /// this reader prints and never the reverse.
    /// </summary>
    /// <summary>
    /// The <c>status</c> a concept carries, as written — the value that makes it a draft (§5.2).
    /// Exposed because the candidate report prints it and no CLI reader should re-derive §11
    /// truthiness for one key.
    /// </summary>
    /// <param name="concept">The concept to read.</param>
    /// <returns>The <c>status</c> value, or <see langword="null" /> when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="concept" /> is null.</exception>
    public static string? Status(OkfConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return FrontmatterValues.Scalar(concept.Frontmatter, "status");
    }

    /// <summary>
    /// The <c>generated</c> stamp's actor, as written (§5.2), or <see langword="null" /> when the
    /// concept carries no readable stamp.
    /// </summary>
    /// <param name="concept">The concept to read.</param>
    /// <returns>The <c>generated.by</c> value, or <see langword="null" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="concept" /> is null.</exception>
    public static string? GeneratedBy(OkfConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return Generated(concept) is { } generated ? FrontmatterValues.Scalar(generated, "by") : null;
    }

    /// <summary>
    /// The <c>generated</c> stamp's instant, as written (§5.2), or <see langword="null" /> when the
    /// concept carries no readable stamp.
    /// </summary>
    /// <param name="concept">The concept to read.</param>
    /// <returns>The <c>generated.at</c> value, or <see langword="null" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="concept" /> is null.</exception>
    public static string? GeneratedAt(OkfConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return Generated(concept) is { } generated
            ? WrittenAt(generated, OkfLifecycleInstant.Parse(FrontmatterValues.Scalar(generated, "at")))
            : null;
    }

    private static OkfMapping? Generated(OkfConcept concept) =>
        concept.Frontmatter.TryGetValue("generated", out OkfValue? value) ? value as OkfMapping : null;

    private static OkfVerificationEvent? Event(OkfMapping mapping)
    {
        if (!OkfVerificationHistory.Recognizes(mapping))
        {
            return null;
        }

        string by = FrontmatterValues.Scalar(mapping, OkfVerificationHistory.AuthorKey) ?? string.Empty;
        OkfLifecycleInstant? instant = OkfLifecycleInstant.Parse(
            FrontmatterValues.Scalar(mapping, OkfVerificationHistory.TimestampKey));

        return new OkfVerificationEvent(by, WrittenAt(mapping, instant), instant);
    }

    /// <summary>
    /// The <c>at</c> value as written. A quoted stamp is left exactly as the file spells it,
    /// parseable or not: rewriting <c>at: 2026-08-15T14:00:00+02:00</c> into UTC would print an
    /// instant the concept never claimed, and rewriting <c>at: last tuesday</c> into anything
    /// would invent one.
    /// </summary>
    /// <remarks>
    /// The two cases with no written text to echo are YAML's own — a date or datetime resolves to
    /// a non-scalar, and <see cref="FrontmatterValues" /> reads that as no value — so the only
    /// spelling available is the one the parser resolved it to, in the canonical form okf writes.
    /// A quoted <c>""</c> is §11-falsy and reads as no stamp, which is the same answer every
    /// other frontmatter reader in the library gives for the same bytes.
    /// </remarks>
    private static string? WrittenAt(OkfMapping mapping, OkfLifecycleInstant? instant)
    {
        string? written = FrontmatterValues.Scalar(mapping, OkfVerificationHistory.TimestampKey);
        if (written is { Length: > 0 })
        {
            return written;
        }

        return instant is { } parsed ? OkfCanonicalTimestamp.ToCanonical(parsed.Instant) : null;
    }

    private static OkfVerificationEvent Newest(IReadOnlyList<OkfVerificationEvent> events)
    {
        OkfVerificationEvent newest = events[0];
        for (int index = 1; index < events.Count; index++)
        {
            if (IsNewer(events[index], newest))
            {
                newest = events[index];
            }
        }

        return newest;
    }

    private static bool IsNewer(OkfVerificationEvent candidate, OkfVerificationEvent incumbent)
    {
        int rank = Rank(candidate.By).CompareTo(Rank(incumbent.By));
        if (rank != 0)
        {
            return rank < 0;
        }

        if (candidate.Instant is { } mine && incumbent.Instant is { } theirs)
        {
            return OkfLifecycleInstant.IsAfter(mine, theirs);
        }

        // Only one side parses: the parseable instant is the newer of the two, because an
        // unreadable stamp is the oldest thing in the block rather than a rival for latest.
        if (candidate.Instant is not null || incumbent.Instant is not null)
        {
            return candidate.Instant is not null;
        }

        // Neither parses, so the later-written event is the later one — the only ordering left.
        return true;
    }

    /// <summary>
    /// §5.3's ordering of who vouches loudest, as a rank: a person, then a process, then a
    /// producer, then an author written in none of §7's forms.
    /// </summary>
    private static int Rank(string actor) => actor switch
    {
        _ when OkfActor.IsHuman(actor) => RankHuman,
        _ when actor.StartsWith(OkfActor.ProcessPrefix, StringComparison.Ordinal) => RankProcess,
        _ when OkfActor.IsValid(actor) => RankProducer,
        _ => RankOther,
    };

    /// <summary>Renders a count for a report line, invariant and pluralized the way the CLI does.</summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The singular noun.</param>
    /// <returns>The rendered phrase, e.g. <c>2 events</c>.</returns>
    internal static string Plural(int count, string singular) =>
        count == 1
            ? $"1 {singular}"
            : count.ToString(CultureInfo.InvariantCulture) + " " + singular + "s";
}
