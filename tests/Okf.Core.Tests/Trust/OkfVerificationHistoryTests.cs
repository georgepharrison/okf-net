namespace Okf.Core.Tests.Trust;

/// <summary>
/// The three-way verdict over a concept's <c>verified</c> key (work item #74). Every row
/// below states the verdict the ticket's eligibility table specifies; none of them was read
/// off the implementation. The table is the specification: only the SHAPE of the block and
/// each event's AUTHOR are adjudicated, the timestamp never is, and the §7 actor grammar is
/// never consulted.
/// </summary>
/// <remarks>
/// The shape test is deliberately the same one the site's verification renderer already
/// applies — a mapping carrying a non-empty scalar author is a recognizable event, and
/// nothing else is. That is not an accident to tidy up later: the site is the surface that
/// shows a person who vouched for a concept, so a verdict that counted an event the site
/// cannot render would claim history a reader cannot see (issue #84 records the wider
/// reconciliation this deliberately leaves open).
/// </remarks>
/// <remarks>
/// The two theories taking a verdict as an xunit parameter are <c>internal</c>: a theory
/// parameter is passed by value, and a public test method cannot name an internal type
/// (CS0051). xunit discovers non-public test methods, so this costs nothing but a keyword.
/// </remarks>
public class OkfVerificationHistoryTests
{
    /// <summary>
    /// The eligibility table, row by row, exactly as work item #74 specifies it. The
    /// expected verdict is the ticket's column, transcribed rather than derived.
    /// </summary>
    /// <param name="verified">The <c>verified</c> key as written in frontmatter.</param>
    /// <param name="expected">The verdict the ticket specifies for that shape.</param>
    /// <param name="candidate">Whether the ticket says a concept in that shape is a candidate.</param>
    [Theory]
    // | key absent                    | Absent      | yes       |
    [InlineData("absent", null, true)]
    // | `verified:` / `null` / `~`    | Absent      | yes       |
    [InlineData("absent", "verified:", true)]
    [InlineData("absent", "verified: null", true)]
    [InlineData("absent", "verified: ~", true)]
    // | `verified: []`                | Absent      | yes       |
    [InlineData("absent", "verified: []", true)]
    // | bare mapping, author, no time | Present     | no        |
    [InlineData("present", "verified: { by: human:ahormati }", false)]
    [InlineData("present", "verified: { by: process:nightly }", false)]
    // | one event, unparseable stamp  | Present     | no        |
    [InlineData("present", "verified: { by: human:ahormati, at: yesterday }", false)]
    [InlineData("present", "verified:\n  - { by: human:ahormati, at: not-a-date }", false)]
    // | author present, empty id      | Present     | no        |
    // `human:` is a non-empty author naming a human; adjudicating how thin the id is is
    // grammar (#82), not structure, so it stays Present.
    [InlineData("present", "verified: { by: \"human:\" }", false)]
    [InlineData("present", "verified:\n  - { by: \" \" }", false)]
    // | empty mapping                 | Unreadable  | quarantine |
    [InlineData("unreadable", "verified: {}", false)]
    // | sequence of empty mappings    | Unreadable  | quarantine |
    [InlineData("unreadable", "verified: [{}, {}]", false)]
    [InlineData("unreadable", "verified:\n  - {}\n  - {}", false)]
    // | author absent, null, or empty | Unreadable  | quarantine |
    // The empty string is the same fact as absent/null — nobody wrote an author — spelled a
    // third way, so it quarantines too (owner's Q1(a): structural, not "any written scalar").
    [InlineData("unreadable", "verified: { at: 2026-06-25T09:00:00Z }", false)]
    [InlineData("unreadable", "verified: { by: }", false)]
    [InlineData("unreadable", "verified: { by: null }", false)]
    [InlineData("unreadable", "verified: { by: \"\" }", false)]
    [InlineData("unreadable", "verified: { by: '' }", false)]
    [InlineData("unreadable", "verified:\n  - { at: 2026-06-25T09:00:00Z }", false)]
    [InlineData("unreadable", "verified:\n  - { by: null }", false)]
    [InlineData("unreadable", "verified:\n  - { by: \"\" }", false)]
    // | author is a sequence or map   | Unreadable  | quarantine |
    [InlineData("unreadable", "verified:\n  - by: [a, b]", false)]
    [InlineData("unreadable", "verified:\n  - by: { name: ringo }", false)]
    [InlineData("unreadable", "verified:\n  - by:\n    - a\n    - b", false)]
    // | any scalar value              | Unreadable  | quarantine |
    [InlineData("unreadable", "verified: yesterday", false)]
    [InlineData("unreadable", "verified: ahormati", false)]
    [InlineData("unreadable", "verified: 42", false)]
    [InlineData("unreadable", "verified: true", false)]
    // | sequence mixing a legal event with a non-mapping item | Unreadable | quarantine |
    [InlineData("unreadable", "verified:\n  - { by: human:ahormati }\n  - just a string", false)]
    [InlineData("unreadable", "verified:\n  - { by: human:ahormati }\n  - [a, b]", false)]
    [InlineData("unreadable", "verified: [human:ahormati]", false)]
    // The same principle, one shape cleaner: a sequence mixing a legal event with an EMPTY
    // mapping (every item is a mapping, but one names no author) is the same hazard and
    // quarantines too. A sequence is Present only if EVERY item is a recognizable event.
    [InlineData("unreadable", "verified:\n  - { by: human:ahormati }\n  - {}", false)]
    [InlineData("unreadable", "verified:\n  - { by: human:ahormati }\n  - { at: 2026-01-01T00:00:00Z }", false)]
    public void EligibilityTableRowsCarryTheSpecifiedVerdict(
        string verdict,
        string? verified,
        bool candidate)
    {
        OkfMapping frontmatter = Frontmatter(verified);

        OkfVerificationVerdict actual = OkfVerificationHistory.Verdict(frontmatter);

        Assert.Equal(verdict, actual.ToWireString());
        Assert.Equal(candidate, actual.IsCandidate());
    }

