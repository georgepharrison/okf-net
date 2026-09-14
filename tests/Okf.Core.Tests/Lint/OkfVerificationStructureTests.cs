namespace Okf.Core.Tests.Lint;

/// <summary>
/// <c>OKF0203</c> (unreadable-verification), the lint rule for a <c>verified</c> block that is
/// present but is not a structure the §5.2 normalization can safely read (work item #78).
/// </summary>
/// <remarks>
/// <para>
/// The expected behaviour is work item #71's eligibility table — the same table
/// <c>OkfVerificationHistoryTests</c> and <c>OkfCandidateScannerTests</c> assert — transcribed
/// rather than read off the implementation. The table is the specification, and the point of
/// asserting it a third time through the linter is that the rule must fire on <em>exactly</em>
/// the shapes <c>okf candidates</c> quarantines: two surfaces that disagree about what counts
/// as malformed are the failure mode this ticket exists to prevent.
/// </para>
/// <para>
/// The rule is deliberately not about everything else that could be wrong with a
/// <c>verified</c> block: the §7 actor grammar is #82's question, a missing or unparseable
/// timestamp is nobody's, and a file whose frontmatter never read is <c>OKF0001</c>'s.
/// </para>
/// </remarks>
public class OkfVerificationStructureTests
{
    /// <summary>
    /// The quarantined arm of the eligibility table, asserted as a lint warning. Row for row
    /// these are the shapes <c>okf candidates</c> refuses to classify.
    /// </summary>
    /// <param name="verified">The <c>verified</c> lines as written, without the frontmatter fences.</param>
    [Theory]
    // Any scalar claims verification without being a structure of events at all.
    [InlineData("verified: ahormati")]
    [InlineData("verified: yesterday")]
    [InlineData("verified: 42")]
    [InlineData("verified: true")]
    [InlineData("verified: 0")]
    [InlineData("verified: \"null\"")]
    // A mapping naming nobody — the stub a producer leaves behind.
    [InlineData("verified: {}")]
    [InlineData("verified: { at: 2026-06-25T09:00:00Z }")]
    // Absent, null and the empty string are one fact in three YAML spellings: no author.
    [InlineData("verified: { by: }")]
    [InlineData("verified: { by: null }")]
    [InlineData("verified: { by: \"\" }")]
    // An author that is a structure is not an author.
    [InlineData("verified:\n  - by: [a, b]")]
    [InlineData("verified:\n  - by: { name: ringo }")]
    // A sequence is readable only when EVERY item is; one junk item hides the concept.
    [InlineData("verified: [{}, {}]")]
    [InlineData("verified:\n  - {}\n  - {}")]
    [InlineData("verified:\n  - { by: human:ahormati }\n  - just a string")]
    [InlineData("verified:\n  - { by: human:ahormati }\n  - [a, b]")]
    [InlineData("verified:\n  - { by: human:ahormati }\n  - {}")]
    [InlineData("verified: [human:ahormati]")]
    [InlineData("verified: [[a], [b]]")]
    // A merge key claims verification but its author sits behind `<<` nothing resolves.
    [InlineData("verified: { <<: { by: human:ahormati } }")]
    public void ABlockThatCannotBeReadAsEventsIsReported(string verified)
    {
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept([verified]));

        var diagnostic = Assert.Single(bundle.Lint(), diagnostic => diagnostic.RuleId == OkfRules.UnreadableVerification);

