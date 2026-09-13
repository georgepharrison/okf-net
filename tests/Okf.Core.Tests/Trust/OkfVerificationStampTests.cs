using System.Globalization;

namespace Okf.Core.Tests.Trust;

/// <summary>
/// The <c>verified</c> block read as the history it records rather than as a verdict: the
/// latest event, the first event, and every event in between (work item #75). Expectations come
/// from spec §5.2 and from #74's shape test, not from what the reader happens to do.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS BESIDE THE VERDICT
/// ----------------------------------
/// <see cref="OkfVerificationHistory" /> answers one question — is there any readable history —
/// and answers it three-valued. The candidate renderer needs the other half: a row that says
/// <em>who</em> stood behind a concept and <em>when</em>, which is the detail that explains why
/// that concept is not on the list. Both readings consult the same shape test, so the two can
/// never disagree about whether an event counts.
///
/// THE SHAPE TEST IS #74'S, VERBATIM
/// --------------------------------
/// An event is a mapping carrying a non-empty scalar author. The timestamp is never read, so a
/// garbage <c>at</c> on a signed event is still a somebody; <c>{ by: "human:" }</c> is still an
/// author, because deciding the identifier is too thin is the actor grammar (#82). A malformed
/// block is not silently dropped here either — it is reported as <see cref="MalformedEventCount" />
/// so a renderer can say "the rest of this block could not be read" instead of printing a
/// shorter list and looking like it read all of it.
/// </remarks>
public class OkfVerificationStampTests
{
    /// <summary>
    /// Every shape the block can take, and what the reader says about it. The first four rows
    /// are the same four the #74 table calls <c>Absent</c>; the rows after them are the ones
    /// #74 calls <c>Present</c> or <c>Unreadable</c>.
    /// </summary>
    /// <param name="verified">The <c>verified</c> line as written, or null for no key.</param>
    /// <param name="events">How many recognizable events the reader returns.</param>
    /// <param name="malformed">How many items it refused to read as events.</param>
    /// <param name="latestBy">The latest recognizable event's author, or null for none.</param>
    /// <param name="latestAt">The latest recognizable event's timestamp as written, or null.</param>
    /// <param name="history">Whether the block records history — false only for the shapes #74 calls Absent.</param>
    [Theory]
    [InlineData(null, 0, 0, null, null, false)]
    [InlineData("verified:", 0, 0, null, null, false)]
    [InlineData("verified: null", 0, 0, null, null, false)]
    [InlineData("verified: []", 0, 0, null, null, false)]
    [InlineData("verified: { by: human:ahormati }", 1, 0, "human:ahormati", null, true)]
    [InlineData("verified: { by: human:ahormati, at: yesterday }", 1, 0, "human:ahormati", "yesterday", true)]
    [InlineData("verified: { by: \"human:\" }", 1, 0, "human:", null, true)]
    [InlineData("verified:\n  - { by: \"human:ringo\", at: 2026-06-25T09:00:00Z }", 1, 0, "human:ringo", "2026-06-25T09:00:00Z", true)]
    [InlineData("verified: {}", 0, 1, null, null, true)]
    [InlineData("verified: { at: 2026-06-25T09:00:00Z }", 0, 1, null, null, true)]
    [InlineData("verified: { by: }", 0, 1, null, null, true)]
    [InlineData("verified: { by: \"\" }", 0, 1, null, null, true)]
    [InlineData("verified: ahormati", 0, 1, null, null, true)]
    [InlineData("verified: 42", 0, 1, null, null, true)]
    [InlineData("verified: true", 0, 1, null, null, true)]
    [InlineData("verified:\n  - { by: human:ahormati }\n  - a string", 1, 1, "human:ahormati", null, true)]
    [InlineData("verified:\n  - {}", 0, 1, null, null, true)]
    [InlineData("verified:\n  - { by: [a, b] }", 0, 1, null, null, true)]
    [InlineData("verified:\n  - { by: { name: ringo } }", 0, 1, null, null, true)]
    public void EachShapeReportsTheEventsItCanReadAndTheOnesItCannot(
        string? verified,
        int events,
        int malformed,
        string? latestBy,
        string? latestAt,
        bool history)
    {
        OkfVerificationStamp stamp = Read(verified);

        Assert.Equal(events, stamp.Events.Count);
        Assert.Equal(malformed, stamp.MalformedEventCount);

        // `HasHistory` is the verdict's answer, so it is false for exactly the four shapes #74
        // calls Absent — including the quarantined ones, which DID have a block.
        Assert.Equal(history, stamp.HasHistory);
        Assert.Equal(history, OkfVerificationHistory.Verdict(Verified(verified)) != OkfVerificationVerdict.Absent);
        Assert.Equal(latestBy, stamp.Latest?.By);
        Assert.Equal(latestAt, stamp.Latest?.At);
    }