    /// <summary>
    /// The falsy scalars that are not the spec's null — <c>0</c>, <c>false</c>, <c>no</c>,
    /// <c>off</c>, and the quoted spellings of null — are scalars, and the table quarantines
    /// "any scalar value". They are here because the normalizer reads all of them as zero
    /// events, which is exactly the silent-skip this verdict exists to refuse: a concept
    /// nobody ever vouched for must not be able to look settled by typing something inert
    /// into <c>verified</c>.
    /// </summary>
    /// <param name="verified">The <c>verified</c> line as written.</param>
    [Theory]
    [InlineData("verified: 0")]
    [InlineData("verified: false")]
    [InlineData("verified: no")]
    [InlineData("verified: off")]
    [InlineData("verified: \"\"")]
    [InlineData("verified: ''")]
    [InlineData("verified: \"null\"")]
    [InlineData("verified: \"~\"")]
    [InlineData("verified: \" \"")]
    public void InertScalarsAreQuarantinedRatherThanReadAsNoHistory(string verified)
    {
        Assert.Equal(
            OkfVerificationVerdict.Unreadable,
            OkfVerificationHistory.Verdict(Frontmatter(verified)));
    }

    /// <summary>
    /// The author test is structural: absent, null and the empty string all quarantine (nobody
    /// wrote an author), while any scalar carrying TEXT is an author — including an inert one
    /// like <c>0</c> or <c>false</c>, and one whose identifier is thin like <c>human:</c>.
    /// Adjudicating that <c>0</c> or <c>human:</c> is a worthless author is the actor grammar
    /// (#82), never this verdict's question, so they stay Present.
    /// </summary>
    /// <param name="author">The <c>by</c> value as written.</param>
    /// <param name="expected">The verdict that shape specifies.</param>
    [Theory]
    [InlineData("~", OkfVerificationVerdict.Unreadable)]
    [InlineData("null", OkfVerificationVerdict.Unreadable)]
    [InlineData("\"\"", OkfVerificationVerdict.Unreadable)]
    [InlineData("0", OkfVerificationVerdict.Present)]
    [InlineData("false", OkfVerificationVerdict.Present)]
    [InlineData("no", OkfVerificationVerdict.Present)]
    [InlineData("off", OkfVerificationVerdict.Present)]
    [InlineData("\"human:\"", OkfVerificationVerdict.Present)]
    internal void TheAuthorTestIsStructuralNotGrammatical(string author, OkfVerificationVerdict expected)
    {
        Assert.Equal(
            expected,
            OkfVerificationHistory.Verdict(Frontmatter($"verified: {{ by: {author} }}")));
    }

