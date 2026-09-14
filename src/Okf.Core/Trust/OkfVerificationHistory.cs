namespace Okf.Core.Trust;

/// <summary>
/// Whether a concept has any readable verification history (work item #74). Three values,
/// because "no" and "I could not tell you" are different answers, and collapsing them is how
/// malformed metadata hides a concept from review.
/// </summary>
/// <remarks>
/// This is NOT the trust tier's question. §5.3 asks *who* vouches for a concept; this asks
/// whether anybody wrote themselves down at all. A concept whose only event carries a garbage
/// timestamp has someone standing behind it and is <see cref="Present" /> here, while
/// <see cref="OkfTrustTier.Unverified" /> and <see cref="Present" /> can both describe the
/// same concept — the tier reads events through the normalizer's tolerances, this reads them
/// through the shape test the site's renderer uses. Issue #84 records that reconciliation as
/// deliberately open; a concept whose verdict disagrees with its tier is behaving as designed.
///
/// DECISIONS BAKED INTO THESE THREE VALUES (recorded so #82 and #84 do not re-litigate them)
/// -----------------------------------------------------------------------------------------
/// 1. THE AUTHOR TEST IS STRUCTURAL, NOT GRAMMATICAL. A recognizable event carries a NON-EMPTY
///    SCALAR author. Absent, null and the empty string are the same fact — nobody wrote an
///    author — in three YAML spellings, and all quarantine. Everything else that is a scalar
///    with text is an author, so <c>{ by: "human:" }</c> stays <see cref="Present" />: there IS
///    a non-empty author naming a human, and deciding that its identifier is too thin to count
///    is the actor grammar, which is #82 and never this verdict's question. This is why the test
///    is the scalar's TEXT length and not §11 truthiness — truthiness would quarantine
///    <c>human:</c> because it is a falsy scalar, collapsing a structural test into a
///    grammatical one.
/// 2. THE SEQUENCE RULE IS ONE PRINCIPLE, NOT A LIST OF SHAPES. A sequence is
///    <see cref="Present" /> only when EVERY item is a recognizable event; any item that is not
///    — a bare string, a nested sequence, or a mapping naming no author — quarantines the whole
///    block. "At least one event is signed" is rejected precisely because it lets a malformed
///    entry ride along inside an otherwise-readable list and hide the concept from review. The
///    ratified asymmetry survives: <c>verified: []</c> stays <see cref="Absent" /> (nothing in it
///    could be malformed) and <c>verified: [{}]</c> stays <see cref="Unreadable" />.
/// 3. A FENCELESS FILE IS NOT THIS VERDICT'S QUESTION ANY MORE (#76). <c>OkfDocument.Parse</c>
///    still reads a file with no frontmatter fence as an EMPTY mapping, so this verdict alone
///    would answer <see cref="Absent" /> for it — which is what #74 recorded, and what #76
///    superseded: a file whose frontmatter was never FOUND is not a file shown to lack history.
///    <see cref="OkfConceptWalk" /> now asks
///    <see cref="OkfDocument.FrontmatterReadOf" /> first and hands such a file to the scanner as
///    unreadable, so it is quarantined under <see cref="OkfQuarantineReason.FrontmatterUnreadable"
///    /> and never reaches this verdict. That is why deleting three dashes can no longer move a
///    file between "candidate" and "quarantined".
/// </remarks>
internal enum OkfVerificationVerdict
{
    /// <summary>
    /// The <c>verified</c> key is absent, null, or an empty sequence: history is provably
    /// absent, so the concept is a candidate for review.
    /// </summary>
    Absent,

    /// <summary>
    /// The block reads as events and every one of them is recognizable. Whether a timestamp
    /// parses, and whether an actor is well-formed per §7, are both somebody else's question.
    /// </summary>
    Present,

    /// <summary>
    /// The block claims verification but cannot be safely read as events. Neither absent nor
    /// present: an enumeration that called this either would be lying in one direction or the
    /// other, so it is named and set aside instead.
    /// </summary>
    Unreadable,
}

/// <summary>Conversions for <see cref="OkfVerificationVerdict" />.</summary>
internal static class OkfVerificationVerdictExtensions
{
    /// <summary>
    /// Whether a concept with this verdict is a review candidate. Only provable absence
    /// qualifies: <see cref="OkfVerificationVerdict.Unreadable" /> is quarantined by the
    /// scanner, never blessed with a candidate listing.
    /// </summary>
    /// <param name="verdict">The verdict to test.</param>
    /// <returns><see langword="true" /> when the concept's history is provably absent.</returns>
    public static bool IsCandidate(this OkfVerificationVerdict verdict) =>
        verdict == OkfVerificationVerdict.Absent;

    /// <summary>The wire spelling of a verdict, which is the JSON contract's value (#77).</summary>
    /// <param name="verdict">The verdict to render.</param>
    /// <returns>Its wire spelling.</returns>
    public static string ToWireString(this OkfVerificationVerdict verdict) => verdict switch
    {
        OkfVerificationVerdict.Absent => "absent",
        OkfVerificationVerdict.Present => "present",
        OkfVerificationVerdict.Unreadable => "unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
    };
}