    /// <summary>
    /// Every event is returned in the order it was written, with both fields as written: the
    /// renderer prints what the file says rather than a normalisation of it, which is the
    /// convention every other okf report follows.
    /// </summary>
    [Fact]
    public void EveryEventIsReturnedInSourceOrderWithBothFieldsAsWritten()
    {
        OkfVerificationStamp stamp = Read("""
            verified:
              - { by: "human:ringo", at: 2026-06-25T09:00:00Z }
              - { by: "process:check", at: 2026-07-01T00:00:00Z }
            """);

        Assert.Equal(
            [("human:ringo", "2026-06-25T09:00:00Z"), ("process:check", "2026-07-01T00:00:00Z")],
            stamp.Events.Select(verification => (verification.By, verification.At)).ToArray());

        // `First` is the first thing written, which is a different fact from `Latest` — and here
        // they happen to coincide, because a person outranks a process (§5.3) whatever the dates.
        Assert.Equal("human:ringo", stamp.First?.By);
        Assert.Equal("human:ringo", stamp.Latest?.By);
    }

    /// <summary>
    /// "Latest" is the newest by the timestamps that parse — but only within one rank (§5.3), so
    /// the ordering here is a producer's newest stamp over an older one, not over a person.
    /// </summary>
    [Fact]
    public void TheLatestEventIsTheNewestTimestampRatherThanTheLastItem()
    {
        OkfVerificationStamp stamp = Read("""
            verified:
              - { by: "process:check", at: 2026-07-01T00:00:00Z }
              - { by: "process:audit", at: 2026-08-01T00:00:00Z }
              - { by: "process:index", at: 2026-06-01T00:00:00Z }
            """);

        Assert.Equal("process:audit", stamp.Latest?.By);
        Assert.Equal("2026-08-01T00:00:00Z", stamp.Latest?.At);
        Assert.Equal("process:check", stamp.First?.By);
    }

    /// <summary>
    /// The §5.3 ranking is real and outranks the clock: a human's older stamp beats a
    /// machine's newer one, whichever machine wrote it. §5.3 ranks `human:` above everything
    /// and says nothing about agent-versus-process, so those two are not ordered against each
    /// other and are not tested as if they were. Without these cases the ranking is dead code:
    /// every other test here keeps one rank or puts the human last, so inverting the table
    /// entirely still passes them.
    /// </summary>
    [Theory]
    [InlineData("human:ringo", "2026-01-01T00:00:00Z", "agent:helper", "2026-09-01T00:00:00Z", "human:ringo")]
    [InlineData("human:ringo", "2026-01-01T00:00:00Z", "process:check", "2026-09-01T00:00:00Z", "human:ringo")]
    [InlineData("human:ringo", "2026-01-01T00:00:00Z", "unknown:something", "2026-09-01T00:00:00Z", "human:ringo")]
    public void AHigherRankedAuthorWinsEvenWhenItsStampIsOlder(
        string firstBy,
        string firstAt,
        string secondBy,
        string secondAt,
        string expectedBy)
    {
        // Built with string.Format rather than interpolation: a YAML flow mapping opens each
        // item with `{`, which an interpolated raw string reads as a substitution instead.
        string Block(string one, string oneAt, string two, string twoAt) =>
            string.Format(
                CultureInfo.InvariantCulture,
                """
                verified:
                  - {{ by: "{0}", at: {1} }}
                  - {{ by: "{2}", at: {3} }}
                """,
                one,
                oneAt,
                two,
                twoAt);

        OkfVerificationStamp stamp = Read(Block(firstBy, firstAt, secondBy, secondAt));

        Assert.Equal(expectedBy, stamp.Latest?.By);

        // Listed the other way round too, so the answer is the ranking and not the item order.
        OkfVerificationStamp reversed = Read(Block(secondBy, secondAt, firstBy, firstAt));

        Assert.Equal(expectedBy, reversed.Latest?.By);
    }

    /// <summary>
    /// A date at day precision is a real instant and ranks against second-precision ones:
    /// §5.2 permits both, and an unparseable timestamp is treated as the oldest thing in the
    /// block rather than dropped, so a signed event is never demoted out of the row because
    /// somebody wrote a date the parser dislikes.
    /// </summary>
    [Fact]
    public void ADayPrecisionStampRanksAgainstSecondPrecisionOnes()
    {
        OkfVerificationStamp day = Read("""
            verified:
              - { by: "human:ringo", at: 2026-08-01 }
              - { by: "process:check", at: 2026-06-01T00:00:00Z }
            """);

        // The date is reported as written — a bare date says "that day", and inventing a
        // midnight for it would print an instant the file never claimed.
        Assert.Equal("human:ringo", day.Latest?.By);
        Assert.Equal("2026-08-01", day.Latest?.At);
        Assert.False(day.Latest?.Instant?.HasTime);
    }

