using System.Text.Json;

namespace Okf.Cli.Tests.Lint;

/// <summary>
/// <c>OKF0203</c> (unreadable-verification) through the command: the <c>--severity</c> override
/// accepts the identifier, <c>--json</c> carries the finding to a machine consumer, and the two
/// surfaces agree about which concepts are malformed (work item #78).
/// </summary>
/// <remarks>
/// <para>
/// The last group is the acceptance criterion that matters most and the one a unit test cannot
/// make: <c>okf lint</c> and <c>okf candidates</c> must never disagree about what counts as
/// malformed, because a concept one surface hides from the other is a concept that escapes
/// review. They are asserted here on the same bundle, from the same bytes, in the same test.
/// </para>
/// <para>
/// The exit-code difference between them is designed and asserted rather than smoothed over:
/// lint defaults the rule to warning and so exits 0 (PRD CLI-5 — defaults block only §11
/// conformance), while candidates gate on the completeness of its own inventory and exits 1
/// whatever any severity says (AD-5).
/// </para>
/// </remarks>
public class VerificationStructureLintTests
{
    [Fact]
    public void TheBrokenFixtureReportsTheUnreadableVerificationBlock()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"));

        // The `verified:` key sits on line 7 of the fixture, and the reader's editor should land
        // there rather than at the top of the file (PRD CLI-15).
        Assert.Contains(
            "unreadable-verification.md:7: warning OKF0203: `verified` claims verification",
            run.Output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// PRD CLI-15's contract is that any diagnostic is reconfigurable by identifier, so the new
    /// identifier has to be accepted everywhere an old one is: the flag, the config file, and
    /// the promotion that turns a warning into a block.
    /// </summary>
    /// <param name="severity">The severity to set.</param>
    /// <param name="expected">The severity the report should print it at.</param>
    [Theory]
    [InlineData("hidden", null)]
    [InlineData("info", "info")]
    [InlineData("warning", "warning")]
    [InlineData("error", "error")]
    public void TheSeverityOverrideAcceptsTheNewIdentifier(string severity, string? expected)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(
            home.Root,
            home.Root,
            "lint",
            Fixtures.Bundle("broken"),
            "--severity",
            $"OKF0203={severity}");

        // A rejected identifier would be a usage failure (exit 2), so reaching a report at all
        // is half the assertion; the printed severity is the other half.
        Assert.DoesNotContain("--severity expects", run.Error, StringComparison.Ordinal);
        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);

        // Anchored on the severity word plus the id: a bare "OKF0203" also matches the
        // `--verbose` lines and a hidden rule must not be reported at any severity.
        Assert.Equal(
            expected is null ? 0 : 1,
            run.DiagnosticLines.Count(line => line.Contains($"{expected} OKF0203", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnErrorDefaultIsNotTheDefaultAndAnOverrideCanMakeItOne()
    {
        using var home = new TempTree();

        // Warning by default: the bundle is warned about, not blocked, and the exit code says so.
        var defaults = CliHarness.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"));

        Assert.Contains("warning OKF0203", defaults.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("error OKF0203", defaults.Output, StringComparison.Ordinal);

        // And a consumer can promote it, which is the configuration the default deliberately is not.
        var promoted = CliHarness.RunIn(
            home.Root,
            home.Root,
            "lint",
            Fixtures.Bundle("broken"),
            "--severity",
            "OKF0203=error");

        Assert.Contains("error OKF0203", promoted.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Story 14: a machine consumer retrieves these structural defects through
    /// <c>okf lint --json</c>, which is why the rule needed a rule id at all — the candidate
    /// command's stdout stays a clean array of candidates.
    /// </summary>
    [Fact]
    public void TheJsonReportCarriesTheFindingAsADiagnosticRecord()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"), "--json");

        using var document = JsonDocument.Parse(run.Output);
        var record = Assert.Single(
            document.RootElement.EnumerateArray(),
            element => element.GetProperty("id").GetString() == "OKF0203");

        Assert.Equal("unreadable-verification", record.GetProperty("rule").GetString());
        Assert.Equal("warning", record.GetProperty("severity").GetString());
        Assert.EndsWith("unreadable-verification.md", record.GetProperty("path").GetString(), StringComparison.Ordinal);
        Assert.Equal(7, record.GetProperty("line").GetInt32());
    }

    /// <summary>
    /// THE CRITERION: lint reports exactly the concepts candidates quarantines, and not one more
    /// or one less. One bundle holding a quarantined shape, a readable block, and a legally empty
    /// one; both commands read it; the finding and the quarantine name the same file.
    /// </summary>
    [Fact]
    public void LintReportsExactlyTheConceptsCandidatesQuarantines()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/notes");
        tree.Write(Path.Combine(bundle, "malformed.md"), Concept("verified: ahormati"));
        tree.Write(Path.Combine(bundle, "signed.md"), Concept("verified: { by: human:ringo, at: yesterday }"));
        tree.Write(Path.Combine(bundle, "empty.md"), Concept("verified: []"));

        var lint = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle);
        var candidates = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        var reported = lint.DiagnosticLines
            .Where(line => line.Contains("OKF0203", StringComparison.Ordinal))
            .ToList();

        // One file reported, and it is the malformed one.
        Assert.Equal(
            ["malformed.md:6: warning OKF0203"],
            reported.Select(Shorten).ToArray());

        // One quarantine, and it is the same file.
        var quarantined = QuarantineLines(candidates.Error);
        Assert.Single(quarantined);
        Assert.Contains("malformed.md", quarantined[0], StringComparison.Ordinal);
        Assert.Contains("verification-structure", quarantined[0], StringComparison.Ordinal);

        // The designed disagreement, stated rather than hidden: lint warns and exits 0; the
        // scanner's completeness gate is severity-independent and exits 1.
        Assert.Equal(CliApplication.ExitSuccess, lint.ExitCode);
        Assert.Equal(CliApplication.ExitDiagnostics, candidates.ExitCode);
    }

    /// <summary>
    /// The stderr line <c>okf candidates</c> prints sends the reader to <c>okf lint</c>, and
    /// <c>okf lint</c>'s message sends them to <c>okf candidates</c>. Asserted as a pair because
    /// either sentence alone is a pointer a reader can follow into a tool that says nothing.
    /// </summary>
    [Fact]
    public void TheTwoSurfacesPointAtEachOther()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/notes");
        tree.Write(Path.Combine(bundle, "malformed.md"), Concept("verified: {}"));

        var lint = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle);
        var candidates = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        Assert.Contains("`okf candidates`", lint.Output, StringComparison.Ordinal);
        Assert.Single(QuarantineLines(candidates.Error));
    }

    private static string[] QuarantineLines(string error) =>
        [.. error.Split('\n').Where(line => line.Contains("could not classify", StringComparison.Ordinal))];

    private static string Shorten(string line)
    {
        int start = line.LastIndexOf(Path.DirectorySeparatorChar) + 1;
        int rule = line.IndexOf(" OKF", StringComparison.Ordinal);
        return line[start..(rule < 0 ? line.Length : rule + " OKF0203".Length)];
    }

    private static string Concept(string verified) =>
        $"""
        ---
        type: Reference
        title: A concept
        description: A concept for the verification-structure rule.
        tags: [fixture]
        {verified}
        ---

        # A concept
        """;
}
