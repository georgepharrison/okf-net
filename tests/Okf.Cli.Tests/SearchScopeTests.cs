using Okf.Core;

namespace Okf.Cli.Tests;

/// <summary>
/// Scope selection on <c>okf search</c> and <c>okf mcp</c> (PRD CLI-2, CLI-3, CLI-4,
/// MCP-3). Every run resolves against a temporary home, so no test reads the developer's
/// own registry.
/// </summary>
public class SearchScopeTests
{
    private const string Query = "widget";

    /// <summary>
    /// The default is project-only whatever else is registered — the determinism rule the
    /// whole scope design is built around (PRD CLI-3).
    /// </summary>
    [Fact]
    public void TheDefaultScopeIsTheProjectVaultEvenWithARegistryFullOfEntries()
    {
        using var world = new World();

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("project-only", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("registered-only", run.Output, StringComparison.Ordinal);
        Assert.Contains("okf: scope project (from built-in defaults)", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteredScopeSeesTheRegisteredVaultAndNotTheProject()
    {
        using var world = new World();

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--scope", "registered");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("registered-only", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("project-only", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonalScopeSeesThePersonalVaultWithoutItBeingRegistered()
    {
        using var world = new World(register: false);

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--scope", "personal");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("personal-only", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// With more than one root in play a bare relative path could name two different files,
    /// so every result is printed absolute and the summary counts the vaults.
    /// </summary>
    [Fact]
    public void AllSpansBothRootsAndDisambiguatesEveryResultByItsRoot()
    {
        using var world = new World();

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--scope", "all");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("project-only", run.Output, StringComparison.Ordinal);
        Assert.Contains("registered-only", run.Output, StringComparison.Ordinal);
        Assert.Contains("in 2 vaults", run.Output, StringComparison.Ordinal);
        Assert.All(
            run.OutputLines.Where(line => line.Contains(".md", StringComparison.Ordinal)),
            line => Assert.Contains(world.Home.Root, line, StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingRegisteredPathIsReportedOnStderrAndTheSearchStillSucceeds()
    {
        using var world = new World();
        Directory.Delete(world.Registered, recursive: true);

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--scope", "all");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("is missing", run.Error, StringComparison.Ordinal);
        Assert.Contains("project-only", run.Output, StringComparison.Ordinal);
    }

    /// <summary>AD-31's chain, exercised layer by layer with `--verbose` naming the source.</summary>
    [Fact]
    public void ConfigurationSetsTheScopeAndTheProjectLayerBeatsTheGlobalOne()
    {
        using var world = new World();
        world.Home.Write(Path.Combine(".config", "okf", "okf.json"), """{ "search": { "scope": "all" } }""");

        var fromGlobal = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--verbose");

        File.WriteAllText(
            Path.Combine(world.Project, "okf", "okf.json"),
            """{ "search": { "scope": "project" } }""");
        var fromProject = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--verbose");
        var fromFlag = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--scope", "registered", "--verbose");

        Assert.Contains("okf: scope all (from ", fromGlobal.Error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(".config", "okf", "okf.json"), fromGlobal.Error, StringComparison.Ordinal);
        Assert.Contains("registered-only", fromGlobal.Output, StringComparison.Ordinal);

        Assert.Contains("okf: scope project (from ", fromProject.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("registered-only", fromProject.Output, StringComparison.Ordinal);

        Assert.Contains("okf: scope registered (from command line)", fromFlag.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("project-only", fromFlag.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownScopeValueIsAUsageFailure()
    {
        using var world = new World();

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--scope", "everything");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Unknown --scope value", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void APathAndAScopeTogetherAreRefusedRatherThanOneBeingIgnored()
    {
        using var world = new World();

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "okf", "--scope", "all");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("exclusive", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    /// <summary>
    /// The parity claim PRD MCP-3 makes, asserted about bytes: one Core entry point
    /// (<see cref="OkfScope" />) resolves both surfaces, so a scope cannot mean one thing to
    /// the CLI and another to the server.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("personal")]
    [InlineData("registered")]
    [InlineData("all")]
    public void TheMcpServerAndTheCliResolveIdenticalScopes(string scope)
    {
        using var world = new World();
        var environment = Cli.Environment(world.Project, world.Home.Root);

        var cli = Cli.Run(environment, "search", Query, "--scope", scope, "--json");
        var server = Mcp.Run(
            environment,
            ["--scope", scope],
            Mcp.Initialize(),
            Mcp.Call(2, "okf_search", $$"""{"query":"{{Query}}"}"""));

        Assert.Equal(CliApplication.ExitSuccess, cli.ExitCode);
        var (text, isError) = server.Content(1);
        Assert.False(isError);
        Assert.Equal(cli.Output.Trim(), text.Trim());
    }

    [Fact]
    public void TheServerReportsItsScopeAndTheLayerAtStartup()
    {
        using var world = new World();
        var environment = Cli.Environment(world.Project, world.Home.Root);

        var run = Mcp.Run(environment, ["--scope", "all", "--verbose"], Mcp.Initialize());

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf: scope all (from command line)", run.Error, StringComparison.Ordinal);
        Assert.Contains(world.Registered, run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// With two roots in scope two bundles can share a directory name, so `okf_list` reports
    /// a label that is unique in scope and the `bundle` argument accepts it — the tool's
    /// arguments do not grow and nothing about the scope can be widened per call (AD-30).
    /// </summary>
    [Fact]
    public void CollidingBundleNamesAreDisambiguatedByLabelRatherThanByANewToolArgument()
    {
        using var world = new World();
        var twin = Directory.CreateDirectory(
            Path.Combine(world.Registered, "okf", "bundles", "notes")).FullName;
        File.WriteAllText(
            Path.Combine(twin, "twin.md"),
            "---\ntype: Concept\ntitle: Twin\n---\n\nA widget in the registered vault.\n");

        var environment = Cli.Environment(world.Project, world.Home.Root);
        var run = Mcp.Run(
            environment,
            ["--scope", "all"],
            Mcp.Initialize(),
            Mcp.Call(2, "okf_list"),
            Mcp.Call(3, "okf_list", """{"bundle":"notes"}"""));

        var (scope, _) = run.Content(1);
        var (ambiguous, isError) = run.Content(2);

        Assert.Contains($"\"label\": \"{Path.Combine(world.Project, "okf", "bundles", "notes")}\"", scope, StringComparison.Ordinal);
        Assert.Contains($"\"label\": \"{twin}\"", scope, StringComparison.Ordinal);
        Assert.True(isError);
        Assert.Contains("More than one bundle in scope is named 'notes'", ambiguous, StringComparison.Ordinal);
    }

    /// <summary>
    /// A registry file that does not parse is an environment failure the caller can read
    /// and repair, not a crash — and reading it never rewrites it, because a command that
    /// silently repaired machine-wide state would be one nobody could trust (AD-7).
    /// </summary>
    [Theory]
    [InlineData("registered")]
    [InlineData("all")]
    public void AMalformedRegistryIsTheUsageExitCodeAndTheFileIsLeftExactlyAsItWas(string scope)
    {
        using var world = new World();
        var registry = world.Home.Write(
            Path.Combine(".config", "okf", OkfRegistry.FileName),
            "{ not json");

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query, "--scope", scope);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
        Assert.Contains(registry, run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
        Assert.Equal("{ not json", File.ReadAllText(registry));
    }

    /// <summary>
    /// The default scope must not so much as read the registry: a broken registry on one
    /// machine cannot be allowed to break the query every machine agrees on (PRD CLI-3).
    /// </summary>
    [Fact]
    public void TheProjectScopeNeverReadsTheRegistryAndIsUnaffectedByABrokenOne()
    {
        using var world = new World();
        world.Home.Write(Path.Combine(".config", "okf", OkfRegistry.FileName), "{ not json");

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("project-only", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheServerRefusesToStartOnAMalformedRegistryRatherThanCrashing()
    {
        using var world = new World();
        world.Home.Write(Path.Combine(".config", "okf", OkfRegistry.FileName), "{ not json");
        var environment = Cli.Environment(world.Project, world.Home.Root);

        var run = Mcp.Run(environment, ["--scope", "registered"], Mcp.Initialize());

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    /// <summary>
    /// The layer `autoRegister` is refused in is named by every command that reads the
    /// project config, `okf search` included — the refusal is only useful if it arrives as
    /// a diagnostic rather than a stack trace (PRD CLI-2, CLI-14).
    /// </summary>
    [Fact]
    public void AutoRegisterInTheProjectConfigIsAUsageFailureForSearchToo()
    {
        using var world = new World();
        File.WriteAllText(
            Path.Combine(world.Project, "okf", "okf.json"),
            """{ "autoRegister": true }""");

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("global setting", run.Error, StringComparison.Ordinal);
        Assert.Contains("project config", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    /// <summary>A `search.scope` typo must be a diagnostic, not an unhandled exception.</summary>
    [Fact]
    public void AnUnknownConfiguredScopeIsAUsageFailureRatherThanACrash()
    {
        using var world = new World();
        File.WriteAllText(
            Path.Combine(world.Project, "okf", "okf.json"),
            """{ "search": { "scope": "everything" } }""");

        var run = Cli.RunIn(world.Project, world.Home.Root, "search", Query);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("`search.scope` must be one of", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    /// <summary>Two vaults and a registry, all inside one temporary home.</summary>
    private sealed class World : IDisposable
    {
        public World(bool register = true)
        {
            Home = new TempTree();
            Project = Home.CreateDirectory("widgets");
            Registered = Home.CreateDirectory("catalog");

            Scaffold(Project, "project-only");
            Scaffold(Registered, "registered-only");
            Cli.RunIn(Home.Root, Home.Root, "init", "--personal");
            Concept(Path.Combine(Home.Root, "okf", "bundles", "okf", "personal-only.md"), "personal-only");

            if (register)
            {
                Cli.RunIn(Registered, Home.Root, "register");
            }
        }

        public TempTree Home { get; }

        public string Project { get; }

        public string Registered { get; }

        public void Dispose() => Home.Dispose();

        private static void Scaffold(string project, string name)
        {
            Cli.RunIn(project, project, "init", "--name", "notes");
            Concept(Path.Combine(project, "okf", "bundles", "notes", name + ".md"), name);
        }

        private static void Concept(string path, string name)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                $"---\ntype: Concept\ntitle: {name}\ndescription: A widget concept.\n---\n\n"
                + "A widget lives here.\n");
        }
    }
}