        Assert.Equal(OkfSeverity.Warning, diagnostic.Severity);
    }

    /// <summary>
    /// The legally empty forms: an absent key, an explicit null, and an empty sequence. These
    /// say "nobody has signed this yet", which is absence, not a defect — and absence is
    /// <c>okf candidates</c>' candidate, never its quarantine.
    /// </summary>
    /// <param name="verified">The <c>verified</c> lines as written, or none for an absent key.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("verified:")]
    [InlineData("verified: null")]
    [InlineData("verified: ~")]
    [InlineData("verified: []")]
    public void TheLegallyEmptyFormsAreSilent(string? verified)
    {
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept(verified is null ? [] : [verified]));

        Assert.DoesNotContain(OkfRules.UnreadableVerification, bundle.LintIds());
    }

    /// <summary>
    /// The readable shapes: a mapping that names an author, and a sequence where every item
    /// does. A legal event with an unparseable timestamp stays silent — the timestamp is never
    /// adjudicated — and neither is an author whose identifier is thin, because that is the
    /// actor grammar (#82) and not this rule.
    /// </summary>
    /// <param name="verified">The <c>verified</c> lines as written.</param>
    [Theory]
    [InlineData("verified: { by: human:ahormati }")]
    [InlineData("verified: { by: human:ahormati, at: yesterday }")]
    [InlineData("verified: { by: human:ahormati, at: [2026, 6, 25] }")]
    [InlineData("verified: { by: \"human:\" }")]
    [InlineData("verified: { by: \" \" }")]
    [InlineData("verified:\n  - { by: process:nightly, at: 2026-08-01T00:00:00Z }\n  - { by: \"human:ringo\", at: 2026-08-10T00:00:00Z }")]
    [InlineData("verified:\n  - { by: Human:ahormati }\n  - { by: humans:ahormati }")]
    public void ABlockThatReadsAsEventsIsSilent(string verified)
    {
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept([verified]));

        Assert.DoesNotContain(OkfRules.UnreadableVerification, bundle.LintIds());
    }

    /// <summary>
    /// The §7 actor grammar is never consulted, so the grammar-valid and the grammar-invalid
    /// spelling of an author must land on the same side of the rule. <c>OkfActor.IsValid</c> is
    /// asserted on the row itself so the two columns genuinely differ in grammar — otherwise
    /// the test would prove nothing about the rule ignoring it.
    /// </summary>
    /// <param name="author">The <c>by</c> value as written, quoted for YAML.</param>
    /// <param name="wellFormed">Whether §7's grammar accepts it — irrelevant to the rule.</param>
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
        Assert.Equal(wellFormed, OkfActor.IsValid(author));

        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept(["verified: { by: \"" + author + "\" }"]));

        Assert.DoesNotContain(OkfRules.UnreadableVerification, bundle.LintIds());
    }

    /// <summary>
    /// PRD CLI-15: the diagnostic points at the line the <c>verified</c> key is written on, the
    /// way <c>OKF0201</c> and <c>OKF0202</c> point at theirs. The line is read back off the file
    /// rather than counted, so the assertion says where the reader's editor lands.
    /// </summary>
    [Fact]
    public void TheDiagnosticPointsAtTheLineTheVerifiedKeyIsWrittenOn()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "concept.md",
            Concept(["generated: { by: okf-net/tests, at: 2026-01-01T00:00:00Z }", "verified: ahormati"]));

        var diagnostic = Assert.Single(bundle.Lint(), diagnostic => diagnostic.RuleId == OkfRules.UnreadableVerification);

        string line = File.ReadAllLines(Path.Combine(bundle.Root, "concept.md"))[diagnostic.Line!.Value - 1];

        Assert.StartsWith("verified:", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A block written as a block sequence spans several lines, and the key's line is still the
    /// one reported — not the first offending item's, and not the top of the file.
    /// </summary>
    [Fact]
    public void TheKeyLineIsReportedForAMultiLineBlock()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "concept.md",
            Concept(["verified:\n  - { by: human:ahormati }\n  - just a string"]));

        var diagnostic = Assert.Single(bundle.Lint(), diagnostic => diagnostic.RuleId == OkfRules.UnreadableVerification);

        string[] lines = File.ReadAllLines(Path.Combine(bundle.Root, "concept.md"));

        Assert.Equal("verified:", lines[diagnostic.Line!.Value - 1]);
    }

    /// <summary>
    /// The message carries what the rule found, in the words the trust rules use: the key, the
    /// section, and the fact that a person has to go and look. The shape itself is not
    /// re-emitted here — <c>okf candidates</c> is the surface that prints the block's bytes.
    /// </summary>
    [Fact]
    public void TheMessageNamesTheKeyAndTheSection()
    {
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept(["verified: {}"]));

        var diagnostic = Assert.Single(bundle.Lint(), diagnostic => diagnostic.RuleId == OkfRules.UnreadableVerification);

        Assert.Contains("`verified`", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("§5.2", diagnostic.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pairing that makes this rule worth a rule id: one concept, one unreadable block, and
    /// both surfaces agreeing about it. <c>okf lint</c> warns and still exits 0 (PRD CLI-5 —
    /// defaults block only §11 conformance) while <c>okf candidates</c> quarantines and exits 1;
    /// the exit-code disagreement is the designed difference #71 records.
    /// </summary>
    [Fact]
    public void TheShapeTheScannerQuarantinesIsTheShapeReportedHere()
    {
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept(["verified: ahormati"]));

        OkfCandidateResult scan = OkfCandidateScanner.Scan([bundle.Bundle], new OkfCandidateOptions
        {
            Today = TempBundle.Today,
        });

        Assert.False(scan.IsComplete);
        Assert.Equal(
            OkfQuarantineReason.VerificationStructure,
            Assert.Single(scan.Quarantined).Reason);

        var diagnostic = Assert.Single(bundle.Lint(), diagnostic => diagnostic.RuleId == OkfRules.UnreadableVerification);

        Assert.Equal(OkfSeverity.Warning, diagnostic.Severity);
        Assert.EndsWith("concept.md", diagnostic.Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file whose frontmatter never read is <c>OKF0001</c>'s finding, not this rule's: the
    /// block was never there to be unreadable, and reporting both would double-report one file
    /// from two surfaces.
    /// </summary>
    /// <param name="text">The whole file's text.</param>
    [Theory]
    [InlineData("# No frontmatter fence at all\n")]
    [InlineData("---\ntype: Reference\nverified: ahormati\n\n# Never closed\n")]
    public void AFileWhoseFrontmatterNeverReadsStaysOKF0001sFinding(string text)
    {
        using var bundle = new TempBundle();
        bundle.Add("broken.md", text);

        Assert.DoesNotContain(OkfRules.UnreadableVerification, bundle.LintIds());
        Assert.Contains(OkfRules.UnparseableFrontmatter, bundle.LintIds());
    }

    /// <summary>
    /// Reserved files are not concepts (§3.1) and are never judged on trust metadata they must
    /// not carry. An <c>index.md</c> holding an unreadable <c>verified</c> block is reported for
    /// the frontmatter it must not have (§8/§12) and for nothing else from this rule.
    /// </summary>
    [Fact]
    public void ReservedFilesAreNeverReportedByThisRule()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "index.md",
            "---\ntype: Reference\nverified: ahormati\n---\n\n# Index\n\n* [Concept](concept.md) - A concept.\n")
            .Add(
            "log.md",
            "---\ntype: Reference\nverified: ahormati\n---\n\n## 2026-06-01\n\n* Nothing.\n");

        Assert.DoesNotContain(OkfRules.UnreadableVerification, bundle.LintIds());
    }

    /// <summary>
    /// A rule is a severity like any other, and the identifier is the configuration key: any of
    /// the four severities can be set for it, and the diagnostic comes back at that severity.
    /// The default is warning and stays warning — an error default would fail the reference
    /// bundles the acceptance criteria require tolerating, since nothing about a directory says a
    /// bundle is a foreign one.
    /// </summary>
    /// <param name="severity">The configured severity.</param>
    [Theory]
    [InlineData(OkfSeverity.Hidden)]
    [InlineData(OkfSeverity.Info)]
    [InlineData(OkfSeverity.Warning)]
    [InlineData(OkfSeverity.Error)]
    public void TheRuleFollowsItsConfiguredSeverity(OkfSeverity severity)
    {
        using var bundle = new TempBundle();
        bundle.Add("concept.md", Concept(["verified: ahormati"]));

        var layer = new OkfSeverityLayer("test");
        layer.Severities[OkfRules.UnreadableVerification] = severity;

        var diagnostics = bundle.Lint(new OkfLintOptions
        {
            Today = TempBundle.Today,
            Severities = new OkfSeverityResolver([layer]),
        });

        // Hidden is a severity, not a switch: the finding is still computed and the command is
        // what withholds it, which is what lets `--verbose` say a rule was silenced rather than
        // clean (decisions.md §7).
        var diagnostic = Assert.Single(diagnostics, item => item.RuleId == OkfRules.UnreadableVerification);

        Assert.Equal(severity, diagnostic.Severity);
    }

    /// <summary>
    /// The catalog entry itself: the trust range's next number, a kebab-case nickname, and a
    /// warning default. Asserted here because <c>--list-rules</c> prints these and the
    /// configuration key is the identifier, never the nickname (decisions.md Q2).
    /// </summary>
    [Fact]
    public void TheRuleShipsInTheTrustRangeWithAWarningDefault()
    {
        var rule = OkfRules.Get(OkfRules.UnreadableVerification);

        Assert.Equal("OKF0203", rule.Id);
        Assert.Equal("unreadable-verification", rule.Nickname);
        Assert.Equal(OkfRuleCategory.Trust, rule.Category);
        Assert.Equal(OkfSeverity.Warning, rule.DefaultSeverity);
    }

    /// <summary>
    /// A concept with nothing wrong with it still trips nothing after this rule arrives — the
    /// rule must not fire on the plain absence of <c>verified</c>, which is what every
    /// unverified concept in a real vault carries.
    /// </summary>
    [Fact]
    public void AConceptWithNoVerifiedKeyAtAllTripsNothing()
    {
        using var bundle = new TempBundle();
        bundle.Add("clean.md", Concept([]));

        Assert.Empty(bundle.Lint());
    }

    private static string Concept(params string[] extraLines)
    {
        string extra = extraLines.Length == 0 ? string.Empty : string.Join('\n', extraLines) + "\n";

        return $"""
            ---
            type: Reference
            title: Concept
            description: A concept for the verification-structure rule.
            tags: [fixture]
            {extra}---

            # Concept
            """;
    }
}
