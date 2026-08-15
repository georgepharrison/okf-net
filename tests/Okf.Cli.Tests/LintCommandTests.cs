using System.Text.Json;

namespace Okf.Cli.Tests;

/// <summary>
/// End-to-end <c>okf lint</c> behavior over the fixture bundles: exit codes (PRD CLI-14),
/// the default severity contract (CLI-5), and both output formats (CLI-15).
/// </summary>
public class LintCommandTests
{
    [Fact]
    public void ConformantFixtureIsSilentAndExitsZero()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("conformant"));

        Assert.Empty(run.DiagnosticLines);
        Assert.Equal(
            "Checked 5 files in 1 bundle (19 rules: 17 active, 2 hidden): 0 errors, 0 warnings, 0 infos.",
            run.Summary);
        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
    }

    [Fact]
    public void TheSummaryLineSaysHowManyRulesWereLive()
    {
        using var home = new TempTree();
        var defaults = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("conformant"));
        var enabled = Cli.RunIn(
            home.Root, home.Root, "lint", Fixtures.Bundle("conformant"), "--severity", "OKF0304=warning");
        var silenced = Cli.RunIn(
            home.Root,
            home.Root,
            "lint",
            Fixtures.Bundle("conformant"),
            "--severity",
            "OKF0001=hidden",
            "--severity",
            "OKF0002=hidden");

        // Friction #11: "0 errors" from a run with the gate switched off has to read
        // differently from "0 errors" with the gate on. The counts move with the
        // configuration, in both directions.
        Assert.Equal(19, Okf.Core.OkfRules.All.Count);
        Assert.Contains("(19 rules: 17 active, 2 hidden)", defaults.Summary, StringComparison.Ordinal);
        Assert.Contains("(19 rules: 18 active, 1 hidden)", enabled.Summary, StringComparison.Ordinal);
        Assert.Contains("(19 rules: 15 active, 4 hidden)", silenced.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenFixtureReportsEveryConformanceFailureAndExitsOne()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"));

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("no-frontmatter.md:1: error OKF0001", run.Output, StringComparison.Ordinal);
        Assert.Contains("unterminated.md:1: error OKF0001", run.Output, StringComparison.Ordinal);
        Assert.Contains("invalid-yaml.md:1: error OKF0001", run.Output, StringComparison.Ordinal);
        Assert.Contains("empty-type.md:2: error OKF0002", run.Output, StringComparison.Ordinal);
        Assert.Contains("nested/index.md:1: error OKF0003", run.Output, StringComparison.Ordinal);
        Assert.Contains("log.md:3: error OKF0004", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenFixtureReportsTheHygieneAndProvenanceRules()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"));

        // hygiene.md trips one rule from each family at once.
        Assert.Contains("hygiene.md:1: warning OKF0301", run.Output, StringComparison.Ordinal);
        Assert.Contains("warning OKF0101: Footnote `[^missing]`", run.Output, StringComparison.Ordinal);
        Assert.Contains("warning OKF0102: Source `never-cited`", run.Output, StringComparison.Ordinal);
        Assert.Contains("warning OKF0103: Source `never-cited`", run.Output, StringComparison.Ordinal);
        Assert.Contains("warning OKF0201", run.Output, StringComparison.Ordinal);
        Assert.Contains("warning OKF0202", run.Output, StringComparison.Ordinal);
        Assert.Contains("info OKF0302: Link target `/nowhere.md`", run.Output, StringComparison.Ordinal);
        Assert.Contains("other-name.md:3: warning OKF0303: Concept title duplicates", run.Output, StringComparison.Ordinal);
        Assert.Contains(
            "other-name.md:1: warning OKF0303: Concept filename duplicates `nested/other_name.md`",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenRulesAreComputedButNotReported()
    {
        using var home = new TempTree();
        var quiet = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"));
        var loud = Cli.RunIn(
            home.Root, home.Root, "lint", Fixtures.Bundle("broken"), "--severity", "OKF0304=warning");

        // Missing `tags` is opt-in: computed on every run, reported only once configured.
        Assert.DoesNotContain("OKF0304", quiet.Output, StringComparison.Ordinal);
        Assert.Contains("hygiene.md:1: warning OKF0304", loud.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsAloneDoNotChangeTheExitCode()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("warnings-only"));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("warning OKF0301", run.Output, StringComparison.Ordinal);
        Assert.Contains("info OKF0302", run.Output, StringComparison.Ordinal);
        Assert.Contains("0 errors, 1 warning, 1 info.", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TreatAllWarningsAsErrorsPromotesWarningsButNotInfo()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(
            home.Root,
            home.Root,
            "lint",
            Fixtures.Bundle("warnings-only"),
            "--treat-all-warnings-as-errors");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("error OKF0301", run.Output, StringComparison.Ordinal);
        Assert.Contains("info OKF0302", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void PerRuleOverridesPromoteAndDemote()
    {
        using var home = new TempTree();

        var promoted = Cli.RunIn(
            home.Root, home.Root, "lint", Fixtures.Bundle("warnings-only"), "--severity", "OKF0302=error");
        Assert.Equal(CliApplication.ExitDiagnostics, promoted.ExitCode);
        Assert.Contains("error OKF0302", promoted.Output, StringComparison.Ordinal);

        var demoted = Cli.RunIn(
            home.Root, home.Root, "lint", Fixtures.Bundle("warnings-only"), "--severity=OKF0301=hidden");
        Assert.Equal(CliApplication.ExitSuccess, demoted.ExitCode);
        Assert.DoesNotContain("OKF0301", demoted.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonOutputIsAStableArrayOfDiagnosticRecords()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("warnings-only"), "--format", "json");

        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        var records = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, records.Count);
        Assert.Equal("OKF0301", records[0].GetProperty("id").GetString());
        Assert.Equal("missing-description", records[0].GetProperty("rule").GetString());
        Assert.Equal("warning", records[0].GetProperty("severity").GetString());
        Assert.Equal(1, records[0].GetProperty("line").GetInt32());
        Assert.EndsWith("orphan.md", records[0].GetProperty("absolutePath").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("warnings-only", records[0].GetProperty("bundle").GetString(), StringComparison.Ordinal);
        Assert.Equal("OKF0302", records[1].GetProperty("id").GetString());

        // The messages keep their backticks and section signs rather than \uXXXX escapes.
        Assert.Contains("§4.1", records[0].GetProperty("message").GetString(), StringComparison.Ordinal);

        var again = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("warnings-only"), "--json");
        Assert.Equal(run.Output, again.Output);
    }

    [Fact]
    public void RunningTwiceProducesIdenticalOutput()
    {
        using var home = new TempTree();
        var first = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"));
        var second = Cli.RunIn(home.Root, home.Root, "lint", Fixtures.Bundle("broken"));

        Assert.Equal(first.Output, second.Output);
        Assert.Equal(first.ExitCode, second.ExitCode);
    }

    [Fact]
    public void VerboseReportsResolutionAndChangedSeverities()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(
            home.Root,
            home.Root,
            "lint",
            Fixtures.Bundle("conformant"),
            "--verbose",
            "--severity",
            "OKF0302=error");

        Assert.Contains("okf: resolved bundle", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: global config: none", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: severity OKF0302 = error (from command line)", run.Error, StringComparison.Ordinal);

        // Every rule is listed with its effective severity and the layer that decided it,
        // not only the ones a layer moved: an unexpected default is as surprising as an
        // unexpected override.
        Assert.Contains(
            "okf: severity OKF0301 = warning (from built-in defaults) missing-description",
            run.Error,
            StringComparison.Ordinal);
        Assert.All(
            Okf.Core.OkfRules.All,
            rule => Assert.Contains($"okf: severity {rule.Id} = ", run.Error, StringComparison.Ordinal));
    }

    [Fact]
    public void JsonStaysABareArrayAndRunMetadataGoesToStderr()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(
            home.Root,
            home.Root,
            "lint",
            Fixtures.Bundle("warnings-only"),
            "--json",
            "--verbose",
            "--severity",
            "OKF0301=hidden");

        // PRD CLI-15's contract is the array itself, so the counts cannot ride along in an
        // envelope; stderr carries them instead and stdout parses exactly as before. The
        // demoted rule is the point: its finding is missing from the array, and only the
        // stderr metadata says a rule was silenced rather than clean.
        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal("OKF0302", Assert.Single(document.RootElement.EnumerateArray()).GetProperty("id").GetString());

        Assert.Contains("okf: checked 2 files in 1 bundle", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: 19 rules: 16 active, 3 hidden", run.Error, StringComparison.Ordinal);
        Assert.Contains(
            "okf: diagnostics 0 errors, 0 warnings, 1 info, 1 at hidden severity",
            run.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PathsArePrintedRelativeToTheWorkingDirectory()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(Fixtures.Root, home.Root, "lint", "warnings-only");

        Assert.Contains("warnings-only/orphan.md:1: warning OKF0301", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AMutatedRawItemIsReportedWhenTheTargetIsAVault()
    {
        // OKF0310 is scoped to the vault rather than to a bundle root, and it follows the
        // vault the working set resolved — which is the same vault whose okf.json already
        // decides severities, so naming one bundle inside it does not opt out of the rule.
        // A bundle with no vault around it is the case that leaves it inapplicable.
        using var project = new TempTree();
        MutatedCapture(project);

        var vault = Cli.RunIn(project.Root, project.Root, "lint", "okf");
        var inside = Cli.RunIn(project.Root, project.Root, "lint", "okf/bundles/b");
        var loose = Cli.RunIn(project.Root, project.Root, "lint", Fixtures.Bundle("conformant"));

        Assert.Contains("OKF0310", vault.RuleIds);
        Assert.Contains("okf/raw/manifest.json", vault.Output, StringComparison.Ordinal);
        Assert.Contains("OKF0310", inside.RuleIds);
        Assert.DoesNotContain("OKF0310", loose.RuleIds);
    }

    [Fact]
    public void TheProjectConfigCanPromoteAMutatedRawItemToAnError()
    {
        using var project = new TempTree();
        MutatedCapture(project);
        project.Write("okf/okf.json", """{ "lint": { "severities": { "OKF0310": "error" } } }""");

        var run = Cli.RunIn(project.Root, project.Root, "lint", "okf");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("error OKF0310", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A vault holding one bundle and one ingested capture whose artifact was edited after
    /// ingestion — the situation the rule exists for.
    /// </summary>
    /// <param name="project">The project tree to build the vault in.</param>
    private static void MutatedCapture(TempTree project)
    {
        project.Write("okf/bundles/b/note.md", """
            ---
            type: Reference
            title: Note
            description: A concept resting on the captured artifact.
            tags: [fixture]
            ---

            # Note
            """);

        // The recorded hash is not the hash of what sits in raw/, which is what an
        // artifact edited after ingestion looks like from the manifest's side.
        project.Write("okf/raw/2026-06-01-thing.txt", "edited after ingestion\n");
        project.Write("okf/raw/manifest.json", """
            {
              "manifestVersion": 1,
              "captures": [
                {
                  "id": "2026-06-01-thing",
                  "form": "flat",
                  "files": [
                    {
                      "path": "2026-06-01-thing.txt",
                      "sha256": "e0b7e1b0b3a4a0d3fbb1e0dcb5cbd3d1f8d90c9d7f9f7b0a1f2e3d4c5b6a7980"
                    }
                  ],
                  "capturedAt": "2026-06-01T00:00:00Z",
                  "capturedBy": "tests",
                  "ingestion": {
                    "at": "2026-06-01T00:00:00Z",
                    "by": "tests",
                    "concepts": ["bundles/b/note.md"]
                  }
                }
              ]
            }
            """);
    }
}
