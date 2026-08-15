namespace Okf.Core.Tests;

/// <summary>
/// The canonical timestamp form okf-net writes: RFC 3339 UTC, <c>Z</c>-suffixed, second
/// precision. Expected values come from the reference bundles' own stamps
/// (<c>2026-06-30T14:00:00Z</c>) and from RFC 3339, not from the formatter under test.
/// </summary>
public class OkfCanonicalTimestampTests
{
    [Theory]
    [InlineData("2026-08-14T20:41:50-05:00", "2026-08-15T01:41:50Z")]
    [InlineData("2026-06-30T14:00:00Z", "2026-06-30T14:00:00Z")]
    [InlineData("2026-01-01T00:00:00+09:00", "2025-12-31T15:00:00Z")]
    public void AnyOffsetRendersAsTheSameInstantInUtc(string input, string expected)
    {
        Assert.Equal(expected, OkfCanonicalTimestamp.ToCanonical(DateTimeOffset.Parse(input, null)));
    }

    [Fact]
    public void SubsecondPrecisionIsDroppedRatherThanRounded()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 14, 0, 0, 999, TimeSpan.Zero);

        Assert.Equal("2026-08-15T14:00:00Z", OkfCanonicalTimestamp.ToCanonical(instant));
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

        var canonical = instants.Select(OkfCanonicalTimestamp.ToCanonical).ToArray();

        Assert.Equal(
            instants.OrderBy(instant => instant).Select(OkfCanonicalTimestamp.ToCanonical),
            canonical.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z")]
    [InlineData("2026-01-01T00:00:00+09:00")]
    [InlineData("2025-12-31T19:00:00-05:00")]
    [InlineData("2026-01-01T00:00:00.123456Z")]
    [InlineData("2026-01-01")]
    public void ReadingStaysTolerantOfEverySpellingTheFormatAllows(string stamp)
    {
        // The canonical form is what okf-net WRITES. Nothing rejects another spelling and
        // nothing rewrites one, so every rule that reads `generated.at` has to keep
        // working on the bundles that already exist — including the three spellings one
        // repository managed to accumulate (dogfood friction #19-9, #20-12).
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept(stamp, lastModified: "2026-06-01"));

        var drift = Assert.Single(bundle.Lint(), diagnostic => diagnostic.RuleId == OkfRules.SourceDrift);

        Assert.Contains("moved", drift.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStampIsComparedOnTheCalendarDayItSpells()
    {
        // The comparison against `sources[].last_modified` — a zone-less calendar date —
        // reads the stamp's own leading date rather than converting it. That is the only
        // reading a zone-less date supports: an offset stamp records the author's day, and
        // an author who wrote on 2026-01-01 did not write on 2025-12-31 because their
        // clock was ahead of UTC.
        //
        // The consequence is the argument for having a canonical form at all: these two
        // stamps are the same instant, and only one of them shares a calendar day with the
        // source. Nothing here is a defect to fix in the comparison; it is why `okf init`
        // and every stamp okf-net writes go through OkfCanonicalTimestamp.
        using var sameDay = new TempBundle();
        sameDay.Add("concept.md", Concept("2026-01-01T00:00:00+09:00", lastModified: "2026-01-01"));

        using var dayBefore = new TempBundle();
        dayBefore.Add("concept.md", Concept("2025-12-31T15:00:00Z", lastModified: "2026-01-01"));

        Assert.DoesNotContain(OkfRules.SourceDrift, sameDay.LintIds());
        Assert.Contains(OkfRules.SourceDrift, dayBefore.LintIds());
    }

    [Theory]
    [InlineData("2026-06-01T00:00:00Z")]
    [InlineData("2026-06-01T23:59:59-05:00")]
    [InlineData("2026-06-01")]
    public void StalenessIsFormIndependentBecauseItComparesDaysOnly(string staleAfter)
    {
        // §5.5's `stale_after` is a day, and only the leading date is read, so a bare
        // date, an offset stamp and a canonical one are the same input. TempBundle.Today
        // is 2026-06-01, and staleness is `today >= stale_after`.
        var frontmatter = OkfDocument.Parse($"---\ntype: Reference\nstale_after: {staleAfter}\n---\n\n# Body\n").Frontmatter;

        Assert.True(OkfDocument.IsStale(frontmatter, TempBundle.Today));
        Assert.False(OkfDocument.IsStale(frontmatter, TempBundle.Today.AddDays(-1)));
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
        Assert.Equal(expected, OkfCanonicalTimestamp.IsCanonical(text));
    }

    private static string Concept(string generatedAt, string lastModified) => $$"""
        ---
        type: Reference
        title: Mixed Forms
        description: d
        tags: [t]
        generated: { by: okf-net/tests, at: {{generatedAt}} }
        sources:
          - id: moved
            resource: https://example.invalid/one
            last_modified: {{lastModified}}
        ---

        # Mixed Forms

        Claims.[^moved]
        """;
}