    /// <summary>
    /// The ticket adjudicates the author and nothing else: an event with no <c>at</c> at all
    /// is as present as one with a perfect timestamp, and one whose <c>at</c> is a mapping or
    /// a sequence is still an event. Timestamps are not this verdict's question.
    /// </summary>
    /// <param name="event">The event's body as written inside the flow mapping.</param>
    [Theory]
    [InlineData("by: human:ahormati")]
    [InlineData("by: human:ahormati, at: 2026-06-25T09:00:00Z")]
    [InlineData("by: human:ahormati, at: ~")]
    [InlineData("by: human:ahormati, at: total-garbage")]
    [InlineData("by: human:ahormati, at: [2026, 6, 25]")]
    [InlineData("by: human:ahormati, at: { year: 2026 }")]
    [InlineData("by: human:ahormati, note: whatever a producer wrote")]
    public void TheTimestampIsNeverAdjudicated(string @event)
    {
        Assert.Equal(
            OkfVerificationVerdict.Present,
            OkfVerificationHistory.Verdict(Frontmatter($"verified: {{ {@event} }}")));
    }

    /// <summary>
    /// The §7 actor grammar is never consulted. The ticket says so plainly, and the two
    /// spellings it names as quarantined — an author that is ABSENT, and one that is a
    /// sequence or mapping — are both about presence and type, never about whether the string
    /// is a well-formed actor. So a grammar-valid and a grammar-invalid author must land on the
    /// SAME verdict, which is what this asserts: <c>human:ahormati</c> and
    /// <c>Human:ahormati</c> are equally somebody. Issue #82 owns the grammar question.
    /// </summary>
    /// <param name="author">The <c>by</c> value as written, quoted for YAML.</param>
    /// <param name="wellFormed">Whether §7's grammar accepts it — irrelevant to the verdict.</param>
    [Theory]
    [InlineData("human:ahormati", true)]
    [InlineData("Human:ahormati", false)]
    [InlineData("humans:ahormati", false)]
    [InlineData("human:", false)]
    [InlineData("process:", false)]
    [InlineData("a producer/1.0", true)]
    [InlineData("ringo smith", false)]
    [InlineData("ahormati", false)]
    public void TheActorGrammarIsNeverConsulted(string author, bool wellFormed)
    {
        // Sanity check on the row itself: the two columns must actually differ in grammar,
        // or this test would prove nothing about the verdict ignoring it.
        Assert.Equal(wellFormed, OkfActor.IsValid(author));

        // Built by concatenation rather than interpolation: YAML's flow-mapping braces
        // would each need doubling inside an interpolated string, which reads as noise.
        Assert.Equal(
            OkfVerificationVerdict.Present,
            OkfVerificationHistory.Verdict(Frontmatter("verified: { by: \"" + author + "\" }")));
    }

    /// <summary>
    /// A sequence where EVERY item is a recognizable event is Present: two signed events, each
    /// a mapping carrying a non-empty author. This is the only shape a sequence can be Present
    /// in — the principle is "every item recognizable", not "at least one is".
    /// </summary>
    [Fact]
    public void EveryItemARecognizableEventIsPresent()
    {
        var verdict = OkfVerificationHistory.Verdict(Frontmatter("""
            verified:
              - { by: process:nightly, at: 2026-08-01T00:00:00Z }
              - { by: "human:ringo", at: 2026-08-10T00:00:00Z }
            """));

        Assert.Equal(OkfVerificationVerdict.Present, verdict);
        Assert.False(verdict.IsCandidate());
    }

    /// <summary>
    /// The general principle closing the unlisted shapes: a structure that claims
    /// verification but cannot be safely read as events is quarantined. A merge key is the
    /// case in point — it is a mapping, and its <c>by</c> lives behind <c>&lt;&lt;</c> that
    /// okf-net does not resolve, so reading it as "no author" would quarantine a concept that
    /// does name a verifier. Reading it as an event would bless a shape nobody can attribute.
    /// It is not a recognizable event, so it quarantines, and the reason names the shape.
    /// </summary>
    [Fact]
    public void AMergeKeyClaimsVerificationButNamesNoAuthorAndIsQuarantined()
    {
        var frontmatter = Frontmatter("verified: { <<: { by: human:ahormati } }");

        Assert.Equal(OkfVerificationVerdict.Unreadable, OkfVerificationHistory.Verdict(frontmatter));
    }