    /// <summary>
    /// A human anywhere in the block outranks a producer, whatever the timestamps say, because
    /// the row's job is to say who stood behind the concept and a person standing behind it is
    /// the strongest answer the block contains.
    /// </summary>
    [Fact]
    public void AHumanAuthorIsReportedAheadOfAProducerWhateverTheDatesSay()
    {
        OkfVerificationStamp stamp = Read("""
            verified:
              - { by: "openai-codex/gpt-5", at: 2026-08-01T00:00:00Z }
              - { by: "human:ringo", at: 2026-01-01T00:00:00Z }
            """);

        Assert.Equal("human:ringo", stamp.Latest?.By);
        Assert.Equal("2026-01-01T00:00:00Z", stamp.Latest?.At);
    }

    /// <summary>
    /// A producer outranks an ungrammatical author. This is the §5.3 ordering, reused rather
    /// than re-derived: the tier already ranks human over process over producer over anything
    /// else, and a renderer that ranked differently would print a row whose "latest
    /// verification" contradicts the tier printed beside it.
    /// </summary>
    [Fact]
    public void AProcessAuthorIsReportedAheadOfAnUngrammaticalOne()
    {
        OkfVerificationStamp stamp = Read("""
            verified:
              - { by: ahormati, at: 2026-08-01T00:00:00Z }
              - { by: "process:check", at: 2026-01-01T00:00:00Z }
            """);

        Assert.Equal("process:check", stamp.Latest?.By);
    }

    /// <summary>
    /// Events of the same rank fall back to the timestamps: newest first, and when neither
    /// timestamp parses the most recently written one, which is the only ordering left.
    /// </summary>
    /// <param name="first">The first event's author and timestamp.</param>
    /// <param name="second">The second event's author and timestamp.</param>
    /// <param name="expectedBy">Whose author the row should print.</param>
    [Theory]
    [InlineData("human:ringo", "2026-06-01T00:00:00Z", "human:ahormati", "2026-08-01T00:00:00Z", "human:ahormati")]
    [InlineData("human:ahormati", "2026-08-01T00:00:00Z", "human:ringo", "2026-06-01T00:00:00Z", "human:ahormati")]
    [InlineData("human:ringo", "yesterday", "human:ahormati", "last tuesday", "human:ahormati")]
    [InlineData("human:ringo", "2026-06-01T00:00:00Z", "human:ahormati", null, "human:ringo")]
    [InlineData("human:ringo", null, "human:ahormati", null, "human:ahormati")]
    public void EqualRankFallsBackToTheTimestampThenToTheWrittenOrder(
        string firstBy,
        string? firstAt,
        string? secondBy,
        string? secondAt,
        string expectedBy)
    {
        OkfVerificationStamp stamp = Read($"verified:\n  - {{ by: \"{firstBy}\", at: {firstAt} }}\n  - {{ by: \"{secondBy}\", at: {secondAt} }}");

        Assert.Equal(expectedBy, stamp.Latest?.By);
    }

    /// <summary>
    /// A malformed item is counted, not dropped in silence: a row that printed "verified once"
    /// for <c>[{ by: human:ringo }, {}]</c> would tell the reader the block held one event when
    /// it holds two, and the second is the reason #74 quarantines the concept.
    /// </summary>
    [Fact]
    public void ABlockMixingReadableAndMalformedItemsReportsBothHalves()
    {
        OkfVerificationStamp stamp = Read("""
            verified:
              - { by: "human:ringo", at: 2026-06-25T09:00:00Z }
              - {}
            """);

        Assert.Single(stamp.Events);
        Assert.Equal(1, stamp.MalformedEventCount);
        Assert.Equal("human:ringo", stamp.Latest?.By);
    }

    /// <summary>
    /// A bare mapping is one event (§5.2), so a concept stamped by <c>okf verify</c> reads as
    /// exactly one verification rather than zero or two.
    /// </summary>
    [Fact]
    public void ABareMappingIsOneEvent()
    {
        OkfVerificationStamp stamp = Read("verified: { by: \"human:ringo\", at: 2026-08-15T14:00:00Z }");

        OkfVerificationEvent only = Assert.Single(stamp.Events);
        Assert.Equal("human:ringo", only.By);
        Assert.Equal("2026-08-15T14:00:00Z", only.At);
    }