/// <summary>
/// The one reading of a concept's <c>verified</c> key that answers "does it have any readable
/// verification history?" (work items #74 and #78).
/// </summary>
/// <remarks>
/// Consumed from two surfaces and no more: <see cref="OkfCandidateScanner" />, which quarantines
/// what it cannot classify, and <c>OkfLinter</c>, which reports the same shapes as <c>OKF0203</c>.
/// That is the whole reason the verdict is one type rather than a predicate each surface writes —
/// a consumer that re-decides readability can disagree with the quarantine list beside it.
/// </remarks>
/// <remarks>
/// WHY A SECOND READING EXISTS
/// ---------------------------
/// <see cref="OkfDocument.NormalizeVerified" /> is the shared hinge for the trust tier, lint,
/// search, the inbox, the site, and the stamping safety check, and every one of its
/// tolerances is pinned by a test. It reads <c>verified: "ahormati"</c> as zero events and
/// <c>verified: {}</c> as one. So a verifier built on that reading would re-review a concept
/// somebody tried to mark verified AND silently skip a concept nobody ever vouched for — the
/// second being the unforgivable one, since the whole point of the enumeration is that nothing
/// escapes review because a tool decided it was uninteresting. Widening the normalizer moves
/// all six consumers at once (#84); this reads the same bytes a second way, narrowly, and
/// leaves the hinge alone.
///
/// THE SHAPE TEST
/// --------------
/// An event is structurally recognizable exactly when it is a mapping carrying a non-empty
/// scalar author — the same test <c>OkfSiteBuilder.Event</c> applies before it will render an
/// event, and <see cref="OkfDocument" /> applies before it will call an actor human. Reusing
/// that test is the point: the site is the surface that shows a person who stood behind a
/// concept, so a verdict counting an event the site cannot draw would claim history no reader
/// can see.
///
/// What is adjudicated is the shape and the author. The timestamp never is — <c>at</c> is not
/// read at all. The §7 actor grammar never is — <c>human:</c>, <c>process:</c> and
/// producer/version spellings are all equally an author, and <c>Human:ahormati</c> is as much
/// a somebody as <c>human:ahormati</c>; issue #82 owns the grammar question.
///
/// WHY THE AUTHOR TEST IS STRUCTURAL, NOT GRAMMATICAL
/// --------------------------------------------------
/// The test is "is there a non-empty scalar author", full stop. Absent, null and the empty
/// string are the same fact — nobody wrote an author — stated in three YAML spellings, and all
/// three quarantine. Everything else that is a scalar carrying text is an author: <c>human:</c>,
/// <c>ahormati</c>, <c>0</c>, <c>false</c> are all somebody who wrote themselves down. This is
/// deliberately NOT §11 truthiness (<see cref="FrontmatterValues.Scalar" />): truthiness would
/// quarantine <c>by: human:</c> because it is a falsy scalar, which turns a structural test
/// into a grammatical one — deciding that an identifier is too thin to count is #82's question,
/// never this one. The test is the scalar's TEXT, with YAML null folded into "no author".
///
/// THE SEQUENCE RULE IS ONE PRINCIPLE
/// ----------------------------------
/// A sequence is Present only when EVERY item is a recognizable event; any item that is not —
/// a bare string, a nested sequence, or a mapping naming no author — quarantines the whole
/// block. A concept must never be SKIPPED by an automated review because a malformed entry rode
/// along inside an otherwise-readable list, so "at least one event is signed" is rejected as a
/// reading. Stated as a principle rather than a list of quarantined shapes, because the shapes
/// nobody enumerated are resolved the same way (see the tie-breaker below).
/// </remarks>
internal static class OkfVerificationHistory
{
    /// <summary>The frontmatter key whose presence and structure are adjudicated (§5.2).</summary>
    public const string Key = "verified";

    /// <summary>The author key inside one verification event (§5.2).</summary>
    public const string AuthorKey = "by";

    /// <summary>The instant key inside one verification event (§5.2).</summary>
    public const string TimestampKey = "at";

    /// <summary>
    /// Whether a <c>verified</c> block claims verification but cannot be safely read as events —
    /// the one question <c>okf candidates</c> quarantines on and <c>okf lint</c> reports as
    /// <c>OKF0203</c>, so neither surface states the shape test itself.
    /// </summary>
    /// <remarks>
    /// Exposed inside the library rather than published: the two consumers are in this assembly,
    /// and a public "is this malformed" helper would invite a third reading of the same block
    /// from outside it.
    /// </remarks>
    /// <param name="frontmatter">The frontmatter to read.</param>
    /// <returns><see langword="true" /> for exactly the shapes <see cref="OkfVerificationVerdict.Unreadable" /> names.</returns>
    internal static bool IsUnreadable(OkfMapping frontmatter) =>
        Verdict(frontmatter) == OkfVerificationVerdict.Unreadable;

