namespace Okf.Core.Tests;

/// <summary>
/// <see cref="OkfLifecycleInstant" />: what parses, and what "later" means when one side
/// of a comparison is a bare date and the other is an instant.
/// </summary>
public class OkfLifecycleInstantTests
{
    [Theory]
    [InlineData("2026-08-14")]
    [InlineData("  2026-08-14  ")]
    [InlineData("2026-08-14T20:41:50-05:00")]
    [InlineData("2026-08-14T20:41:50Z")]
    [InlineData("2026-08-14 20:41:50")]
    public void AnIsoValueParses(string text)
    {
        Assert.True(OkfLifecycleInstant.TryParse(text, out var timestamp));
        Assert.Equal(new DateOnly(2026, 8, 14), timestamp.Date);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-8-14")]
    [InlineData("14/08/2026")]
    [InlineData("2026-13-01")]
    public void AValueThatIsNotAnIsoDateDoesNotParse(string? text)
    {
        Assert.False(OkfLifecycleInstant.TryParse(text, out _));
        Assert.Null(OkfLifecycleInstant.Parse(text));
    }

    [Fact]
    public void TheDateIsTheOneWritten_NotTheUtcOne()
    {
        // 2026-08-14T20:41:50-05:00 is 2026-08-15T01:41:50Z. The author's day is the one
        // the document means, and it is the one `okf lint` reads (its first ten chars).
        var timestamp = OkfLifecycleInstant.Parse("2026-08-14T20:41:50-05:00")!.Value;

        Assert.Equal(new DateOnly(2026, 8, 14), timestamp.Date);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 1, 41, 50, TimeSpan.Zero), timestamp.Instant);
    }

    [Fact]
    public void AValueWithNoOffsetIsReadAsUtc()
    {
        // Reading it as local time would make okf output depend on the reader's timezone.
        var timestamp = OkfLifecycleInstant.Parse("2026-08-14T20:41:50")!.Value;

        Assert.Equal(new DateTimeOffset(2026, 8, 14, 20, 41, 50, TimeSpan.Zero), timestamp.Instant);
        Assert.True(timestamp.HasTime);
    }

    [Fact]
    public void ATrailerThatIsNotATimeDegradesToDatePrecision()
    {
        // `okf lint` reads the first ten characters and ignores the rest; refusing the
        // whole value here would make the inbox blind to a concept lint still reports on.
        var timestamp = OkfLifecycleInstant.Parse("2026-08-14-ish")!.Value;

        Assert.Equal(new DateOnly(2026, 8, 14), timestamp.Date);
        Assert.False(timestamp.HasTime);
    }

    [Fact]
    public void TwoInstantsOnTheSameDayCompareAsInstants()
    {
        var earlier = OkfLifecycleInstant.Parse("2026-08-14T08:00:00Z")!.Value;
        var later = OkfLifecycleInstant.Parse("2026-08-14T12:00:00Z")!.Value;

        Assert.True(OkfLifecycleInstant.IsAfter(later, earlier));
        Assert.False(OkfLifecycleInstant.IsAfter(earlier, later));
    }

    [Fact]
    public void ABareDateAgainstAnInstantComparesByDate()
    {
        // A bare date says "that day" and nothing finer, so against a same-day instant it
        // is neither before nor after: picking a time for it — midnight, or the end of the
        // day — would be picking the answer rather than reading it. Both directions are
        // asserted, because a comparison answering "not after" one way and "after" the
        // other would be an ordering that depends on which side you ask from.
        var sourceMoved = OkfLifecycleInstant.Parse("2026-08-15")!.Value;
        var generated = OkfLifecycleInstant.Parse("2026-08-15T01:41:50Z")!.Value;

        Assert.False(OkfLifecycleInstant.IsAfter(sourceMoved, generated));
        Assert.False(OkfLifecycleInstant.IsAfter(generated, sourceMoved));
        Assert.True(OkfLifecycleInstant.IsAfter(OkfLifecycleInstant.Parse("2026-08-16")!.Value, generated));
    }

    [Fact]
    public void TheWiderComparisonTakesEitherPrecisionAndOnlyEverAddsToTheOrdering()
    {
        // `2026-08-15T01:00:00+05:00` is 2026-08-14T20:00Z — an EARLIER instant than
        // 2026-08-14T23:00Z and a LATER written date. Ordering follows the instants;
        // OKF0103 follows the written dates; the wider comparison follows both, so a
        // drifted source cannot be reported by the linter and hidden by the inbox.
        var offsetDate = OkfLifecycleInstant.Parse("2026-08-15T01:00:00+05:00")!.Value;
        var utcInstant = OkfLifecycleInstant.Parse("2026-08-14T23:00:00Z")!.Value;

        Assert.False(OkfLifecycleInstant.IsAfter(offsetDate, utcInstant));
        Assert.True(OkfLifecycleInstant.IsAfterAtEitherPrecision(offsetDate, utcInstant));
        Assert.True(OkfLifecycleInstant.IsAfterAtEitherPrecision(utcInstant, offsetDate));

        // Wider, not weaker: equal stays equal, and an earlier value stays earlier.
        var stamp = OkfLifecycleInstant.Parse("2026-08-15T01:41:50Z")!.Value;
        Assert.False(OkfLifecycleInstant.IsAfterAtEitherPrecision(stamp, stamp));
        Assert.False(OkfLifecycleInstant.IsAfterAtEitherPrecision(OkfLifecycleInstant.Parse("2026-08-14")!.Value, stamp));
        Assert.True(OkfLifecycleInstant.IsAfterAtEitherPrecision(OkfLifecycleInstant.Parse("2026-08-16")!.Value, stamp));
    }

    [Fact]
    public void EqualTimestampsAreNotAfterEachOther()
    {
        var stamp = OkfLifecycleInstant.Parse("2026-08-14T20:41:50-05:00")!.Value;
        var same = OkfLifecycleInstant.Parse("2026-08-15T01:41:50Z")!.Value;

        // Same instant, two spellings: equal, and therefore neither is after the other.
        Assert.Equal(0, OkfLifecycleInstant.Compare(stamp, same));
        Assert.Equal(0, OkfLifecycleInstant.Compare(same, stamp));
        Assert.False(OkfLifecycleInstant.IsAfter(stamp, same));
        Assert.False(OkfLifecycleInstant.IsAfter(same, stamp));
    }

    [Fact]
    public void ANullOperandIsNeverAfterAnything()
    {
        var stamp = OkfLifecycleInstant.Parse("2026-08-14")!.Value;

        Assert.False(OkfLifecycleInstant.IsAfter(null, stamp));
        Assert.False(OkfLifecycleInstant.IsAfter(stamp, null));
        Assert.False(OkfLifecycleInstant.IsAfter(null, null));
    }

    [Theory]
    [InlineData("2026-08-14", 1)]
    [InlineData("2026-08-15", 0)]
    [InlineData("2026-08-20", -5)]
    public void DaysUntilCountsWholeDaysFromTheWrittenDate(string text, int expected)
    {
        var timestamp = OkfLifecycleInstant.Parse(text)!.Value;

        Assert.Equal(expected, timestamp.DaysUntil(new DateOnly(2026, 8, 15)));
    }
}
