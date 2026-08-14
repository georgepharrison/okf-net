namespace Okf.Core.Tests;

/// <summary>
/// Conformance validation (§11), <c>verified</c> normalization (§5.2), trust tiers
/// (§5.3), and staleness (§5.5) — PRD CORE-3 and CORE-5 through CORE-7. Tests named
/// <c>Ported_*</c> are direct ports of the reference implementation's cases.
/// </summary>
public class OkfDocumentTrustTests
{
    [Fact]
    public void Ported_ValidateRejectsMissingType()
    {
        var doc = new OkfDocument(new OkfMapping { { "title", "Y" } }, string.Empty);

        var ex = Assert.Throws<OkfDocumentException>(doc.Validate);

        Assert.Contains("type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ported_ValidateAcceptsTypeOnly()
    {
        // OKF v0.2 §11: `type` is the only always-required key.
        new OkfDocument(new OkfMapping { { "type", "X" } }, string.Empty).Validate();
    }

    [Theory]
    [InlineData("type: X", true)]
    [InlineData("type: \"0\"", true)]
    [InlineData("type: \"  \"", true)]
    [InlineData("type: 42", true)]
    [InlineData("title: Y", false)]
    [InlineData("type:", false)]
    [InlineData("type: null", false)]
    [InlineData("type: ~", false)]
    [InlineData("type: \"\"", false)]
    [InlineData("type: ''", false)]
    [InlineData("type: 0", false)]
    [InlineData("type: false", false)]
    [InlineData("type: no", false)]
    [InlineData("type: []", false)]
    [InlineData("type: {}", false)]
    public void ValidateUsesReferenceTruthinessForRequiredKeys(string frontmatter, bool valid)
    {
        // The reference implementation tests `not self.frontmatter.get(k)`, so the
        // check is Python truthiness over PyYAML's YAML 1.1 resolution: `0`, `false`,
        // `no`, `null`, `""`, `[]` and `{}` are all "missing".
        var doc = OkfDocument.Parse($"---\n{frontmatter}\n---\n\nBody.\n");

        if (valid)
        {
            doc.Validate();
        }
        else
        {
            Assert.Throws<OkfDocumentException>(doc.Validate);
        }
    }

    [Fact]
    public void Ported_NormalizeVerifiedTreatsBareMappingAsList()
    {
        var frontmatter = Frontmatter("verified: { by: human:ahormati, at: 2026-06-25T09:00:00Z }");

        var events = OkfDocument.NormalizeVerified(frontmatter);

        var only = Assert.Single(events);
        Assert.Equal("human:ahormati", OkfValues.Text(only, "by"));
        Assert.Equal("2026-06-25T09:00:00Z", OkfValues.Text(only, "at"));
        Assert.Empty(OkfDocument.NormalizeVerified(new OkfMapping()));
    }

    [Fact]
    public void NormalizeVerifiedTreatsAnExplicitNullAsAbsent()
    {
        Assert.Empty(OkfDocument.NormalizeVerified(Frontmatter("verified:")));
        Assert.Empty(OkfDocument.NormalizeVerified(Frontmatter("verified: null")));
    }

    [Fact]
    public void NormalizeVerifiedDropsNonMappingListElements()
    {
        var frontmatter = Frontmatter("""
            verified:
              - by: process:nightly
              - just a string
              - [a, b]
              - by: human:ahormati
            """);

        var events = OkfDocument.NormalizeVerified(frontmatter);

        Assert.Equal(2, events.Count);
        Assert.Equal(["process:nightly", "human:ahormati"], events.Select(e => OkfValues.Text(e, "by")));
    }

    [Fact]
    public void NormalizeVerifiedIgnoresScalarAndEmptyListShapes()
    {
        Assert.Empty(OkfDocument.NormalizeVerified(Frontmatter("verified: yesterday")));
        Assert.Empty(OkfDocument.NormalizeVerified(Frontmatter("verified: []")));
    }

    [Fact]
    public void NormalizeVerifiedKeepsAnEmptyMappingAsOneEvent()
    {
        // `isinstance({}, dict)` is true in the reference implementation, so an empty
        // mapping is a (contentless) verification event, not an absent one.
        Assert.Single(OkfDocument.NormalizeVerified(Frontmatter("verified: {}")));
    }

    [Fact]
    public void Ported_TrustTier()
    {
        Assert.Equal(OkfTrustTier.Unverified, OkfDocument.TrustTier(new OkfMapping()));

        Assert.Equal(
            OkfTrustTier.MachineConfirmed,
            OkfDocument.TrustTier(Frontmatter("verified:\n  - { by: process:finance-nightly, at: x }")));

        Assert.Equal(
            OkfTrustTier.HumanReviewed,
            OkfDocument.TrustTier(Frontmatter("""
                verified:
                  - { by: process:finance-nightly, at: x }
                  - { by: human:ahormati, at: y }
                """)));

        // A bare mapping is treated as a one-element list.
        Assert.Equal(
            OkfTrustTier.HumanReviewed,
            OkfDocument.TrustTier(Frontmatter("verified: { by: human:ahormati, at: z }")));
    }

    [Fact]
    public void TrustTierRendersTheSpecStrings()
    {
        Assert.Equal("unverified", OkfTrustTier.Unverified.ToSpecString());
        Assert.Equal("machine-confirmed", OkfTrustTier.MachineConfirmed.ToSpecString());
        Assert.Equal("human-reviewed", OkfTrustTier.HumanReviewed.ToSpecString());
    }

    [Theory]
    [InlineData("verified: {}", OkfTrustTier.MachineConfirmed)]
    [InlineData("verified: { at: 2026-06-25T09:00:00Z }", OkfTrustTier.MachineConfirmed)]
    [InlineData("verified: { by: }", OkfTrustTier.MachineConfirmed)]
    [InlineData("verified: { by: Human:ahormati }", OkfTrustTier.MachineConfirmed)]
    [InlineData("verified: { by: humans:ahormati }", OkfTrustTier.MachineConfirmed)]
    [InlineData("verified: { by: \"human:\" }", OkfTrustTier.HumanReviewed)]
    [InlineData("verified: [a string]", OkfTrustTier.Unverified)]
    [InlineData("verified: []", OkfTrustTier.Unverified)]
    [InlineData("verified:", OkfTrustTier.Unverified)]
    public void TrustTierEdgeCases(string frontmatter, OkfTrustTier expected) =>
        Assert.Equal(expected, OkfDocument.TrustTier(Frontmatter(frontmatter)));

    [Fact]
    public void TrustTierReadsOnlyVerified()
    {
        // PRD CORE-6: `generated`, `status`, and `sources` never affect the tier.
        var frontmatter = Frontmatter("""
            generated: { by: human:ahormati, at: 2026-06-20T22:53:05Z }
            status: draft
            sources:
              - id: s1
            """);

        Assert.Equal(OkfTrustTier.Unverified, OkfDocument.TrustTier(frontmatter));
    }

    [Fact]
    public void Ported_IsStale()
    {
        var reference = new DateOnly(2026, 9, 23);

        Assert.True(OkfDocument.IsStale(Frontmatter("stale_after: \"2026-09-23\""), reference));
        Assert.False(OkfDocument.IsStale(Frontmatter("stale_after: \"2026-09-24\""), reference));
        Assert.False(OkfDocument.IsStale(new OkfMapping(), reference));
        Assert.False(OkfDocument.IsStale(Frontmatter("stale_after: not-a-date"), reference));
    }

    // Rows marked "deviation" behave differently in the reference implementation;
    // see the IsStale*DeviationFromReference tests below for the reasoning.
    [Theory]
    [InlineData("stale_after: 2026-09-22", true)]
    [InlineData("stale_after: 2026-09-23", true)]
    [InlineData("stale_after: 2026-09-24", false)]
    [InlineData("stale_after: '2026-09-23'", true)]
    [InlineData("stale_after: 2026-09-23T10:00:00Z", true)] // deviation
    [InlineData("stale_after: 2026-09-24T00:00:00Z", false)] // deviation
    [InlineData("stale_after:", false)]
    [InlineData("stale_after: ''", false)]
    [InlineData("stale_after: 0", false)]
    [InlineData("stale_after: false", false)]
    [InlineData("stale_after: 2026-9-3", false)]
    [InlineData("stale_after: 2026-13-45", false)]
    [InlineData("stale_after: 20260923", false)] // deviation
    [InlineData("stale_after: [2026-09-23]", false)]
    [InlineData("stale_after: { on: 2026-09-23 }", false)]
    public void IsStaleEdgeCases(string frontmatter, bool expected) =>
        Assert.Equal(expected, OkfDocument.IsStale(Frontmatter(frontmatter), new DateOnly(2026, 9, 23)));

    [Fact]
    public void IsStaleComparesTheDatePartOfADateTime_DeviationFromReference()
    {
        // DELIBERATE DEVIATION. PyYAML resolves `2026-09-23T10:00:00Z` to a
        // datetime, which the reference implementation then compares against a date
        // and raises `TypeError: '>=' not supported between instances of
        // 'datetime.date' and 'datetime.datetime'`. PRD CORE-7 requires a datetime
        // to be compared on its date part instead, and §5.5 says an unusable value
        // is never an error, so okf-net compares the leading date and never throws.
        var reference = new DateOnly(2026, 9, 23);

        Assert.True(OkfDocument.IsStale(Frontmatter("stale_after: 2026-09-23T10:00:00Z"), reference));
        Assert.False(OkfDocument.IsStale(Frontmatter("stale_after: 2026-09-24T00:00:00Z"), reference));
    }

    [Fact]
    public void IsStaleAcceptsOnlyExtendedIsoDates_DeviationFromReference()
    {
        // DELIBERATE DEVIATION. The reference calls `date.fromisoformat(str(raw)[:10])`,
        // and on Python 3.11+ that also accepts basic-format (`20260923`) and ISO
        // week (`2026-W38-3`) dates — an artifact of the interpreter version, not of
        // the format. Spec §5.5 defines `stale_after` as an absolute `YYYY-MM-DD`
        // date, so okf-net accepts exactly that (optionally followed by a time) and
        // treats everything else as unparseable, i.e. not stale.
        var reference = new DateOnly(2026, 9, 23);

        Assert.False(OkfDocument.IsStale(Frontmatter("stale_after: 20260923"), reference));
        Assert.False(OkfDocument.IsStale(Frontmatter("stale_after: '2026-W38-3'"), reference));
    }

    [Fact]
    public void ParseKeepsAnOutOfRangeDateAsText_DeviationFromReference()
    {
        // DELIBERATE DEVIATION. PyYAML's timestamp constructor raises a bare
        // ValueError for `2026-13-45`, which the reference implementation does not
        // catch (it only handles yaml.YAMLError), so parsing the whole document
        // fails. okf-net never resolves scalars into typed values, so the document
        // parses and only the staleness check declines to use the value.
        var doc = OkfDocument.Parse("---\ntype: X\nstale_after: 2026-13-45\n---\n\nBody.\n");

        Assert.Equal("2026-13-45", OkfValues.Text(doc.Frontmatter, "stale_after"));
        Assert.False(OkfDocument.IsStale(doc.Frontmatter, new DateOnly(2026, 9, 23)));
    }

    [Fact]
    public void IsStaleTakesItsTodayFromTheInjectedClock()
    {
        var frontmatter = Frontmatter("stale_after: 2026-09-23");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero));

        Assert.True(OkfDocument.IsStale(frontmatter, clock));
        Assert.False(OkfDocument.IsStale(
            frontmatter,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 22, 23, 59, 59, TimeSpan.Zero))));
    }

    private static OkfMapping Frontmatter(string yaml) =>
        OkfDocument.Parse($"---\n{yaml}\n---\n\nBody.\n").Frontmatter;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
