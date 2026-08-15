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
        Assert.Equal(
            Path.Combine(bundle, "topic", "drifted.md"),
            item.GetProperty("absolutePath").GetString());
        Assert.EndsWith("topic/drifted.md", item.GetProperty("displayPath").GetString()!, StringComparison.Ordinal);
        Assert.Equal(bundle, item.GetProperty("bundle").GetString());
        Assert.Equal("b", item.GetProperty("bundleName").GetString());
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
        Assert.Equal("2018-06-01T00:00:00Z", item.GetProperty("verifiedAt").GetString());
        Assert.Equal("2020-01-01", item.GetProperty("staleAfter").GetString());

        // Ages are whole days from the written date, so they move with the clock; what is
        // fixed is that both are present and ordered the way the two dates are.
        Assert.True(item.GetProperty("ageDays").GetInt32() > item.GetProperty("staleDays").GetInt32());

        var source = Assert.Single(item.GetProperty("driftedSources").EnumerateArray());
        Assert.Equal("spec", source.GetProperty("id").GetString());
        Assert.Equal("https://example.org/spec", source.GetProperty("resource").GetString());
        Assert.Equal("2020-02-02", source.GetProperty("lastModified").GetString());
    }

    [Fact]
    public void AbsentFrontmatterBecomesJsonNullRatherThanAMissingKey()
    {
        // A consumer reading `record.generatedAt` must get null, not undefined: an absent
        // key and a null one are the same thing only until somebody writes a schema.
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/bare.md", Concept("status: draft"));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--json");

        using var document = JsonDocument.Parse(run.Output);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        foreach (var name in (string[])
                 ["type", "generatedBy", "generatedAt", "verifiedBy", "verifiedAt", "staleAfter", "ageDays", "staleDays"])
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty(name).ValueKind);
        }

        Assert.Empty(item.GetProperty("driftedSources").EnumerateArray());

        // Indented, like every other JSON surface okf writes.
        Assert.Contains("\n  {", run.Output, StringComparison.Ordinal);
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

    [Fact]
    public void ASingleWaitingConceptIsCountedInTheSingular()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/settled.md", Concept("type: Concept\ntitle: Settled"));
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\ntitle: Drafted\nstatus: draft"));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Contains(
            "Checked 2 concepts in 1 bundle: 1 concept needs attention (1 unacknowledged, 0 stale, 0 with source drift).",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADraftWithNoGeneratedStampSaysSoRatherThanInventingOne()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\ntitle: Drafted\nstatus: draft"));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Contains("status: draft; no generated stamp; never verified", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegeneratedConceptNamesItsLastVerification()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/moved-on.md", Concept("""
            type: Concept
            title: Moved On
            generated: { by: claude-fable/5, at: 2020-06-01T00:00:00Z }
            verified:
              - { by: "human:ringo", at: 2020-01-01T00:00:00Z }
            """));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Contains(
            "generated claude-fable/5 2020-06-01T00:00:00Z (",
            run.Output,
            StringComparison.Ordinal);
        Assert.Contains("last verified human:ringo 2020-01-01T00:00:00Z", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnrecordedActorIsNamedAsSuchOnBothSides()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/anon.md", Concept("""
            type: Concept
            title: Anonymous
            generated: { at: 2020-06-01T00:00:00Z }
            verified:
              - { at: 2020-01-01T00:00:00Z }
            """));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Contains("generated an unrecorded actor 2020-06-01T00:00:00Z", run.Output, StringComparison.Ordinal);
        Assert.Contains("last verified an unrecorded actor 2020-01-01T00:00:00Z", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AStampDatedInTheFutureIsReportedAsAheadRatherThanAsAnAge()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/ahead.md", Concept("""
            type: Concept
            title: Ahead
            generated: { by: claude-fable/5, at: 2099-01-01T00:00:00Z }
            """));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Contains("(in ", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(" ago)", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ADriftedSourceWithNoIdIsNamedByItsResource()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/moved.md", Concept("""
            type: Concept
            title: Moved
            generated: { by: "human:ringo", at: 2020-01-01T00:00:00Z }
            sources:
              - resource: https://example.org/spec
                last_modified: 2020-02-02
            """));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Contains(
            "moved since: https://example.org/spec (2020-02-02)",
            run.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--format=json")]
    [InlineData("--json")]
    public void EverySpellingOfTheJsonFlagProducesTheSameArray(string flag)
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\nstatus: draft"));

        var inline = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, flag);
        var spaced = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--format", "json");

        Assert.Equal(spaced.Output, inline.Output);
        Assert.StartsWith("[", inline.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTextIsTheDefaultAndIsNotJson()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/drafted.md", Concept("type: Concept\nstatus: draft"));

        var explicitText = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle, "--format", "text");
        var implicitText = Cli.RunIn(tree.Root, tree.Root, "inbox", bundle);

        Assert.Equal(implicitText.Output, explicitText.Output);
        Assert.StartsWith("Unacknowledged (1)", explicitText.Output, StringComparison.Ordinal);
    }

    private static string Concept(string frontmatter) => $"---\n{frontmatter}\n---\n\nBody.\n";
}
