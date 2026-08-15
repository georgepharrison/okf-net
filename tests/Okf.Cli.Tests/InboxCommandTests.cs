using System.Text.Json;

namespace Okf.Cli.Tests;

/// <summary>
/// End-to-end <c>okf inbox</c> behavior: the flags, the exit-code contract (PRD CLI-12,
/// CLI-14), the grouped text report, and the JSON array that is its machine contract.
/// </summary>
public class InboxCommandTests
{
    [Fact]
    public void HelpExitsZeroAndDescribesTheReasons()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "inbox", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf inbox [path] [options]", run.Output, StringComparison.Ordinal);
        Assert.Contains("--fail-if-any", run.Output, StringComparison.Ordinal);
        Assert.Contains("Unacknowledged", run.Output, StringComparison.Ordinal);
        Assert.Contains("Source drift", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsReachableFromTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf inbox [path]", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--nope")]
    [InlineData("--format=yaml")]
    [InlineData("--format")]
    public void AMalformedCommandLineIsAUsageFailure(string argument)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "inbox", argument);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void ASecondBareArgumentIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "inbox", "one", "two");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("at most one path", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnresolvableVaultIsAnEnvironmentFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "inbox");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EachReasonGetsItsOwnGroupWithADetailLine()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/drafted.md", Concept("""
            type: Concept
            title: Drafted
            status: draft
            generated: { by: claude-fable/5, at: 2020-01-01T00:00:00Z }
            """));
        tree.Write("vault/bundles/b/expired.md", Concept("""
            type: Concept
            title: Expired
            stale_after: 2020-01-01
            generated: { by: "human:ringo", at: 2019-01-01T00:00:00Z }
            verified:
              - { by: "process:check", at: 2019-06-01T00:00:00Z }
            """));
        tree.Write("vault/bundles/b/moved.md", Concept("""
            type: Concept
            title: Moved
            generated: { by: "human:ringo", at: 2019-01-01T00:00:00Z }
            verified:
              - { by: "process:check", at: 2019-06-01T00:00:00Z }
            sources:
              - id: spec
                resource: https://example.org/spec
                last_modified: 2020-02-02
            """));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("Unacknowledged (1)", run.Output, StringComparison.Ordinal);
        Assert.Contains("status: draft;", run.Output, StringComparison.Ordinal);
        Assert.Contains("Stale (1)", run.Output, StringComparison.Ordinal);
        Assert.Contains("stale_after 2020-01-01", run.Output, StringComparison.Ordinal);
        Assert.Contains("Source drift (1)", run.Output, StringComparison.Ordinal);
        Assert.Contains("moved since: spec (2020-02-02)", run.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Checked 3 concepts in 1 bundle: 3 concepts need attention (1 unacknowledged, 1 stale, 1 with source drift).",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AVaultWithNothingWaitingSaysSoAndExitsZero()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/settled.md", Concept("""
            type: Concept
            title: Settled
            generated: { by: "human:ringo", at: 2020-01-01T00:00:00Z }
            """));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal("Checked 1 concept in 1 bundle: nothing needs attention.", run.Output.TrimEnd('\n'));
    }

    [Fact]
    public void FailIfAnyTurnsANonEmptyInboxIntoExitOne()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\nstatus: draft"));

        var reported = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);
        var gated = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--fail-if-any");

        // Same report, different exit code: the flag changes the branch, never the content.
        Assert.Equal(CliApplication.ExitSuccess, reported.ExitCode);
        Assert.Equal(CliApplication.ExitDiagnostics, gated.ExitCode);
        Assert.Equal(reported.Output, gated.Output);
    }

    [Fact]
    public void FailIfAnyStillExitsZeroWhenNothingIsWaiting()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/settled.md", Concept("type: Concept\ntitle: Settled"));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--fail-if-any");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
    }

    [Fact]
    public void AnUnparseableConceptIsCountedRatherThanSilentlySkipped()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/broken.md", "---\nkey: [unterminated\n---\n\nBody.\n");
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\nstatus: draft"));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Contains("Skipped 1 file whose frontmatter does not parse", run.Output, StringComparison.Ordinal);
        Assert.Contains("Checked 1 concept in 1 bundle", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonIsAStableArrayCarryingEveryFieldAJudgementNeeds()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/topic/drifted.md", Concept("""
            type: Reference
            title: Drifted
            status: draft
            stale_after: 2020-01-01
            generated: { by: claude-fable/5, at: 2019-01-01T00:00:00Z }
            verified:
              - { by: "process:check", at: 2018-06-01T00:00:00Z }
            sources:
              - id: spec
                resource: https://example.org/spec
                last_modified: 2020-02-02
            """));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--format", "json");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        var item = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal("topic/drifted", item.GetProperty("id").GetString());
        Assert.Equal("topic/drifted.md", item.GetProperty("path").GetString());
        Assert.Equal("Drifted", item.GetProperty("title").GetString());
        Assert.Equal("Reference", item.GetProperty("type").GetString());
        Assert.Equal(
            ["unacknowledged", "stale", "source-drift"],
            item.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.Equal("machine-confirmed", item.GetProperty("trustTier").GetString());
        Assert.True(item.GetProperty("stale").GetBoolean());
        Assert.Equal("draft", item.GetProperty("status").GetString());
        Assert.Equal("claude-fable/5", item.GetProperty("generatedBy").GetString());
        Assert.Equal("2019-01-01T00:00:00Z", item.GetProperty("generatedAt").GetString());
        Assert.Equal("process:check", item.GetProperty("verifiedBy").GetString());
        Assert.Equal("2020-01-01", item.GetProperty("staleAfter").GetString());

        var source = Assert.Single(item.GetProperty("driftedSources").EnumerateArray());
        Assert.Equal("spec", source.GetProperty("id").GetString());
        Assert.Equal("2020-02-02", source.GetProperty("lastModified").GetString());
    }

    [Fact]
    public void AnEmptyInboxIsAnEmptyJsonArray()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/settled.md", Concept("type: Concept\ntitle: Settled"));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    [Fact]
    public void VerboseReportsResolutionOnStderrAndLeavesTheContractAlone()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\nstatus: draft"));

        var quiet = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--json");
        var loud = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--json", "--verbose");

        Assert.Equal(quiet.Output, loud.Output);
        Assert.Empty(quiet.Error);
        Assert.Contains("okf: resolved bundle", loud.Error, StringComparison.Ordinal);
        Assert.Contains("okf: read 1 concept, 1 item on the inbox", loud.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsAreOrderedDeterministicallyByPath()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        foreach (var name in (string[])["zebra.md", "alpha.md", "topic/mid.md"])
        {
            tree.Write($"vault/bundles/b/{name}", Concept("type: Concept\nstatus: draft"));
        }

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        Assert.Equal(
            ["alpha", "topic/mid", "zebra"],
            document.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    private static string Concept(string frontmatter) => $"---\n{frontmatter}\n---\n\nBody.\n";
}
