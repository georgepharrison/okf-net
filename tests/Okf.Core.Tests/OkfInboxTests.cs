namespace Okf.Core.Tests;

/// <summary>
/// Acknowledgment, staleness, and source-drift classification (PRD CORE-15, CLI-12).
/// Every expectation here is derived from decisions.md §7 and the spec's §5.2/§5.5 text,
/// not from what the code happens to do.
/// </summary>
public class OkfInboxTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    [Fact]
    public void AConceptWithNoLifecycleFrontmatterIsNotOnTheInbox()
    {
        // No `generated`, no `verified`, no `stale_after`: hand-written prose nobody dated.
        // There is no stamp to be newer than a verification, so there is nothing true to say.
        Assert.Null(Classify("type: Concept\ntitle: Plain"));
    }

    [Fact]
    public void AgentGeneratedAndNeverVerifiedIsUnacknowledged()
    {
        var item = Classify("type: Concept\ngenerated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }");

        Assert.NotNull(item);
        Assert.Equal([OkfInboxReason.Unacknowledged], item.Reasons);
        Assert.Equal("claude-fable/5", item.GeneratedBy);
        Assert.Null(item.VerifiedAt);
        Assert.Equal(1, item.AgeDays);
    }

    [Fact]
    public void HumanGeneratedAndNeverVerifiedIsNotUnacknowledged()
    {
        // A person wrote it. There is nobody waiting on it — the third arm of CORE-15 is
        // "no verified at all after a generated stamp by a NON-human actor".
        Assert.Null(Classify("type: Concept\ngenerated: { by: \"human:ringo\", at: 2026-08-14T20:41:50-05:00 }"));
    }

    [Fact]
    public void AGeneratedStampWithNoActorCountsAsNonHuman()
    {
        // What a concept does not record, it cannot claim.
        var item = Classify("type: Concept\ngenerated: { at: 2026-08-14 }");

        Assert.NotNull(item);
        Assert.True(item.Has(OkfInboxReason.Unacknowledged));
        Assert.Null(item.GeneratedBy);
    }

    [Fact]
    public void StatusDraftIsUnacknowledgedEvenWhenVerifiedLater()
    {
        var item = Classify("""
            type: Concept
            status: draft
            generated: { by: claude-fable/5, at: 2026-08-01T00:00:00Z }
            verified:
              - { by: "human:ringo", at: 2026-08-10T00:00:00Z }
            """);

        Assert.NotNull(item);
        Assert.True(item.Has(OkfInboxReason.Unacknowledged));
        Assert.Equal("draft", item.Status);
        Assert.Equal(OkfTrustTier.HumanReviewed, item.Concept.TrustTier);
    }

    [Fact]
    public void RegeneratedAfterVerificationIsUnacknowledged()
    {
        var item = Classify("""
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-14T12:00:00Z }
            verified:
              - { by: "human:ringo", at: 2026-08-10T00:00:00Z }
              - { by: "process:schema-check", at: 2026-08-12T00:00:00Z }
            """);

        Assert.NotNull(item);
        Assert.True(item.Has(OkfInboxReason.Unacknowledged));

        // The LATEST verification is the one compared against, not the first or the human one.
        Assert.Equal("process:schema-check", item.VerifiedBy);
        Assert.Equal("2026-08-12T00:00:00Z", item.VerifiedAt);
    }

    [Fact]
    public void VerifiedAfterGenerationIsAcknowledged()
    {
        Assert.Null(Classify("""
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-10T00:00:00Z }
            verified:
              - { by: "human:ringo", at: 2026-08-12T00:00:00Z }
            """));
    }

    [Fact]
    public void AVerificationStampedInTheSameSecondAcknowledgesTheWrite()
    {
        // The boundary. CORE-15 says generated.at NEWER than the latest verified.at;
        // equality is the verification *of* that write, not a stale one.
        Assert.Null(Classify("""
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-14T12:00:00Z }
            verified:
              - { by: "human:ringo", at: 2026-08-14T12:00:00Z }
            """));
    }

    [Fact]
    public void AVerificationOneSecondEarlierDoesNotAcknowledgeTheWrite()
    {
        var item = Classify("""
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-14T12:00:01Z }
            verified:
              - { by: "human:ringo", at: 2026-08-14T12:00:00Z }
            """);

        Assert.NotNull(item);
        Assert.True(item.Has(OkfInboxReason.Unacknowledged));
    }

    [Fact]
    public void ABareVerifiedMappingIsAOneElementList()
    {
        // §5.2: a single verifier MAY be written without the list dash.
        Assert.Null(Classify("""
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-10T00:00:00Z }
            verified: { by: "human:ringo", at: 2026-08-12T00:00:00Z }
            """));
    }

    [Fact]
    public void AVerificationWithNoReadableTimestampStillCountsAsVerified()
    {
        // Someone stood behind this. There is no timestamp proving they stood behind THIS
        // version, and inventing one to report against would be worse than saying nothing.
        Assert.Null(Classify("""
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-10T00:00:00Z }
            verified:
              - { by: "human:ringo" }
            """));
    }

    [Fact]
    public void AMalformedVerificationTimestampNeverMasksAGoodOne()
    {
        var item = Classify("""
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-14T00:00:00Z }
            verified:
              - { by: "human:ringo", at: 2026-08-12T00:00:00Z }
              - { by: "process:broken", at: whenever }
            """);

        Assert.NotNull(item);
        Assert.Equal("human:ringo", item.VerifiedBy);
    }

    [Theory]
    [InlineData("2026-08-14", true, 1)]
    [InlineData("2026-08-15", true, 0)]
    [InlineData("2026-08-16", false, null)]
    public void StalenessIsTodayOnOrAfterStaleAfter(string staleAfter, bool stale, int? staleDays)
    {
        // §5.5: stale when today >= stale_after. The boundary is today == stale_after.
        var item = Classify($"type: Concept\nstale_after: {staleAfter}");

        Assert.Equal(stale, item is not null && item.Has(OkfInboxReason.Stale));
        Assert.Equal(staleDays, item?.StaleDays);
    }

    [Fact]
    public void ASourceModifiedAfterGenerationIsDrift()
    {
        var item = Classify("""
            type: Concept
            generated: { by: "human:ringo", at: 2026-08-10T00:00:00Z }
            sources:
              - id: spec
                resource: https://example.org/spec
                last_modified: 2026-08-12
              - id: quiet
                resource: https://example.org/quiet
                last_modified: 2026-08-01
            """);

        Assert.NotNull(item);
        Assert.Equal([OkfInboxReason.SourceDrift], item.Reasons);
        var drifted = Assert.Single(item.DriftedSources);
        Assert.Equal("spec", drifted.Id);
        Assert.Equal("https://example.org/spec", drifted.Resource);
        Assert.Equal("2026-08-12", drifted.LastModified);
        Assert.Equal("spec", drifted.Display);
    }

    [Fact]
    public void ASourceModifiedTheSameDayAsAnInstantGenerationIsNotDrift()
    {
        // A bare date coerced to midnight UTC would sort before an instant later that day
        // and report the drift backwards; compared by date, the two are the same day.
        Assert.Null(Classify("""
            type: Concept
            generated: { by: "human:ringo", at: 2026-08-15T01:41:50Z }
            sources:
              - id: spec
                resource: https://example.org/spec
                last_modified: 2026-08-15
            """));
    }

    [Fact]
    public void DriftNeedsAGeneratedTimestampToCompareAgainst()
    {
        // PRD CORE-8: concepts lacking `generated.at` produce no drift signal. This one is
        // on the inbox for being unacknowledged, and for nothing else.
        var item = Classify("""
            type: Concept
            generated: { by: claude-fable/5 }
            sources:
              - id: spec
                resource: https://example.org/spec
                last_modified: 2026-08-12
            """);

        Assert.NotNull(item);
        Assert.Equal([OkfInboxReason.Unacknowledged], item.Reasons);
        Assert.Empty(item.DriftedSources);
    }

    [Fact]
    public void ASourceWithNoIdIsNamedByItsResource()
    {
        var item = Classify("""
            type: Concept
            generated: { by: "human:ringo", at: 2026-08-10T00:00:00Z }
            sources:
              - resource: https://example.org/spec
                last_modified: 2026-08-12
            """);

        Assert.NotNull(item);
        var drifted = Assert.Single(item.DriftedSources);
        Assert.Null(drifted.Id);
        Assert.Equal("https://example.org/spec", drifted.Resource);
        Assert.Equal("https://example.org/spec", drifted.Display);
    }

    [Fact]
    public void ASourceWithNeitherIdNorResourceStillReportsItsDrift()
    {
        var item = Classify("""
            type: Concept
            generated: { by: "human:ringo", at: 2026-08-10T00:00:00Z }
            sources:
              - title: Anonymous
                last_modified: 2026-08-12
            """);

        Assert.NotNull(item);
        Assert.Equal("source", Assert.Single(item.DriftedSources).Display);
    }

    [Fact]
    public void StaleDaysIsAbsentOnAConceptWhoseStaleAfterHasNotArrived()
    {
        // The concept is on the inbox for being unacknowledged, and `staleDays` is a
        // measurement of an expiry that has not happened.
        var item = Classify("""
            type: Concept
            stale_after: 2027-01-01
            generated: { by: claude-fable/5, at: 2026-08-14T00:00:00Z }
            """);

        Assert.NotNull(item);
        Assert.Equal([OkfInboxReason.Unacknowledged], item.Reasons);
        Assert.Equal("2027-01-01", item.StaleAfter);
        Assert.Null(item.StaleDays);
    }

    [Fact]
    public void AConceptCanCarryEveryReasonAtOnce()
    {
        var item = Classify("""
            type: Concept
            status: draft
            stale_after: 2026-08-01
            generated: { by: claude-fable/5, at: 2026-07-01T00:00:00Z }
            sources:
              - id: spec
                resource: https://example.org/spec
                last_modified: 2026-08-12
            """);

        Assert.NotNull(item);

        // Reason order is fixed, so a report grouped by reason is stable across runs.
        Assert.Equal(
            [OkfInboxReason.Unacknowledged, OkfInboxReason.Stale, OkfInboxReason.SourceDrift],
            item.Reasons);
    }

    [Fact]
    public void AScanOrdersItemsByBundleThenPathAndCountsWhatItRead()
    {
        using var bundle = new TempBundle();
        bundle.Add("zebra.md", Document("type: Concept\ngenerated: { by: claude-fable/5, at: 2026-08-01 }"))
            .Add("alpha.md", Document("type: Concept\ngenerated: { by: claude-fable/5, at: 2026-08-01 }"))
            .Add("settled.md", Document("type: Concept\ntitle: Settled"))
            .Add("index.md", Document("type: Index"))
            .Add("log.md", "# Directory Update Log\n")
            .Add("broken.md", "---\nkey: [unterminated\n---\n\nbody\n");

        var result = OkfInboxScanner.Scan([bundle.Bundle], new OkfInboxOptions { Today = Today });

        Assert.Equal(["alpha.md", "zebra.md"], result.Items.Select(item => item.Concept.Path));

        // index.md and log.md are reserved (§3.1) and are not concepts; the unparseable
        // file is an OKF0001 error for `okf lint`, counted here so a broken vault does not
        // read as a clean one.
        Assert.Equal(3, result.ConceptCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(2, result.Count(OkfInboxReason.Unacknowledged));
        Assert.Equal(0, result.Count(OkfInboxReason.Stale));
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void AVaultWhereNothingIsWaitingScansEmpty()
    {
        using var bundle = new TempBundle();
        bundle.Add("settled.md", Document("type: Concept\ngenerated: { by: \"human:ringo\", at: 2026-08-01 }"));

        var result = OkfInboxScanner.Scan([bundle.Bundle], new OkfInboxOptions { Today = Today });

        Assert.True(result.IsEmpty);
        Assert.Equal(1, result.ConceptCount);
        Assert.Equal(0, result.SkippedCount);
    }

    private static OkfInboxItem? Classify(string frontmatter)
    {
        var document = OkfDocument.Parse(Document(frontmatter));
        var bundle = new OkfBundle(Path.Combine(Path.GetTempPath(), "okf-inbox-tests"));
        var concept = new OkfConcept(bundle, Path.Combine(bundle.Root, "concept.md"), document, Today);
        return OkfInboxScanner.Classify(concept, Today);
    }

    private static string Document(string frontmatter) => $"---\n{frontmatter}\n---\n\nBody.\n";
}