    /// <summary>
    /// An author written with no timestamp is still an event and is reported with a null
    /// timestamp rather than an invented one — the same rule the site's event renderer follows.
    /// </summary>
    [Fact]
    public void AnEventWithNoTimestampHasNoTimestamp()
    {
        OkfVerificationStamp stamp = Read("verified: { by: \"human:ringo\" }");

        OkfVerificationEvent only = Assert.Single(stamp.Events);
        Assert.Equal("human:ringo", only.By);
        Assert.Null(only.At);
    }

    /// <summary>
    /// A quoted timestamp that is not in the canonical spelling is reported as the file spells
    /// it: the row prints the concept's own words, and converting <c>+02:00</c> to UTC would
    /// print an instant the concept never claimed.
    /// </summary>
    [Fact]
    public void AQuotedTimestampIsReportedExactlyAsWritten()
    {
        OkfVerificationStamp stamp = Read("verified:\n  - { by: \"human:ringo\", at: \"2026-08-15T14:00:00+02:00\" }");

        Assert.Equal("2026-08-15T14:00:00+02:00", stamp.Latest?.At);

        // It still compares as the instant it is, so ordering is not held hostage to spelling:
        // a later canonical stamp outranks it even though it was written first.
        OkfVerificationStamp both = Read("""
            verified:
              - { by: "human:ringo", at: "2026-08-15T14:00:00+02:00" }
              - { by: "human:ahormati", at: 2026-08-15T13:00:00Z }
            """);

        Assert.Equal("human:ahormati", both.Latest?.By);
    }

    /// <summary>
    /// The reader and the verdict cannot disagree, because they consult one shape test. This is
    /// the pairing the renderer relies on: every concept #74 calls <c>Absent</c> prints "never
    /// verified", and every concept it does not call <c>Absent</c> prints a name.
    /// </summary>
    /// <param name="verified">The <c>verified</c> line as written, or null for no key.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("verified:")]
    [InlineData("verified: null")]
    [InlineData("verified: ~")]
    [InlineData("verified: []")]
    internal void WhateverTheVerdictCallsAbsentHasNoHistoryHere(string? verified) =>
        Assert.False(Read(verified).HasHistory);

    [Theory]
    [InlineData("verified: {}")]
    [InlineData("verified: [{}, {}]")]
    [InlineData("verified: ahormati")]
    [InlineData("verified:\n  - { by: human:ahormati }\n  - a string")]
    internal void WhateverTheVerdictDoesNotCallAbsentStillReportsWhatItReads(string verified)
    {
        OkfVerificationStamp stamp = Read(verified);

        Assert.True(stamp.HasHistory);
        Assert.True(stamp.Events.Count + stamp.MalformedEventCount > 0);
    }

    /// <summary>
    /// The empty history is a value rather than a hole: a renderer with a concept that has no
    /// <c>verified</c> key asks the same question and gets an empty stamp, not an exception and
    /// not a null.
    /// </summary>
    [Fact]
    public void AConceptWithNoBlockReadsAsAnEmptyStamp()
    {
        OkfVerificationStamp stamp = OkfVerificationStamp.Read(OkfDocument.Parse("---\ntype: Concept\n---\n\nBody.\n").Frontmatter);

        // `Assert.Empty` on Latest/First would accept a default struct; these are nullable, so
        // the absence is asserted as absence.
        Assert.Null(stamp.Latest);
        Assert.Null(stamp.First);
        Assert.False(stamp.HasHistory);
        Assert.Empty(stamp.Events);
        Assert.Equal(0, stamp.MalformedEventCount);
    }

    /// <summary>
    /// The same frontmatter read twice yields the same answer, because the reader holds no
    /// state and consults no clock — the property the candidate report depends on when it prints
    /// one row per concept.
    /// </summary>
    [Fact]
    public void ReadingIsRepeatable()
    {
        OkfMapping frontmatter = OkfDocument.Parse(
            "---\ntype: Concept\nverified:\n  - { by: \"human:ringo\", at: 2026-06-25T09:00:00Z }\n---\n\nBody.\n").Frontmatter;

        OkfVerificationStamp first = OkfVerificationStamp.Read(frontmatter);
        OkfVerificationStamp second = OkfVerificationStamp.Read(frontmatter);

        Assert.Equal(first.Latest?.By, second.Latest?.By);
        Assert.Equal(first.Latest?.At, second.Latest?.At);
        Assert.Equal(first.Events.Count, second.Events.Count);
    }

    private static OkfMapping Verified(string? verified) =>
        OkfDocument.Parse($"---\ntype: Concept\n{Line(verified)}\n---\n\nBody.\n").Frontmatter;

    private static OkfVerificationStamp Read(string? verified) => OkfVerificationStamp.Read(Verified(verified));

    private static string Line(string? verified) => verified ?? string.Empty;
}
