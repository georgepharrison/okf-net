namespace Okf.Core.Tests;

/// <summary>
/// The canonical timestamp form okf-net writes: RFC 3339 UTC, <c>Z</c>-suffixed, second
/// precision. Expected values come from the reference bundles' own stamps
/// (<c>2026-06-30T14:00:00Z</c>) and from RFC 3339, not from the formatter under test.
/// </summary>
public class OkfTimestampTests
{
    [Theory]
    [InlineData("2026-08-14T20:41:50-05:00", "2026-08-15T01:41:50Z")]
    [InlineData("2026-06-30T14:00:00Z", "2026-06-30T14:00:00Z")]
    [InlineData("2026-01-01T00:00:00+09:00", "2025-12-31T15:00:00Z")]
    public void AnyOffsetRendersAsTheSameInstantInUtc(string input, string expected)
    {
        Assert.Equal(expected, OkfTimestamp.ToCanonical(DateTimeOffset.Parse(input, null)));
    }

    [Fact]
    public void SubsecondPrecisionIsDroppedRatherThanRounded()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 14, 0, 0, 999, TimeSpan.Zero);

        Assert.Equal("2026-08-15T14:00:00Z", OkfTimestamp.ToCanonical(instant));
    }

    [Fact]
    public void CanonicalStampsSortLexicographicallyInChronologicalOrder()
    {
        // This is the property the form is picked for: every diff, index, and log listing
        // that orders stamps as text gets chronological order for free. A local-offset
        // stamp does not have it — 2026-01-01T00:00:00+09:00 precedes
        // 2025-12-31T20:00:00Z chronologically and follows it as text.
        var instants = new[]
        {
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            new DateTimeOffset(2025, 12, 31, 20, 0, 0, TimeSpan.Zero),
        };

        var canonical = instants.Select(OkfTimestamp.ToCanonical).ToArray();

        Assert.Equal(
            instants.OrderBy(instant => instant).Select(OkfTimestamp.ToCanonical),
            canonical.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("2026-08-15T14:00:00Z", true)]
    [InlineData("2026-08-14T20:41:50-05:00", false)]
    [InlineData("2026-08-15T14:00:00.123Z", false)]
    [InlineData("2026-08-15", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheCanonicalSpellingIsRecognizedAsCanonical(string? text, bool expected)
    {
        Assert.Equal(expected, OkfTimestamp.IsCanonical(text));
    }
}
