namespace Okf.Core.Tests.Trust;

/// <summary>
/// The candidate scanner: who is on the list, who is quarantined, in what order, and what the
/// result says about its own completeness (work item #74). Expectations come from work item
/// #71's spec and #74's eligibility table, not from the implementation.
/// </summary>
/// <remarks>
/// The scanner's contract is two lists that are not opposites. A concept is a candidate only
/// when its history is provably absent; a concept whose history cannot be established is
/// quarantined and is NOT a candidate. The third group — a file whose frontmatter cannot be
/// read at all — is quarantined too (#76), under its own reason, because a file nobody could
/// read has not been shown to lack history either.
/// </remarks>
public class OkfCandidateScannerTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    /// <summary>
    /// The eligibility table applied to whole concepts: the shapes that are Absent come back
    /// as candidates, the shapes that are Present do not, and the shapes that are Unreadable
    /// come back quarantined. Row for row this is the same table as
    /// <see cref="OkfVerificationHistoryTests" />, reached through the scanner instead of the
    /// verdict, because it is the scanner a caller uses and the table is the specification.
    /// </summary>
    /// <param name="verified">The <c>verified</c> line as written, or null for no key.</param>
    /// <param name="outcome">Which of the three lists the concept belongs on.</param>
    [Theory]
    [InlineData(null, Outcome.Candidate)]
    [InlineData("verified:", Outcome.Candidate)]
    [InlineData("verified: null", Outcome.Candidate)]
    [InlineData("verified: ~", Outcome.Candidate)]
    [InlineData("verified: []", Outcome.Candidate)]
    [InlineData("verified: { by: human:ahormati }", Outcome.Neither)]
    [InlineData("verified: { by: human:ahormati, at: yesterday }", Outcome.Neither)]
    [InlineData("verified: { by: \"human:\" }", Outcome.Neither)]
    [InlineData("verified: {}", Outcome.Quarantined)]
    [InlineData("verified: [{}, {}]", Outcome.Quarantined)]
    [InlineData("verified: { at: 2026-06-25T09:00:00Z }", Outcome.Quarantined)]
    [InlineData("verified: { by: }", Outcome.Quarantined)]
    [InlineData("verified: { by: \"\" }", Outcome.Quarantined)]
    [InlineData("verified:\n  - by: [a, b]", Outcome.Quarantined)]
    // The two rows the scanner table was missing, added so both theories agree on every shape:
    // an author that is a mapping, and a boolean scalar value.
    [InlineData("verified:\n  - by: { name: ringo }", Outcome.Quarantined)]
    [InlineData("verified: true", Outcome.Quarantined)]
    [InlineData("verified: ahormati", Outcome.Quarantined)]
    [InlineData("verified: 42", Outcome.Quarantined)]
    [InlineData("verified:\n  - { by: human:ahormati }\n  - a string", Outcome.Quarantined)]
    // A sequence mixing a legal event with an empty mapping quarantines too: every item must be
    // a recognizable event.
    [InlineData("verified:\n  - { by: human:ahormati }\n  - {}", Outcome.Quarantined)]
    internal void EachTableShapeLandsOnTheListTheSpecPutsItOn(string? verified, Outcome outcome)
    {
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Document($"type: Concept\n{Line(verified)}"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(outcome == Outcome.Candidate, result.Candidates.Any(candidate => candidate.Path == "concept.md"));
        Assert.Equal(outcome == Outcome.Quarantined, result.Quarantined.Any(item => item.Path == "concept.md"));
    }

    internal enum Outcome
    {
        Candidate,
        Quarantined,
        Neither,
    }

    /// <summary>
    /// A quarantined concept is named with the reason and with something a person can act on:
    /// the reason's wire spelling is the machine half, and the detail carries what the block
    /// actually said, so the fixer is not hunting through the file for it.
    /// </summary>
    [Fact]
    public void AQuarantinedConceptIsNamedWithItsReasonAndWhatItSaid()
    {
        using var bundle = new TempBundle();
        bundle.Add("quiet.md", Document("type: Concept\nverified: ahormati"));

        OkfQuarantinedConcept quarantined = Assert.Single(Scan(bundle).Quarantined);

        Assert.Equal("quiet.md", quarantined.Path);
        Assert.Equal(OkfQuarantineReason.VerificationStructure, quarantined.Reason);
        Assert.Equal("verification-structure", quarantined.Reason.ToWireString());
        Assert.Contains("ahormati", quarantined.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A candidate carries everything needed to open the exact file without reconstructing a
    /// path from parts (#71 story 5): bundle, bundle-relative path, concept id, absolute path.
    /// </summary>
    [Fact]
    public void ACandidateCarriesItsBundlePathsAndId()
    {
        using var bundle = new TempBundle("vault");
        bundle.Add("topics/notes.md", Document("type: Concept"));

        OkfCandidate candidate = Assert.Single(Scan(bundle).Candidates);

        Assert.Equal("vault", candidate.Bundle.Name);
        Assert.Equal("topics/notes.md", candidate.Path);
        Assert.Equal("topics/notes", candidate.Id);
        Assert.Equal(
            bundle.Root + Path.DirectorySeparatorChar + "topics/notes.md".Replace('/', Path.DirectorySeparatorChar),
            candidate.AbsolutePath);
    }

    /// <summary>
    /// The record carries <c>stale_after</c> as written alongside the <c>stale</c> flag, so a
    /// consumer can rank review priority without re-parsing frontmatter (#77's field list). The
    /// value is reported as written, not canonicalized: the file's own text is the answer.
    /// </summary>
    [Fact]
    public void ACandidateCarriesStaleAfterAsWritten()
    {
        using var bundle = new TempBundle();
        bundle.Add("fresh.md", Document("type: Concept\nstale_after: 2020-01-01"))
            .Add("bare.md", Document("type: Concept"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(
            "2020-01-01",
            result.Candidates.Single(candidate => candidate.Path == "fresh.md").StaleAfter);
        Assert.Null(result.Candidates.Single(candidate => candidate.Path == "bare.md").StaleAfter);
    }

    /// <summary>
    /// Candidates are ordered by bundle, then by bundle-relative path, and that order is the
    /// walk's rather than a second sort: two runs over the same bytes print the same list, and
    /// the bundle order the caller named is the order reported (#71 story 3).
    /// </summary>
    [Fact]
    public void CandidatesAreOrderedByBundleThenBundleRelativePath()
    {
        using var zebra = new TempBundle("zebra");
        zebra.Add("zulu.md", Document("type: Concept"))
            .Add("Alpha.md", Document("type: Concept"))
            .Add("alpha/mid.md", Document("type: Concept"));
        using var aardvark = new TempBundle("aardvark");
        aardvark.Add("zulu.md", Document("type: Concept")).Add("alpha.md", Document("type: Concept"));

        OkfCandidateResult forward = OkfCandidateScanner.Scan(
            [zebra.Bundle, aardvark.Bundle],
            new OkfCandidateOptions { Today = Today });
        OkfCandidateResult reversed = OkfCandidateScanner.Scan(
            [aardvark.Bundle, zebra.Bundle],
            new OkfCandidateOptions { Today = Today });

        Assert.Equal(
            ["zebra/Alpha.md", "zebra/alpha/mid.md", "zebra/zulu.md", "aardvark/alpha.md", "aardvark/zulu.md"],
            forward.Candidates.Select(candidate => $"{candidate.Bundle.Name}/{candidate.Path}"));
        Assert.Equal(
            ["aardvark/alpha.md", "aardvark/zulu.md", "zebra/Alpha.md", "zebra/alpha/mid.md", "zebra/zulu.md"],
            reversed.Candidates.Select(candidate => $"{candidate.Bundle.Name}/{candidate.Path}"));
    }

    /// <summary>
    /// Reserved files never appear: <c>index.md</c> and <c>log.md</c> carry a listing and an
    /// update history, not a concept, and that is the shared walk's decision (#72) rather than
    /// this scanner's. A bundle of nothing but reserved files yields nothing.
    /// </summary>
    [Fact]
    public void ReservedFilesNeverAppear()
    {
        using var bundle = new TempBundle();
        bundle.Add("index.md", Document("type: Index"))
            .Add("log.md", Document("type: Concept"))
            .Add("nested/index.md", Document("type: Index"))
            .Add("notes.md", Document("type: Concept"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(["notes.md"], result.Candidates.Select(candidate => candidate.Path));
        Assert.Empty(result.Quarantined);
    }

    /// <summary>
    /// A draft with no history is a candidate. Draft is a statement that the content is not
    /// finished, not a statement that somebody verified it, and the scanner decides nothing
    /// about drafts (#71).
    /// </summary>
    [Fact]
    public void ADraftConceptWithNoHistoryIsACandidate()
    {
        using var bundle = new TempBundle();
        bundle.Add("wip.md", Document("type: Concept\nstatus: draft"));

        Assert.Equal(["wip.md"], Scan(bundle).Candidates.Select(candidate => candidate.Path));
    }

    /// <summary>
    /// A stale concept with no history is a candidate. Staleness says the concept has expired,
    /// which is a reason to look at it and not a reason to leave it off the list.
    /// </summary>
    [Fact]
    public void AStaleConceptWithNoHistoryIsACandidate()
    {
        using var bundle = new TempBundle();
        bundle.Add("expired.md", Document("type: Concept\nstale_after: 2026-01-01"));

        Assert.Equal(["expired.md"], Scan(bundle).Candidates.Select(candidate => candidate.Path));
    }

    /// <summary>
    /// The per-directory conventional file is an ordinary concept — <c>about.md</c> is
    /// okf-net's own convention, not a spec §3.1 reserved name — so it is listed like any
    /// other when nobody has verified it.
    /// </summary>
    [Fact]
    public void AConventionalAboutFileIsACandidateWhenUnverified()
    {
        using var bundle = new TempBundle();
        bundle.Add("topics/about.md", Document("type: Concept"));

        Assert.Equal(["topics/about.md"], Scan(bundle).Candidates.Select(candidate => candidate.Path));
    }

    /// <summary>
    /// Already-verified concepts are excluded on both tiers, and the tier is reported rather
    /// than consulted: a concept a person verified and a concept a process confirmed both have
    /// history, which is the only question the scanner asks.
    /// </summary>
    /// <param name="author">The verification author, one per tier.</param>
    /// <param name="tier">The tier §5.3 derives from it, reported on the concept.</param>
    [Theory]
    [InlineData("human:ahormati", OkfTrustTier.HumanReviewed)]
    [InlineData("process:nightly", OkfTrustTier.MachineConfirmed)]
    public void AnAlreadyVerifiedConceptIsExcludedOnEitherTier(string author, OkfTrustTier tier)
    {
        using var bundle = new TempBundle();
        bundle.Add("settled.md", Document($"type: Concept\nverified: [{{ by: \"{author}\", at: 2026-08-01T00:00:00Z }}]"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Empty(result.Candidates);
        Assert.Empty(result.Quarantined);
        Assert.Equal(1, result.ConceptCount);
        Assert.Equal(tier, OkfDocument.TrustTier(OkfDocument.Parse(Document(
            $"type: Concept\nverified: [{{ by: \"{author}\", at: 2026-08-01T00:00:00Z }}]")).Frontmatter));
    }

    /// <summary>
    /// A concept whose frontmatter does not parse is quarantined under its own reason, and is
    /// never reported as a candidate: a file nobody could read has not been shown to lack
    /// history. It is named rather than counted, because an inventory that reports only a
    /// number cannot tell a person which file to open (#76).
    /// </summary>
    [Fact]
    public void AFileWhoseFrontmatterDoesNotParseIsQuarantinedAndNotACandidate()
    {
        using var bundle = new TempBundle();
        // Two colons on one line: not a mapping, so the frontmatter itself fails to parse.
        bundle.Add("broken.md", "---\ntype: Concept: also: nonsense\n---\n\nBody.\n")
            .Add("healthy.md", Document("type: Concept"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(["healthy.md"], result.Candidates.Select(candidate => candidate.Path));
        OkfQuarantinedConcept quarantined = Assert.Single(result.Quarantined);
        Assert.Equal("broken.md", quarantined.Path);
        Assert.Equal(OkfQuarantineReason.FrontmatterUnreadable, quarantined.Reason);
        // Both files the walk reached are counted, including the one that never became a
        // concept: an unreadable file is a file the scan reached and set aside, not one it did
        // not see.
        Assert.Equal(2, result.ConceptCount);
    }

    /// <summary>
    /// THE #76 REPRODUCTION. A file with no frontmatter fence at all PARSES SUCCESSFULLY, to
    /// empty frontmatter, so its absent <c>verified</c> key used to read as provably-absent
    /// history and the file was enumerated as a review candidate at exit 0. That is the
    /// manipulability defect this ticket exists to close: three dashes must not move a file
    /// between "candidate" and "quarantined", in either direction.
    /// </summary>
    [Fact]
    public void AFileWithNoFrontmatterFenceIsQuarantinedRatherThanEnumeratedAsAReviewCandidate()
    {
        using var bundle = new TempBundle();
        bundle.Add("healthy.md", Document("type: Concept\ntitle: Healthy"))
            .Add("README.md", "# README\n\nNot a concept.\n");

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(["healthy.md"], result.Candidates.Select(candidate => candidate.Path));
        OkfQuarantinedConcept quarantined = Assert.Single(result.Quarantined);
        Assert.Equal("README.md", quarantined.Path);
        Assert.Equal(OkfQuarantineReason.FrontmatterUnreadable, quarantined.Reason);
        Assert.False(result.IsComplete);
    }

    /// <summary>
    /// The three unreadability shapes the ticket enumerates — no opening fence, a fence that
    /// never closes, and a block that does not parse — all quarantine under the one reason, and
    /// each keeps its own detail. The symmetry of the outcome is the anti-manipulation
    /// property; the asymmetry of the detail is what lets a person fix the right thing.
    /// </summary>
    /// <param name="text">The file's whole content.</param>
    /// <param name="fragment">What the detail must name about that shape.</param>
    [Theory]
    [InlineData("# README\n\nNo frontmatter block at all.\n", "has no YAML frontmatter block")]
    [InlineData("---\ntype: Concept\n\nno closing fence\n", "Unterminated")]
    [InlineData("---\nkey: [unterminated\n---\n\nBody.\n", "Invalid YAML")]
    [InlineData("---\njust a string\n---\n\nBody.\n", "must be a YAML mapping")]
    public void EveryUnreadableShapeIsQuarantinedUnderItsOwnDetail(string text, string fragment)
    {
        using var bundle = new TempBundle();
        bundle.Add("odd.md", text);

        OkfQuarantinedConcept quarantined = Assert.Single(Scan(bundle).Quarantined);

        Assert.Equal(OkfQuarantineReason.FrontmatterUnreadable, quarantined.Reason);
        Assert.Contains(fragment, quarantined.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The frontmatter reason is a different KIND from the verification-structure reason, and
    /// the wire spellings keep them apart: one wants an editor, the other wants a linter. A
    /// shared spelling would let a caller triage a stray <c>README.md</c> as a malformed
    /// <c>verified</c> block.
    /// </summary>
    [Fact]
    public void TheFrontmatterReasonIsDistinctFromTheVerificationStructureReason()
    {
        using var bundle = new TempBundle();
        bundle.Add("fenceless.md", "# README\n\nNo frontmatter.\n")
            .Add("lying.md", Document("type: Concept\nverified: ahormati"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(
            [("fenceless.md", "frontmatter-unreadable"), ("lying.md", "verification-structure")],
            result.Quarantined.Select(item => (item.Path, item.Reason.ToWireString())));
        Assert.Equal(1, result.Count(OkfQuarantineReason.FrontmatterUnreadable));
        Assert.Equal(1, result.Count(OkfQuarantineReason.VerificationStructure));
    }

    /// <summary>
    /// Quarantine names come from the walk's order, not from the two reasons: a fenceless file,
    /// a candidate, and a malformed block interleave by path, so the incomplete list is one
    /// deterministic list rather than one list per reason.
    /// </summary>
    [Fact]
    public void QuarantinesFromBothReasonsComeBackInWalkOrder()
    {
        using var bundle = new TempBundle();
        bundle.Add("alpha.md", "# Alpha\n")
            .Add("middle.md", Document("type: Concept"))
            .Add("zulu.md", Document("type: Concept\nverified: 42"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(["alpha.md", "zulu.md"], result.Quarantined.Select(item => item.Path));
        Assert.Equal(["middle.md"], result.Candidates.Select(candidate => candidate.Path));
    }

    /// <summary>
    /// The scan is deterministic because the date is injected rather than read from the clock,
    /// mirroring the inbox scanner. A stale concept is a candidate on any date, so the date
    /// cannot change this list — which is the assertion worth having: passing a future date and
    /// a past date must produce the same candidates.
    /// </summary>
    [Fact]
    public void TheComparisonDateIsInjectedAndNeverChangesTheEligibility()
    {
        using var bundle = new TempBundle();
        bundle.Add("expired.md", Document("type: Concept\nstale_after: 2026-01-01"))
            .Add("future.md", Document("type: Concept\nstale_after: 2099-01-01"))
            .Add("plain.md", Document("type: Concept"));

        OkfCandidateResult past = OkfCandidateScanner.Scan(
            [bundle.Bundle], new OkfCandidateOptions { Today = new DateOnly(2020, 1, 1) });
        OkfCandidateResult future = OkfCandidateScanner.Scan(
            [bundle.Bundle], new OkfCandidateOptions { Today = new DateOnly(2030, 1, 1) });

        Assert.Equal(
            past.Candidates.Select(candidate => candidate.Path),
            future.Candidates.Select(candidate => candidate.Path));
    }

    /// <summary>
    /// The counts a caller reports: concepts classified, quarantined by reason, and the
    /// inventory's completeness. Completeness is the whole point of the result shape — a
    /// candidate list that omits a concept is worse than one that admits a gap (#71) — so a
    /// single quarantine must flip it.
    /// </summary>
    [Fact]
    public void OneQuarantineMakesTheInventoryIncomplete()
    {
        using var bundle = new TempBundle();
        bundle.Add("clean.md", Document("type: Concept"))
            .Add("garbage.md", Document("type: Concept\nverified: {}"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(2, result.ConceptCount);
        Assert.Single(result.Candidates);
        Assert.Single(result.Quarantined);
        Assert.False(result.IsComplete);
        Assert.Equal(1, result.Count(OkfQuarantineReason.VerificationStructure));
        Assert.Equal(0, result.Count(OkfQuarantineReason.FrontmatterUnreadable));
        Assert.Equal(["garbage.md"], result.For(OkfQuarantineReason.VerificationStructure).Select(item => item.Path));
    }

    /// <summary>
    /// A scan that finds nothing is complete and empty; a scan whose only concept is verified
    /// finds nothing. Neither is an error — an inventory that finds no candidates is a success,
    /// not a failure (#71) — which is why emptiness is a property rather than a status.
    /// </summary>
    [Fact]
    public void ASweepWithNothingToReportIsCompleteAndEmpty()
    {
        using var bundle = new TempBundle();
        bundle.Add("settled.md", Document("type: Concept\nverified: { by: \"human:ringo\" }"));

        OkfCandidateResult result = Scan(bundle);

        Assert.True(result.IsComplete);
        Assert.True(result.IsEmpty);
        Assert.Empty(result.Candidates);
        Assert.Equal(1, result.ConceptCount);
    }

    /// <summary>
    /// Multiple bundles in one scan, with the counts the result reports: every concept in
    /// every named bundle is classified exactly once, and the bundle list comes back as
    /// named. This is the shape <c>okf candidates</c> runs on — a whole working set.
    /// </summary>
    [Fact]
    public void OneScanCoversMultipleBundlesAndCountsThem()
    {
        using var project = new TempBundle("project");
        project.Add("unverified.md", Document("type: Concept"))
            .Add("verified.md", Document("type: Concept\nverified: { by: \"human:ringo\" }"))
            .Add("index.md", Document("type: Index"))
            .Add("broken.md", "---\ntype: Concept: also: nonsense\n---\n\nBody.\n");
        using var personal = new TempBundle("personal");
        personal.Add("notes.md", Document("type: Concept"))
            .Add("garbage.md", Document("type: Concept\nverified: ahormati"));

        OkfCandidateResult result = OkfCandidateScanner.Scan(
            [project.Bundle, personal.Bundle],
            new OkfCandidateOptions { Today = Today });

        Assert.Equal(["project", "personal"], result.Bundles.Select(b => b.Name));
        // 5 files reached: project's unverified.md and verified.md (index.md is reserved) plus
        // its unreadable broken.md, and personal's notes.md and garbage.md.
        Assert.Equal(5, result.ConceptCount);
        Assert.Equal(["project/unverified.md", "personal/notes.md"], result.Candidates.Select(c => $"{c.Bundle.Name}/{c.Path}"));
        // Both reasons, in the walk's order: project's unreadable file sorts before personal's
        // malformed block, so the incomplete list is one list across bundles, not one per reason.
        Assert.Equal(
            [("project/broken.md", "frontmatter-unreadable"), ("personal/garbage.md", "verification-structure")],
            result.Quarantined.Select(q => ($"{q.Bundle.Name}/{q.Path}", q.Reason.ToWireString())));
        Assert.False(result.IsComplete);
    }

    /// <summary>
    /// The verdict and the trust tier are different questions, and a candidate may legitimately
    /// print a tier that disagrees with its verdict. Here the two agree; the disagreement case
    /// is the quarantined <c>verified: {}</c>, which §5.3 calls machine-confirmed while this
    /// scanner refuses to classify it. Both facts are asserted so neither is accidental.
    /// </summary>
    [Fact]
    public void AQuarantinedConceptMayReportATierThatDisagreesWithItsVerdict()
    {
        using var bundle = new TempBundle();
        bundle.Add("claims.md", Document("type: Concept\nverified: {}"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Empty(result.Candidates);
        OkfQuarantinedConcept quarantined = Assert.Single(result.Quarantined);
        Assert.Equal(OkfTrustTier.MachineConfirmed, quarantined.TrustTier);
    }

    /// <summary>
    /// The scanner reads files and never writes them: byte-identical input and output before
    /// and after a scan, which is the read-only promise #71 makes and the reason a candidate
    /// run is safe on a dirty working tree.
    /// </summary>
    [Fact]
    public void AScanNeverWritesToAnyFileItReads()
    {
        using var bundle = new TempBundle();
        bundle.Add("clean.md", Document("type: Concept"))
            .Add("garbage.md", Document("type: Concept\nverified: ahormati"))
            .Add("broken.md", "---\ntype: Concept: also: nonsense\n---\n\nBody.\n");

        Dictionary<string, byte[]> before = Snapshot(bundle);
        Scan(bundle);
        Dictionary<string, byte[]> after = Snapshot(bundle);

        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (string path in before.Keys)
        {
            Assert.Equal(Convert.ToBase64String(before[path]), Convert.ToBase64String(after[path]));
        }
    }

    /// <summary>
    /// A file that cannot be read off the disk at all propagates rather than being counted,
    /// exactly as the walk does: that is an environment failure the command reports as one, not
    /// a fact about a concept, and a scanner that counted past it would be certifying a tree it
    /// never saw.
    /// </summary>
    [SkippableFact]
    public void AFileThatCannotBeReadAtAllPropagates()
    {
        Skip.If(OperatingSystem.IsWindows(), "POSIX permissions decide this; Windows ACLs do not.");
        Skip.If(Environment.UserName == "root", "root reads a mode-0000 file regardless.");

        // Restated as a real guard, not only Skip.If: the platform-compatibility analyzer
        // cannot read Skip.If, so without this the CA1416 finding fails the build.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var bundle = new TempBundle();
        bundle.Add("notes.md", Document("type: Concept"));
        string blocked = Path.Combine(bundle.Root, "blocked.md");
        File.WriteAllText(blocked, Document("type: Concept"));
        File.SetUnixFileMode(blocked, UnixFileMode.None);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => Scan(bundle));
        }
        finally
        {
            File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// A file whose frontmatter is a truthy non-mapping fails to parse, so it is quarantined
    /// and never listed. This is the fence-present sibling of the no-fence case, and it lands
    /// in the same place.
    /// </summary>
    [Fact]
    public void AFileWhoseFrontmatterIsNotAMappingIsQuarantinedAndNotListed()
    {
        using var bundle = new TempBundle();
        bundle.Add("scalar.md", "---\njust a string\n---\n\nBody.\n");

        OkfCandidateResult result = Scan(bundle);

        Assert.Empty(result.Candidates);
        Assert.Equal(OkfQuarantineReason.FrontmatterUnreadable, Assert.Single(result.Quarantined).Reason);
        Assert.Equal(1, result.ConceptCount);
    }

    /// <summary>
    /// A null working set is a caller bug and throws rather than answering with an empty
    /// inventory, which would look like "nothing needs review".
    /// </summary>
    [Fact]
    public void ANullWorkingSetThrows()
    {
        Assert.Throws<ArgumentNullException>(() => OkfCandidateScanner.Scan(null!));
    }

    /// <summary>
    /// Every reason has a distinct wire spelling, so #76 can add its reason without either
    /// surface printing a string the other already owns.
    /// </summary>
    [Fact]
    public void EveryReasonHasItsOwnWireSpelling()
    {
        string[] wire = [.. OkfQuarantineReasonExtensions.All.Select(reason => reason.ToWireString())];

        Assert.Equal(OkfQuarantineReasonExtensions.All.Count, wire.Distinct(StringComparer.Ordinal).Count());
        Assert.All(wire, spelling => Assert.False(string.IsNullOrWhiteSpace(spelling)));
    }

    /// <summary>
    /// The detail names the SHAPE it found, not just that something was wrong: a scalar, an
    /// empty mapping, and a sequence each read differently on the line a human uses to fix the
    /// file, and a caller cannot triage "unreadable" without knowing which unreadable it is.
    /// </summary>
    /// <param name="verified">The malformed <c>verified</c> line.</param>
    /// <param name="fragment">The wording the detail must carry for that shape.</param>
    [Theory]
    [InlineData("verified: ahormati", "the scalar 'ahormati'")]
    [InlineData("verified: 42", "the scalar '42'")]
    [InlineData("verified: {}", "an empty mapping")]
    [InlineData("verified: [{}, {}]", "a sequence of 2 item(s)")]
    [InlineData("verified: \"quoted\"", "the quoted string 'quoted'")]
    public void TheDetailNamesTheShapeItFound(string verified, string fragment)
    {
        using var bundle = new TempBundle();
        bundle.Add("odd.md", Document($"type: Concept\n{verified}"));

        OkfQuarantinedConcept quarantined = Assert.Single(Scan(bundle).Quarantined);

        Assert.Contains(fragment, quarantined.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A candidate list and a quarantine list are independent: emptiness means BOTH are empty,
    /// so a scan with candidates but no quarantine is not empty, and one with a quarantine but
    /// no candidates is not empty either. The conjunction is the assertion — an <c>||</c> would
    /// call a partially-populated result empty.
    /// </summary>
    [Fact]
    public void EmptinessMeansBothListsAreEmpty()
    {
        using var candidates = new TempBundle();
        candidates.Add("open.md", Document("type: Concept"));

        using var quarantined = new TempBundle();
        quarantined.Add("garbage.md", Document("type: Concept\nverified: ahormati"));

        using var both = new TempBundle();
        both.Add("open.md", Document("type: Concept")).Add("garbage.md", Document("type: Concept\nverified: {}"));

        using var neither = new TempBundle();
        neither.Add("settled.md", Document("type: Concept\nverified: { by: \"human:ringo\" }"));

        Assert.False(Scan(candidates).IsEmpty);
        Assert.False(Scan(quarantined).IsEmpty);
        Assert.False(Scan(both).IsEmpty);
        Assert.True(Scan(neither).IsEmpty);
    }

    /// <summary>
    /// A mapping that is neither empty nor readable is "a mapping", the one shape string the
    /// empty-mapping case does not already cover: a mapping with entries but no author renders
    /// its own text, so a caller sees what was there rather than a blank.
    /// </summary>
    [Fact]
    public void AMappingWithEntriesButNoAuthorIsNamedAsAMapping()
    {
        using var bundle = new TempBundle();
        bundle.Add("odd.md", Document("type: Concept\nverified: { at: 2026-06-25T09:00:00Z }"));

        OkfQuarantinedConcept quarantined = Assert.Single(Scan(bundle).Quarantined);

        Assert.Contains("is a mapping,", quarantined.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The detail carries the block re-emitted, not just its shape: the reason line is the one
    /// place a person learns WHICH concept and what it said, so the rendered text has to be in
    /// it. This asserts the render path returns its value rather than falling through to the
    /// shape-only wording.
    /// </summary>
    [Fact]
    public void TheDetailCarriesTheBlockAsReEmitted()
    {
        using var bundle = new TempBundle();
        bundle.Add("odd.md", Document("type: Concept\nverified: { at: 2026-06-25T09:00:00Z }"));

        OkfQuarantinedConcept quarantined = Assert.Single(Scan(bundle).Quarantined);

        Assert.StartsWith("'verified:", quarantined.Detail, StringComparison.Ordinal);
        Assert.Contains("2026-06-25", quarantined.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The merge is order-sensitive, not merely set-valued. A candidate that sorts BETWEEN two
    /// quarantines must not push them both after it, and the two reasons must interleave by path
    /// rather than group by reason — otherwise the incomplete list is one list per reason and a
    /// reader scanning it top-to-bottom loses the file that sits in the middle of the bundle.
    /// </summary>
    [Fact]
    public void QuarantinesAndCandidatesInterleaveByPathRatherThanByReason()
    {
        using var bundle = new TempBundle();
        bundle.Add("a-fenceless.md", "# A\n")
            .Add("b-signed.md", Document("type: Concept\nverified: { by: human:ahormati }"))
            .Add("c-fenceless.md", "# C\n")
            .Add("d-open.md", "---\ntype: Concept\nnever closed\n")
            .Add("e-broken.md", "---\nkey: [unterminated\n---\n\nBody.\n")
            .Add("f-lying.md", Document("type: Concept\nverified: 42"))
            .Add("g-open.md", "---\ntype: Concept\nnever closed\n");

        OkfCandidateResult result = Scan(bundle);

        Assert.Empty(result.Candidates);
        Assert.Equal(
            ["a-fenceless.md", "c-fenceless.md", "d-open.md", "e-broken.md", "f-lying.md", "g-open.md"],
            result.Quarantined.Select(item => item.Path));
        // The verification-structure quarantine is last because its path is last, not because
        // its reason was reached second: the unreadable files on either side of it keep their place.
        Assert.Equal(
            ["frontmatter-unreadable", "frontmatter-unreadable", "frontmatter-unreadable",
                "frontmatter-unreadable", "verification-structure", "frontmatter-unreadable"],
            result.Quarantined.Select(item => item.Reason.ToWireString()));
    }

    /// <summary>
    /// The incomplete list is ordered by bundle-relative path ACROSS bundles, not by bundle first:
    /// <c>MarkdownFiles()</c> orders by (bundle, path) with a separator-safe comparison, so path
    /// order is the walk's order, and a report that grouped by bundle instead would sort a vault's
    /// gaps differently depending on which bundle happened to be named first. Two bundles carrying
    /// the same filenames make the grouping visible: grouped output alternates, path-ordered
    /// output does not.
    /// </summary>
    [Fact]
    public void TheIncompleteListIsOrderedByPathAcrossBundlesNotByBundleFirst()
    {
        using var alpha = new TempBundle("alpha");
        alpha.Add("z-fenceless.md", "# Z\n")
            .Add("a-signed.md", Document("type: Concept\nverified: { by: human:x }"))
            .Add("m-lying.md", Document("type: Concept\nverified: 42"));
        using var beta = new TempBundle("beta");
        beta.Add("z-fenceless.md", "# Z\n")
            .Add("a-signed.md", Document("type: Concept\nverified: { by: human:x }"))
            .Add("m-lying.md", Document("type: Concept\nverified: 42"));

        OkfCandidateResult result = OkfCandidateScanner.Scan(
            [alpha.Bundle, beta.Bundle], new OkfCandidateOptions { Today = Today });

        // `m-lying.md` from BOTH bundles before `z-fenceless.md` from either: the paths decide,
        // and the bundle only breaks a tie between two files that share one.
        Assert.Equal(
            [("alpha", "m-lying.md"), ("beta", "m-lying.md"), ("alpha", "z-fenceless.md"), ("beta", "z-fenceless.md")],
            result.Quarantined.Select(item => (item.Bundle.Name, item.Path)));
    }

    /// <summary>
    /// Path ordering inside the merge ranks <c>/</c> below the letters, the way the walk's
    /// bundle-relative sort does: a file in a subdirectory sorts BEFORE a sibling whose name
    /// continues with a letter, so <c>topic/a.md</c> precedes <c>topicZ.md</c>. A merge that
    /// compared raw host separators would put them the other way round on Windows.
    /// </summary>
    [Fact]
    public void TheMergeOrdersASubdirectoryBeforeALetterSibling()
    {
        using var bundle = new TempBundle();
        bundle.Add("topic/a-fenceless.md", "# A\n")
            .Add("topicZ-broken.md", "---\nkey: [unterminated\n---\n\nBody.\n")
            .Add("topic/signed.md", Document("type: Concept\nverified: { by: human:x }"));

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(["topic/a-fenceless.md", "topicZ-broken.md"], result.Quarantined.Select(item => item.Path));
    }

    /// <summary>
    /// A path that is a strict prefix of another sorts first, and the comparison must not read
    /// past the shorter string to decide it: <c>note.md</c> comes before <c>notes-more.md</c>
    /// because it is shorter, not because of a character that exists in only one of them.
    /// </summary>
    [Fact]
    public void AShorterPathSortsBeforeItsOwnExtension()
    {
        using var bundle = new TempBundle();
        bundle.Add("note.md", "# Note\n")
            .Add("note.md-open.md", "---\ntype: Concept\nnever closed\n");

        OkfCandidateResult result = Scan(bundle);

        Assert.Equal(["note.md", "note.md-open.md"], result.Quarantined.Select(item => item.Path));
    }

    /// <summary>
    /// A scan with no options object still runs, and runs against today's date rather than
    /// throwing: <see cref="OkfInboxScanner" /> treats a null options the same way, and a
    /// caller who only has bundles should not have to synthesize an options object.
    /// </summary>
    [Fact]
    public void ANullOptionsObjectIsAccepted()
    {
        using var bundle = new TempBundle();
        bundle.Add("open.md", Document("type: Concept"));

        OkfCandidateResult result = OkfCandidateScanner.Scan([bundle.Bundle]);

        Assert.Equal(["open.md"], result.Candidates.Select(candidate => candidate.Path));
    }

    private static OkfCandidateResult Scan(TempBundle bundle) =>
        OkfCandidateScanner.Scan([bundle.Bundle], new OkfCandidateOptions { Today = Today });

    private static Dictionary<string, byte[]> Snapshot(TempBundle bundle) =>
        Directory.GetFiles(bundle.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static string Line(string? verified) => verified ?? string.Empty;

    private static string Document(string frontmatter)
    {
        string trimmed = frontmatter.Trim('\n');
        return $"---\n{trimmed}\n---\n\nBody.\n";
    }
}
