using System.Text.Json;

namespace Okf.Cli.Tests.Candidates;

/// <summary>
/// End-to-end <c>okf candidates</c> behaviour (work item #75): the flags, the exit-code
/// contract, the inventory it prints, and the quarantine notices that go somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// The rows are asserted as whole lines, not substrings, following <c>InboxCommandTests</c>: a
/// report a person greps and a CI job parses deserves tests that notice a column moving. The
/// expected strings are written out from #71's spec and #74's eligibility table rather than read
/// off the first run of the command.
/// </para>
/// <para>
/// The distinction the whole surface rests on is restated here as a test each: candidates on
/// stdout with exit 0 (an inventory, not a gate), quarantined concepts on stderr with exit 1 (an
/// enumeration that admits its gap), and usage or environment failures at exit 2.
/// </para>
/// </remarks>
public class CandidatesCommandTests
{
    [Fact]
    public void HelpExitsZeroAndDescribesTheEligibilityPredicate()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "candidates", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf candidates [path] [options]", run.Output, StringComparison.Ordinal);
        Assert.Contains("--format <text|json>", run.Output, StringComparison.Ordinal);

        // The predicate, the scope rules, and the exit codes are the three things help owes.
        Assert.Contains("provably absent", run.Output, StringComparison.Ordinal);
        Assert.Contains("personal vault", run.Output, StringComparison.Ordinal);
        Assert.Contains("2  usage or environment failure", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsReachableFromTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf candidates [path]", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--nope")]
    [InlineData("--format=yaml")]
    [InlineData("--format")]
    public void AMalformedCommandLineIsAUsageFailure(string argument)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "candidates", argument);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void ASecondBareArgumentIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "candidates", "one", "two");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("at most one path", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnresolvableVaultIsAnEnvironmentFailure()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "candidates");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AConceptWithNoHistoryIsListedAndTheRunStillExitsZero()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(
            [
                "  vault/bundles/b/quiet.md  Quiet  [unverified] (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "Scanned 1 concept in 1 bundle: 1 candidate, 0 quarantined.",
            ],
            run.OutputLines);
    }

    /// <summary>
    /// The three spellings of "provably absent" that are not an absent key each get the detail
    /// that names what the file actually said, because the row's job is to tell a reviewer where
    /// to look.
    /// </summary>
    /// <param name="frontmatter">The concept's frontmatter.</param>
    /// <param name="detail">The detail line the report must carry.</param>
    [Theory]
    [InlineData("type: Concept\ntitle: Empty\nverified:", "`verified` is null — nobody has ever stood behind this content")]
    [InlineData("type: Concept\ntitle: Empty\nverified: null", "`verified` is null — nobody has ever stood behind this content")]
    [InlineData("type: Concept\ntitle: Empty\nverified: []", "`verified: []` — an explicit empty list, which says nobody has yet")]
    public void AnAbsentHistoryNamesTheShapeItFound(string frontmatter, string detail)
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept(frontmatter));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(detail, run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A concept with any readable history is not a candidate — not a human one, and not a
    /// machine one. The <c>at</c> is garbage in the human case on purpose: someone wrote
    /// themselves down, which is the whole test (#71 story 15).
    /// </summary>
    /// <param name="verified">The <c>verified</c> line as written.</param>
    [Theory]
    [InlineData("verified: { by: \"human:ringo\" }")]
    [InlineData("verified: { by: \"human:ahormati\", at: yesterday }")]
    [InlineData("verified: { by: \"process:check\", at: 2026-06-25T09:00:00Z }")]
    [InlineData("verified:\n  - { by: openai-codex/gpt-5, at: 2026-06-25T09:00:00Z }")]
    public void AConceptWithReadableHistoryIsNotListed(string verified)
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/signed.md", Concept($"type: Concept\ntitle: Signed\n{verified}"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal("Scanned 1 concept in 1 bundle: 0 candidates, 0 quarantined. Nothing awaits review.", LastLine(run));
    }

    /// <summary>
    /// A concept whose <c>verified</c> block cannot be read is named on stderr, is not listed on
    /// stdout, and makes the run exit 1: an inventory that quietly omits it would be worse than
    /// one that admits the gap.
    /// </summary>
    [Fact]
    public void AnUnreadableVerificationStructureIsQuarantinedOnStderrAndExitsOne()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/lying.md", Concept("type: Concept\ntitle: Lying\nverified: ahormati"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Equal(["Scanned 1 concept in 1 bundle: 0 candidates, 1 quarantined."], run.OutputLines);
        // Named by the same path form the rows use, with the reason's wire spelling, the tier the
        // file appears to earn, and the detail a person can act on.
        Assert.Equal(
            [
                "okf: could not classify vault/bundles/b/lying.md [verification-structure] [unverified]: "
                    + "'verified: ahormati' is the scalar 'ahormati', which cannot be read as verification events",
            ],
            [.. run.ErrorLines.Select(line => line.Replace(tree.Root + "/", string.Empty, StringComparison.Ordinal))]);
    }

    /// <summary>
    /// A stub the author never finished — an empty mapping naming nobody — quarantines rather
    /// than counting as verified, which is the shape the whole feature exists for (#71 story 16).
    /// </summary>
    [Fact]
    public void AnEmptyVerifiedMappingIsQuarantinedRatherThanBlessed()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/stub.md", Concept("type: Concept\ntitle: Stub\nverified: {}"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("verification-structure", run.Error, StringComparison.Ordinal);
        Assert.Contains("an empty mapping", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("stub.md", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Candidates are one row each, in the walk's order, each with the detail line beneath it;
    /// the summary counts concepts, bundles, candidates and quarantines so an empty inventory is
    /// distinguishable from a failed run.
    /// </summary>
    [Fact]
    public void EveryCandidateGetsARowAndTheSummaryCountsFourThings()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/zebra.md", Concept("type: Concept\ntitle: Zebra"));
        tree.Write("vault/bundles/b/alpha.md", Concept("type: Concept\ntitle: Alpha\ntags: [okf-net, trust]"));
        tree.Write("vault/bundles/b/topic/mid.md", Concept("type: Reference\ntitle: Mid\ndescription: A middle concept."));
        tree.Write(
            "vault/bundles/b/signed.md",
            Concept("type: Concept\ntitle: Signed\nverified: { by: \"human:ringo\", at: 2026-06-25T09:00:00Z }"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(
            [
                "  vault/bundles/b/alpha.md  Alpha  [unverified] (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "  vault/bundles/b/topic/mid.md  Mid  [unverified] (Reference)",
                "    no `verified` key — nobody has ever stood behind this content",
                "  vault/bundles/b/zebra.md  Zebra  [unverified] (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "Scanned 4 concepts in 1 bundle: 3 candidates, 0 quarantined.",
            ],
            run.OutputLines);
    }

    /// <summary>
    /// The row carries the flags a verifier uses to order its work: the tier as derived, the
    /// stale marker, and the draft marker. None of them is an eligibility test — a draft and a
    /// stale concept are both candidates — they are reported so the caller can choose a policy.
    /// </summary>
    [Fact]
    public void ARowMarksTrustStalenessAndDraftWithoutFilteringOnThem()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\ntitle: Drafted\nstatus: draft"));
        tree.Write("vault/bundles/b/expired.md", Concept("type: Concept\ntitle: Expired\nstale_after: 2020-01-01"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        // Whole lines: the flag group carries the marker, and the type group follows it.
        Assert.Equal(
            [
                "  vault/bundles/b/drafted.md  Drafted  [unverified] (draft) (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "  vault/bundles/b/expired.md  Expired  [unverified] (stale) (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "Scanned 2 concepts in 1 bundle: 2 candidates, 0 quarantined.",
            ],
            [.. run.OutputLines.Select(line => line.Replace(tree.Root + "/", string.Empty, StringComparison.Ordinal))]);
    }

    /// <summary>
    /// A quarantined concept's row on stderr carries the tier the file appears to earn, because
    /// the disagreement is the finding: <c>verified: {}</c> reads <c>machine-confirmed</c> to
    /// §5.3 while the scanner refuses to classify it.
    /// </summary>
    [Fact]
    public void AQuarantineNoticeReportsTheTierThatDisagreesWithIt()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/stub.md", Concept("type: Concept\ntitle: Stub\nverified: {}"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Contains("[machine-confirmed]", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("machine-confirmed", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyInventorySaysSoAndStillExitsZero()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/signed.md", Concept("type: Concept\ntitle: Signed\nverified: { by: \"human:ringo\" }"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal("Scanned 1 concept in 1 bundle: 0 candidates, 0 quarantined. Nothing awaits review.", LastLine(run));
    }

    [Fact]
    public void FortyCandidatesAreASuccessNotAFailure()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        for (var index = 0; index < 40; index++)
        {
            tree.Write($"vault/bundles/b/concept-{index:D2}.md", Concept($"type: Concept\ntitle: Concept {index}"));
        }

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        // An inventory, not a gate on volume: the exit code says nothing about the length.
        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(81, run.OutputLines.Length);
        Assert.Equal("Scanned 40 concepts in 1 bundle: 40 candidates, 0 quarantined.", LastLine(run));
    }

    [Fact]
    public void ReservedFilesAreNotConceptsAndNeverAppear()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/index.md", "# Index\n\nGenerated.\n");
        tree.Write("vault/bundles/b/log.md", "# Log\n\n- 2026-08-15: hello\n");
        tree.Write("vault/bundles/b/about.md", Concept("type: Concept\ntitle: About"));
        tree.Write("vault/bundles/b/real.md", Concept("type: Concept\ntitle: Real"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(
            [
                "  vault/bundles/b/about.md  About  [unverified] (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "  vault/bundles/b/real.md  Real  [unverified] (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "Scanned 2 concepts in 1 bundle: 2 candidates, 0 quarantined.",
            ],
            run.OutputLines);
    }

    [Fact]
    public void TheJsonIsAnArrayOfTheCandidatesAndNothingElse()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/topic/quiet.md", Concept("""
            type: Reference
            title: Quiet
            description: A concept nobody has read.
            tags: [okf-net, trust]
            status: draft
            stale_after: 2020-01-01
            """));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--format", "json");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        var row = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal("topic/quiet", row.GetProperty("id").GetString());
        Assert.Equal("topic/quiet.md", row.GetProperty("path").GetString());
        Assert.Equal(Path.Combine(bundle, "topic", "quiet.md"), row.GetProperty("absolutePath").GetString());
        Assert.EndsWith("topic/quiet.md", row.GetProperty("displayPath").GetString()!, StringComparison.Ordinal);
        Assert.Equal(bundle, row.GetProperty("bundle").GetString());
        Assert.Equal("b", row.GetProperty("bundleName").GetString());
        Assert.Equal("Quiet", row.GetProperty("title").GetString());
        Assert.Equal("Reference", row.GetProperty("type").GetString());
        Assert.Equal("A concept nobody has read.", row.GetProperty("description").GetString());
        Assert.Equal(["okf-net", "trust"], row.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal("unverified", row.GetProperty("trustTier").GetString());
        Assert.True(row.GetProperty("stale").GetBoolean());
        Assert.Equal("2020-01-01", row.GetProperty("staleAfter").GetString());
        Assert.Equal("draft", row.GetProperty("status").GetString());

        // Indented, like every other JSON surface okf writes.
        Assert.Contains("\n  {", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>status</c> and <c>generated</c> are reported as written, as policy input for the
    /// caller rather than as the enumerator's opinion (#71 stories 8 and 9): a verifier may skip
    /// drafts or prefer recent material, and it cannot decide that without the values.
    /// </summary>
    [Fact]
    public void TheRecordCarriesStatusAndGeneratedAsWritten()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/fresh.md", Concept("""
            type: Concept
            title: Fresh
            status: draft
            generated: { by: claude-fable/5, at: 2020-01-01T00:00:00Z }
            """));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        var row = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal("draft", row.GetProperty("status").GetString());
        Assert.Equal("claude-fable/5", row.GetProperty("generatedBy").GetString());
        Assert.Equal("2020-01-01T00:00:00Z", row.GetProperty("generatedAt").GetString());
    }

    /// <summary>
    /// Absent frontmatter becomes JSON null rather than a missing key: a consumer reading
    /// <c>record.description</c> must get null, not undefined (#71 story 5's contract).
    /// </summary>
    [Fact]
    public void AbsentFieldsBecomeJsonNullRatherThanMissingKeys()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/bare.md", Concept("title: Bare"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        var row = Assert.Single(document.RootElement.EnumerateArray());
        foreach (var name in (string[])["type", "description", "status", "generatedBy", "generatedAt"])
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty(name).ValueKind);
        }

        Assert.Empty(row.GetProperty("tags").EnumerateArray());
    }

    [Fact]
    public void AnEmptyInventoryIsAnEmptyJsonArray()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/signed.md", Concept("type: Concept\nverified: { by: \"human:ringo\" }"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    /// <summary>
    /// The JSON contract is exactly the candidates: a quarantined concept is not a record in the
    /// array, and its notice still reaches stderr — so a machine consumer iterating stdout never
    /// has to guess whether the array is the whole truth, because the exit code tells it.
    /// </summary>
    [Fact]
    public void TheJsonCarriesNoQuarantineNoticesAndTheRunStillExitsOne()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));
        tree.Write("vault/bundles/b/lying.md", Concept("type: Concept\ntitle: Lying\nverified: 42"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(["quiet"], document.RootElement.EnumerateArray().Select(row => row.GetProperty("id").GetString()));
        Assert.Contains("verification-structure", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("verification-structure", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void QuarantineNoticesReachStderrInTextToo()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/lying.md", Concept("type: Concept\ntitle: Lying\nverified: 42"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--format", "text");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.DoesNotContain("verification-structure", run.Output, StringComparison.Ordinal);
        Assert.Contains("verification-structure", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void VerboseReportsResolutionAndCountsOnStderrAndLeavesTheContractAlone()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));

        var quiet = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");
        var loud = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json", "--verbose");

        Assert.Equal(quiet.Output, loud.Output);
        Assert.Empty(quiet.Error);
        Assert.Contains("okf: resolved bundle", loud.Error, StringComparison.Ordinal);
        Assert.Contains("okf: bundle", loud.Error, StringComparison.Ordinal);
        Assert.Contains("okf: scanned 1 concept, 1 candidate, 0 quarantined", loud.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidatesAreOrderedDeterministicallyByPath()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        foreach (var name in (string[])["zebra.md", "alpha.md", "topic/mid.md"])
        {
            tree.Write($"vault/bundles/b/{name}", Concept("type: Concept"));
        }

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(
            ["alpha", "topic/mid", "zebra"],
            document.RootElement.EnumerateArray().Select(row => row.GetProperty("id").GetString()));
    }

    [Fact]
    public void AVaultScansEveryBundleItHoldsAndCountsThem()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("vault", "bundles", "one"));
        tree.CopyFixture("warnings-only", Path.Combine("vault", "bundles", "two"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", Path.Combine(tree.Root, "vault"));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Matches(@"^Scanned \d+ concepts in 2 bundles: \d+ candidates, 0 quarantined", LastLine(run));
    }

    [Fact]
    public void ThePersonalVaultIsTheFallbackTarget()
    {
        using var tree = new TempTree();
        tree.Write(Path.Combine("personal", "bundles", "notes", "quiet.md"), Concept("type: Concept\ntitle: Quiet"));
        var workingDirectory = tree.CreateDirectory("empty");

        var run = CliHarness.Run(
            CliHarness.Environment(workingDirectory, tree.Root, okfHome: Path.Combine(tree.Root, "personal")),
            "candidates",
            "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("personal vault", run.Error, StringComparison.Ordinal);
        Assert.Contains("personal/bundles/notes/quiet.md  Quiet  [unverified] (Concept)", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE #76 REPRODUCTION, named. The ticket's own case: one healthy concept and one fenceless
    /// <c>README.md</c>. Before #76 this printed "2 candidates, 0 quarantined" at exit 0 and
    /// handed the stray file to an automated verifier as knowledge to review.
    /// </summary>
    [Fact]
    public void AFencelessReadmeAlongsideOneHealthyConceptIsOneCandidateAndOneQuarantine()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/healthy.md", Concept("type: Concept\ntitle: Healthy"));
        tree.Write("vault/bundles/b/README.md", "# README\n\nNot a concept.\n");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Equal(
            [
                "  vault/bundles/b/healthy.md  Healthy  [unverified] (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "Scanned 2 concepts in 1 bundle: 1 candidate, 1 quarantined.",
            ],
            run.OutputLines);
        Assert.Equal(
            [
                "okf: could not classify vault/bundles/b/README.md [frontmatter-unreadable]: "
                    + "this file is not readable as a concept: File has no YAML frontmatter block. "
                    + "Run `okf lint` on the bundle to fix it.",
            ],
            [.. run.ErrorLines.Select(line => line.Replace(tree.Root + "/", string.Empty, StringComparison.Ordinal))]);
    }

    /// <summary>
    /// The three unreadability shapes each get their own stable wire spelling on the notice,
    /// and none of them prints the verification-structure spelling — a stray <c>README.md</c>
    /// must not read as a malformed <c>verified</c> block to whoever is triaging it.
    /// </summary>
    /// <param name="content">The file's whole content.</param>
    /// <param name="wire">The reason's wire spelling the notice must carry.</param>
    /// <param name="fragment">What the notice must say about that shape.</param>
    [Theory]
    [InlineData("# README\n\nNo frontmatter.\n", "frontmatter-unreadable", "no YAML frontmatter block")]
    [InlineData("---\ntype: Concept\n\nno closing fence\n", "frontmatter-unreadable", "Unterminated")]
    [InlineData("---\nkey: [unterminated\n---\n\nBody.\n", "frontmatter-unreadable", "Invalid YAML")]
    public void EachUnreadableShapeIsNamedWithItsOwnReason(
        string content,
        string wire,
        string fragment)
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/odd.md", content);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains($"[{wire}]", run.Error, StringComparison.Ordinal);
        Assert.Contains(fragment, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("verification-structure", run.Error, StringComparison.Ordinal);
        Assert.Equal("Scanned 1 concept in 1 bundle: 0 candidates, 1 quarantined.", LastLine(run));
    }

    /// <summary>
    /// The notice says plainly that the file is not readable as a concept and points at
    /// <c>okf lint</c> for the fix — the two things a person needs, in the order they need them.
    /// </summary>
    [Fact]
    public void AnUnreadableNoticeSaysTheFileIsNotAConceptAndPointsAtLint()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/README.md", "# README\n\nNo frontmatter.\n");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Contains("not readable as a concept", run.Error, StringComparison.Ordinal);
        Assert.Contains("Run `okf lint`", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The JSON array stays exactly the candidates whatever the reason: an unreadable file is
    /// not a record, and a machine consumer reads exit 1 as "that array is not the whole truth".
    /// </summary>
    [Fact]
    public void AnUnreadableFileStaysOutOfTheJsonAndStillMovesTheExitCode()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));
        tree.Write("vault/bundles/b/README.md", "# README\n\nNo frontmatter.\n");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(["quiet"], document.RootElement.EnumerateArray().Select(row => row.GetProperty("id").GetString()));
        Assert.DoesNotContain("frontmatter-unreadable", run.Output, StringComparison.Ordinal);
        Assert.Contains("frontmatter-unreadable", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file whose frontmatter does not parse is now a quarantine like any other: counted in
    /// the summary, exit 1, and named on stderr. The parenthetical count #75 shipped as an
    /// intermediate disclosure is gone, because the quarantined count replaced it.
    /// </summary>
    [Fact]
    public void AFileWhoseFrontmatterDoesNotParseIsQuarantinedAndCountsAsOne()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/broken.md", "---\nkey: [unterminated\n---\n\nBody.\n");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Equal(
            [
                "  vault/bundles/b/quiet.md  Quiet  [unverified] (Concept)",
                "    no `verified` key — nobody has ever stood behind this content",
                "Scanned 2 concepts in 1 bundle: 1 candidate, 1 quarantined.",
            ],
            run.OutputLines);
        Assert.DoesNotContain("whose frontmatter does not parse", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--verbose</c> counts the quarantines that came from unreadable files in the same total
    /// the summary prints, so the two channels cannot disagree about how much was set aside.
    /// </summary>
    [Fact]
    public void VerboseCountsUnreadableQuarantinesInTheSameTotal()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/README.md", "# README\n\nNo frontmatter.\n");
        tree.Write("vault/bundles/b/lying.md", Concept("type: Concept\ntitle: Lying\nverified: 42"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--verbose");

        // One concept (the unreadable README never became one), one quarantine per reason, and
        // the same 2 on both channels.
        Assert.Contains("okf: scanned 2 concepts, 0 candidates, 2 quarantined", run.Error, StringComparison.Ordinal);
        Assert.Contains("0 candidates, 2 quarantined", LastLine(run));
    }

    [Theory]
    [InlineData("--format=json")]
    [InlineData("--json")]
    public void EverySpellingOfTheJsonFlagProducesTheSameArray(string flag)
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));

        var inline = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, flag);
        var spaced = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--format", "json");

        Assert.Equal(spaced.Output, inline.Output);
        Assert.StartsWith("[", inline.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTextIsTheDefaultAndIsNotJson()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));

        var explicitText = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--format", "text");
        var implicitText = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(implicitText.Output, explicitText.Output);
        Assert.StartsWith("  vault/bundles/b/quiet.md", explicitText.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The command writes nothing: the vault's bytes and their write times are the same after the
    /// scan as before it, which is the read-only promise (#71, and CLI-16's offline rule).
    /// </summary>
    [Fact]
    public void TheScanLeavesEveryFileItReadsUntouched()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));
        tree.Write("vault/bundles/b/lying.md", Concept("type: Concept\ntitle: Lying\nverified: 42"));
        var before = ReferenceBundles.Snapshot(bundle);

        CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(before, ReferenceBundles.Snapshot(bundle));
    }

    /// <summary>
    /// The record's fields and their order, in full. #77 fixes this list: a consumer that parses
    /// one of okf's three JSON writers parses all three, so the names and the order are the
    /// contract, and a test that pins them is what stops a field moving in one writer only.
    /// </summary>
    [Fact]
    public void TheRecordCarriesExactlyTheContractFieldsInOrder()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/topic/quiet.md", Concept("""
            type: Reference
            title: Quiet
            description: A concept nobody has read.
            tags: [okf-net, trust]
            status: draft
            stale_after: 2020-01-01
            generated: { by: claude-fable/5, at: 2019-01-01T00:00:00Z }
            """));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            [
                "id",
                "path",
                "displayPath",
                "absolutePath",
                "bundle",
                "bundleName",
                "title",
                "type",
                "description",
                "tags",
                "trustTier",
                "stale",
                "status",
                "generatedBy",
                "generatedAt",
                "staleAfter",
            ],
            row.EnumerateObject().Select(property => property.Name));

        Assert.Equal("2020-01-01", row.GetProperty("staleAfter").GetString());
        Assert.Equal("2019-01-01T00:00:00Z", row.GetProperty("generatedAt").GetString());
    }

    /// <summary>
    /// Nothing in a record names how the tool decided: no rule id, no verdict vocabulary, no
    /// quarantine language. The array is the same shape a future read-only tool will reuse, and a
    /// field that leaks the mechanism into it is a field every consumer then has to ignore.
    /// </summary>
    [Fact]
    public void TheRecordSaysWhereTheConceptIsAndNotHowTheToolDecided()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));
        tree.Write("vault/bundles/b/lying.md", Concept("type: Concept\ntitle: Lying\nverified: 42"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        var row = Assert.Single(document.RootElement.EnumerateArray());
        foreach (var name in (string[])
                 ["OKF0", "rule", "verdict", "quarantine", "severity", "candidate", "unreadable"])
        {
            Assert.DoesNotContain(name, run.Output, StringComparison.OrdinalIgnoreCase);
        }

        // And the shape is stable: every record carries each key once whatever the frontmatter
        // holds, which is what lets a consumer read the array without branching.
        Assert.Equal(
            row.EnumerateObject().Select(property => property.Name).Distinct().Count(),
            row.EnumerateObject().Count());
    }

    /// <summary>
    /// <c>staleAfter</c> is part of the shared lifecycle block, so it is null rather than absent
    /// when the concept names no date — the same rule as its neighbours (#77).
    /// </summary>
    [Fact]
    public void StaleAfterIsNullRatherThanAbsentWhenNoDateIsWritten()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/bare.md", Concept("title: Bare"));

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("staleAfter").ValueKind);
    }

    /// <summary>
    /// Standard output is strictly the array: one JSON document, one trailing newline, and nothing
    /// else — not the summary line the text report ends with, not a quarantine notice, not a
    /// verbose note. A pipe into another tool must never have to strip prose off the contract.
    /// </summary>
    [Fact]
    public void StandardOutputIsNothingButTheArrayWhileAQuarantineIsBeingReported()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", Concept("type: Concept\ntitle: Quiet"));
        tree.Write("vault/bundles/b/lying.md", Concept("type: Concept\ntitle: Lying\nverified: 42"));

        var quiet = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");
        var loud = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json", "--verbose");

        foreach (var run in (CliRun[])[quiet, loud])
        {
            Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
            Assert.StartsWith("[", run.Output, StringComparison.Ordinal);
            Assert.EndsWith("]" + Environment.NewLine, run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Scanned", run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("quarantined", run.Output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not classify", run.Output, StringComparison.Ordinal);

            // The whole stdout parses as one array, so there is no prose hiding after it.
            using var document = JsonDocument.Parse(run.Output);
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        }

        // The notices and the counts are on stderr, where they belong; the verbose counts are
        // the same ones the text report's summary line carries.
        Assert.Contains("could not classify", loud.Error, StringComparison.Ordinal);
        Assert.Contains("okf: scanned", loud.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("okf: scanned", quiet.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The inventory's order is the text report's order, byte-stably: two runs over the same bytes
    /// print the same array, so one run's output diffs against the next and what shows up is what
    /// was newly verified (#71 story 35).
    /// </summary>
    [Fact]
    public void TheArrayIsByteStableAcrossRunsAndMatchesTheTextOrder()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        foreach (var name in new[] { "zebra.md", "alpha.md", "topic/mid.md", "About.md" })
        {
            tree.Write($"vault/bundles/b/{name}", Concept("type: Concept"));
        }

        var first = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");
        var second = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");
        var text = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Equal(first.Output, second.Output);

        using var document = JsonDocument.Parse(first.Output);
        string[] expected = ["vault/bundles/b/About.md", "vault/bundles/b/alpha.md", "vault/bundles/b/topic/mid.md", "vault/bundles/b/zebra.md"];
        Assert.Equal(
            expected,
            document.RootElement.EnumerateArray().Select(row => row.GetProperty("displayPath").GetString()));

        // The text report lists the same concepts in the same order: one ordering, two renderings.
        string[] rows = [
            .. text.OutputLines
                .Where(line => line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("    ", StringComparison.Ordinal))
                .Select(line => line.TrimStart().Split("  ")[0]),
        ];
        Assert.Equal(expected, rows);
    }

    /// <summary>
    /// The summary line. The shared harness recognises only <c>Checked</c>, <c>Generated</c> and
    /// <c>Found</c>, and this verb deliberately does not reuse those prefixes, so its tests name
    /// the last line themselves.
    /// </summary>
    private static string LastLine(CliRun run) => run.OutputLines[^1];

    private static string Concept(string frontmatter) => $"---\n{frontmatter}\n---\n\nBody.\n";
}