    /// <summary>
    /// An alias resolves at parse time, so an anchored event is an ordinary event: there is
    /// nothing unreadable about it and no reason to quarantine a concept someone verified
    /// with a YAML anchor.
    /// </summary>
    [Fact]
    public void AnAliasedEventIsAnOrdinaryEvent()
    {
        var frontmatter = Frontmatter("""
            anchors:
              - &ringo { by: "human:ringo", at: 2026-08-10T00:00:00Z }
            verified:
              - *ringo
            """);

        Assert.Equal(OkfVerificationVerdict.Present, OkfVerificationHistory.Verdict(frontmatter));
    }

    /// <summary>
    /// A block that is neither absent nor a recognizable event but also not a shape the
    /// table names still cannot be read as events, so it quarantines. This is the principle
    /// doing the work rather than the table: a future YAML shape is not a new verdict to
    /// invent.
    /// </summary>
    [Fact]
    public void AnUnlistedShapeThatCannotBeReadAsEventsQuarantines()
    {
        // A nested sequence of sequences: no mapping anywhere, so no author anywhere.
        Assert.Equal(
            OkfVerificationVerdict.Unreadable,
            OkfVerificationHistory.Verdict(Frontmatter("verified: [[a], [b]]")));
    }

    /// <summary>
    /// The sequence rule is one principle: a sequence is Present only if EVERY item is a
    /// recognizable event. Here every item is a mapping (so the block reads as a list of
    /// events) but the second names no author, so it is not a recognizable event and the whole
    /// block quarantines — even though the first event was signed. "At least one author" is the
    /// reading that lets a garbage entry ride along and hide the concept from review, which is
    /// the hazard this verdict exists to close.
    /// </summary>
    [Fact]
    public void ASequenceWhereSomeButNotEveryEventNamesAnAuthorQuarantines()
    {
        var verdict = OkfVerificationHistory.Verdict(Frontmatter("""
            verified:
              - { by: "human:ringo", at: 2026-08-10T00:00:00Z }
              - { at: 2026-08-11T00:00:00Z }
            """));

        Assert.Equal(OkfVerificationVerdict.Unreadable, verdict);
    }

    /// <summary>
    /// The verdict is about the <c>verified</c> key and nothing else: a concept with a
    /// generation stamp, a draft status, and a stale date is still judged on its
    /// verification history alone.
    /// </summary>
    [Fact]
    public void OnlyTheVerificationKeyIsRead()
    {
        var frontmatter = new OkfMapping();
        frontmatter.Add("type", "Concept");
        frontmatter.Add("status", "draft");
        frontmatter.Add("generated", OkfValue.Scalar("nonsense"));

        Assert.Equal(OkfVerificationVerdict.Absent, OkfVerificationHistory.Verdict(frontmatter));
    }

    /// <summary>
    /// A null frontmatter is a caller bug, not a shape to classify: quarantining it would
    /// turn a programming error into a verdict about a concept that was never read.
    /// </summary>
    [Fact]
    public void ANullFrontmatterThrows()
    {
        Assert.Throws<ArgumentNullException>(() => OkfVerificationHistory.Verdict((OkfMapping)null!));
        Assert.Throws<ArgumentNullException>(() => OkfVerificationHistory.Verdict((OkfConcept)null!));
    }

    /// <summary>
    /// The verdict's wire spelling is the JSON contract #77 will print, so it is fixed here
    /// rather than invented by a renderer. Hyphenated, lowercase, like the trust tiers'.
    /// </summary>
    [Fact]
    public void TheVerdictsHaveStableWireSpellings()
    {
        Assert.Equal("absent", OkfVerificationVerdict.Absent.ToWireString());
        Assert.Equal("present", OkfVerificationVerdict.Present.ToWireString());
        Assert.Equal("unreadable", OkfVerificationVerdict.Unreadable.ToWireString());
    }

    /// <summary>
    /// A quarantine reason needs its own stable wire spelling too, and the one this ticket
    /// can name is the verification structure: #76 adds a second reason for frontmatter that
    /// does not parse at all, and the two must never share a spelling.
    /// </summary>
    [Fact]
    public void TheQuarantineReasonsHaveStableWireSpellings()
    {
        Assert.Equal(
            "verification-structure",
            OkfQuarantineReason.VerificationStructure.ToWireString());
    }

    private static OkfMapping Frontmatter(string? verifiedLine)
    {
        string frontmatter = verifiedLine is null ? "type: Concept" : $"type: Concept\n{verifiedLine}";
        return OkfDocument.Parse($"---\n{frontmatter}\n---\n\nBody.\n").Frontmatter;
    }
}