    /// <summary>Asks a concept whether it has any readable verification history.</summary>
    /// <param name="concept">The concept to read. Its frontmatter is the only thing consulted.</param>
    /// <returns>The verdict.</returns>
    public static OkfVerificationVerdict Verdict(OkfConcept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return Verdict(concept.Frontmatter);
    }

    /// <summary>Asks frontmatter whether it carries any readable verification history.</summary>
    /// <param name="frontmatter">The frontmatter to read.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frontmatter" /> is null — a caller bug, not a shape to classify.</exception>
    public static OkfVerificationVerdict Verdict(OkfMapping frontmatter)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);

        if (!frontmatter.TryGetValue(Key, out OkfValue? value) || IsNull(value))
        {
            return OkfVerificationVerdict.Absent;
        }

        switch (value)
        {
            // An empty sequence claims nothing: `verified: []` is a producer saying "checked,
            // nobody signed", which is absence of history rather than a block that cannot be
            // read. The table's `verified: []` row is this case.
            case OkfSequence { Count: 0 }:
                return OkfVerificationVerdict.Absent;

            // Any scalar is a claim of verification that is not a structure of events at all:
            // `verified: ahormati`, `verified: 0`, `verified: "null"`. The normalizer reads
            // every one of them as zero events, which is how a concept opts out of review by
            // typing something inert. The table quarantines "any scalar value"; this is that
            // row, and it is the row the feature exists for.
            case OkfScalar:
                return OkfVerificationVerdict.Unreadable;

            case OkfMapping mapping:
                return Recognizes(mapping) ? OkfVerificationVerdict.Present : OkfVerificationVerdict.Unreadable;

            case OkfSequence sequence:
                // ONE principle, not a list of quarantined shapes: a sequence is Present only
                // when EVERY item is a recognizable event; any item that is not — a bare
                // string, a nested sequence, or a mapping that names no author — quarantines
                // the whole block. A concept must never be SKIPPED by an automated review
                // because a malformed entry rode along inside an otherwise-readable list, and
                // "somebody signed at least one event" is exactly the reading that lets a
                // garbage entry hide the concept. This is the table's mixed-sequence row
                // generalised: mixing a legal event with a non-mapping junk item quarantines,
                // and mixing one with an empty mapping is the same hazard in a cleaner coat.
                // The ratified asymmetry survives: `verified: []` stays Absent (nothing in it
                // could be malformed) and `verified: [{}]` stays Unreadable.
                return sequence.All(item => item is OkfMapping mapping && Recognizes(mapping))
                    ? OkfVerificationVerdict.Present
                    : OkfVerificationVerdict.Unreadable;

            default:
                return OkfVerificationVerdict.Unreadable;
        }
    }

    /// <summary>
    /// Whether a mapping is a structurally recognizable verification event: it carries a
    /// non-empty scalar author.
    /// </summary>
    /// <remarks>
    /// Exposed inside the library because <see cref="OkfVerificationStamp" /> prints the events
    /// this verdict has already decided are recognizable. Two shape tests would be two answers to
    /// one question, and a renderer that printed an event the verdict refuses to count would
    /// disagree with the quarantine list beside it.
    /// </remarks>
    internal static bool Recognizes(OkfMapping mapping) =>
        mapping.TryGetValue(AuthorKey, out OkfValue? author) && IsAuthor(author);

    /// <summary>
    /// Whether a value is a non-empty scalar author. STRUCTURAL, not grammatical: absent, null
    /// and the empty string are the same fact — nobody wrote an author — stated in three YAML
    /// spellings, and all three quarantine. Everything else that is a scalar with text is an
    /// author, whatever it says: <c>human:</c>, <c>ahormati</c>, <c>0</c>, <c>false</c> are all
    /// somebody who wrote themselves down. Adjudicating how thin or malformed an identifier is
    /// is the actor grammar, which is #82 and never this verdict's question.
    /// </summary>
    /// <remarks>
    /// This is deliberately NOT §11 truthiness, which <see cref="FrontmatterValues.Scalar" />
    /// applies: truthiness would quarantine <c>by: human:</c> (a falsy scalar) even though it
    /// carries a non-empty author, collapsing a structural test into a grammatical one. The
    /// test is the scalar's TEXT length, with YAML null excluded because null is the "no author
    /// written" case, not an empty one.
    /// </remarks>
    private static bool IsAuthor(OkfValue? value) =>
        value is OkfScalar scalar && !scalar.IsNull && scalar.Value.Length > 0;

    /// <summary>
    /// Whether a value is YAML null — the key written with nothing after it, <c>null</c>, or
    /// <c>~</c>. Only the unquoted spellings: <c>verified: "null"</c> is a string, and the
    /// table quarantines a scalar.
    /// </summary>
    /// <remarks>
    /// <see cref="OkfScalar.IsNull" /> is the repo's own answer to that question and already
    /// excludes quoted scalars, which is why this is not a truthiness test.
    /// </remarks>
    private static bool IsNull(OkfValue? value) => value is null || (value is OkfScalar scalar && scalar.IsNull);
}
