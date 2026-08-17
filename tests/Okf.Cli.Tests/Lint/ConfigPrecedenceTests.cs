namespace Okf.Cli.Tests.Lint;

/// <summary>
/// PRD CLI-4: CLI args &gt; project config &gt; global config, over the built-in defaults.
/// Each test drives the same rule (<c>OKF0301</c>, missing description) from a different
/// layer, because the exit code makes the effective severity observable.
/// </summary>
public class ConfigPrecedenceTests
{
    private const string PromoteToError = """
        { "lint": { "severities": { "OKF0301": "error" } } }
        """;

    private const string DemoteToHidden = """
        { "lint": { "severities": { "OKF0301": "hidden" } } }
        """;

    [Fact]
    public void GlobalConfigOverridesTheBuiltInDefault()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("warnings-only", "bundle");
        tree.Write(Path.Combine(".config", "okf", "okf.json"), PromoteToError);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("error OKF0301", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectConfigOverridesTheGlobalConfig()
    {
        using var tree = new TempTree();
        tree.CopyFixture("warnings-only", Path.Combine("project", "okf", "bundles", "notes"));
        tree.Write(Path.Combine(".config", "okf", "okf.json"), PromoteToError);
        tree.Write(Path.Combine("project", "okf", "okf.json"), DemoteToHidden);

        var run = CliHarness.RunIn(Path.Combine(tree.Root, "project"), tree.Root, "lint");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.DoesNotContain("OKF0301", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandLineOverridesTheProjectConfig()
    {
        using var tree = new TempTree();
        tree.CopyFixture("warnings-only", Path.Combine("project", "okf", "bundles", "notes"));
        tree.Write(Path.Combine("project", "okf", "okf.json"), DemoteToHidden);

        var run = CliHarness.RunIn(
            Path.Combine(tree.Root, "project"), tree.Root, "lint", "--severity", "OKF0301=error");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("error OKF0301", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void LowerLayersStillSupplyKeysHigherLayersDoNotSet()
    {
        using var tree = new TempTree();
        tree.CopyFixture("warnings-only", Path.Combine("project", "okf", "bundles", "notes"));
        tree.Write(
            Path.Combine(".config", "okf", "okf.json"),
            """
            { "lint": { "severities": { "OKF0302": "error" } } }
            """);
        tree.Write(Path.Combine("project", "okf", "okf.json"), DemoteToHidden);

        var run = CliHarness.RunIn(Path.Combine(tree.Root, "project"), tree.Root, "lint");

        // The project file speaks only to OKF0301; the global file's OKF0302 survives.
        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("error OKF0302", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("OKF0301", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TreatAllWarningsAsErrorsIsConfigurable()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("warnings-only", "bundle");
        tree.Write(
            Path.Combine(".config", "okf", "okf.json"),
            """
            { "lint": { "treatAllWarningsAsErrors": true } }
            """);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle, "--verbose");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("treatAllWarningsAsErrors", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TagRegistryComesFromConfigAndIsOptIn()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("warnings-only", "bundle");
        tree.Write(
            Path.Combine(".config", "okf", "okf.json"),
            """
            { "lint": { "tagRegistry": ["approved"], "severities": { "OKF0305": "warning" } } }
            """);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle);

        Assert.Contains("warning OKF0305: Tag `fixture` is not in the bundle's tag registry.", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTagRegistryOverridesTheGlobalTagRegistry()
    {
        using var tree = new TempTree();
        tree.CopyFixture("warnings-only", Path.Combine("project", "okf", "bundles", "notes"));
        tree.Write(
            Path.Combine(".config", "okf", "okf.json"),
            """
            { "lint": { "tagRegistry": ["fixture"], "severities": { "OKF0305": "warning" } } }
            """);
        tree.Write(
            Path.Combine("project", "okf", "okf.json"),
            """
            { "lint": { "tagRegistry": ["approved"] } }
            """);

        var run = CliHarness.RunIn(Path.Combine(tree.Root, "project"), tree.Root, "lint");

        Assert.Contains("warning OKF0305: Tag `fixture` is not in the bundle's tag registry.", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitConfigFileReplacesTheProjectConfig()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("warnings-only", "bundle");
        var config = tree.Write("elsewhere.json", PromoteToError);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle, "--config", config);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("error OKF0301", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownRuleIdInConfigIsReportedRatherThanIgnored()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("warnings-only", "bundle");
        tree.Write(
            Path.Combine(".config", "okf", "okf.json"),
            """
            { "lint": { "severities": { "OKF9999": "error" } } }
            """);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("OKF9999", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedConfigFileIsAnEnvironmentFailure()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("warnings-only", "bundle");
        tree.Write(Path.Combine(".config", "okf", "okf.json"), "{ not json");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "lint", bundle);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("not valid JSON", run.Error, StringComparison.Ordinal);
    }
}
