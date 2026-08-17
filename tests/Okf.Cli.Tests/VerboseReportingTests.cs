namespace Okf.Cli.Tests;

/// <summary>
/// What <c>--verbose</c> reports. PRD CLI-4 makes the effective value of anything that
/// changed a run's behaviour part of the contract, so these are assertions about the
/// report, not about wording for its own sake: which vault was resolved, which bundles it
/// spans, which configuration layer supplied each answer, and — where a flag chose between
/// two paths through the command — which path was taken.
/// </summary>
public class VerboseReportingTests
{
    /// <summary>
    /// Every verb that resolves a working set names it, and names each bundle in it, on
    /// stderr. Two bundles, so a report that named only the first would fail (PRD CLI-1
    /// resolves them all).
    /// </summary>
    [Theory]
    [InlineData("lint")]
    [InlineData("index")]
    [InlineData("index --check")]
    [InlineData("inbox")]
    [InlineData("search concept")]
    [InlineData("site --out site")]
    public void VerboseNamesTheResolutionAndEveryBundle(string commandLine)
    {
        using var tree = new TempTree();
        var clean = tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var noisy = tree.CopyFixture("warnings-only", Path.Combine("okf", "bundles", "noisy"));

        var run = Cli.RunIn(tree.Root, tree.Root, [.. commandLine.Split(' '), "--verbose"]);

        Assert.Contains("okf: resolved ", run.Error, StringComparison.Ordinal);
        Assert.Contains($"okf: bundle {clean}", run.Error, StringComparison.Ordinal);
        Assert.Contains($"okf: bundle {noisy}", run.Error, StringComparison.Ordinal);
    }

    /// <summary>Without the flag, none of it is written: stderr stays for failures.</summary>
    [Theory]
    [InlineData("lint")]
    [InlineData("index")]
    [InlineData("inbox")]
    [InlineData("search concept")]
    [InlineData("site --out site")]
    public void WithoutVerboseNothingIsWrittenToStandardError(string commandLine)
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Cli.RunIn(tree.Root, tree.Root, commandLine.Split(' '));

        Assert.Empty(run.Error);
    }

    /// <summary>
    /// <c>okf index --check</c> writes nothing, and <c>--verbose</c> says which of the two
    /// it is doing before it does it — the one line that distinguishes a dry check from a
    /// run that is about to rewrite files (AD-13/AD-14, PRD CLI-10).
    /// </summary>
    [Theory]
    [InlineData(false, "okf: writing generated index.md files")]
    [InlineData(true, "okf: --check: nothing will be written")]
    public void IndexVerboseSaysWhetherItWillWrite(bool check, string expected)
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        string[] args = check ? ["index", "--check", "--verbose"] : ["index", "--verbose"];

        var run = Cli.RunIn(tree.Root, tree.Root, args);

        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf site --single-file</c> and the multi-page default are two different shapes on
    /// disk (AD-40), so <c>--verbose</c> names which one is being written and where.
    /// </summary>
    [Theory]
    [InlineData(false, "okf: writing a multi-page site into ")]
    [InlineData(true, "okf: writing one self-contained file into ")]
    public void SiteVerboseSaysWhichShapeItWillWrite(bool singleFile, string expected)
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var output = Path.Combine(tree.Root, "site");
        string[] args = singleFile
            ? ["site", "--out", output, "--single-file", "--verbose"]
            : ["site", "--out", output, "--verbose"];

        var run = Cli.RunIn(tree.Root, tree.Root, args);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(expected + output, run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf lint --verbose</c> reports both configuration layers by name. A layer that
    /// was looked for and not found says where it looked, which is the difference between
    /// "no project config" and "your project config is somewhere else" (PRD CLI-4, CLI-6).
    /// </summary>
    [Fact]
    public void LintVerboseReportsBothConfigLayersWhenNeitherExists()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Cli.RunIn(tree.Root, tree.Root, "lint", "--verbose");

        Assert.Contains("okf: global config: none", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: project config: none (looked for ", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project config that exists is reported by its own path rather than as "none", and
    /// the "looked for" note disappears with it.
    /// </summary>
    [Fact]
    public void LintVerboseNamesTheProjectConfigItLoaded()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var config = tree.Write(
            Path.Combine("okf", "okf.json"),
            """
            { "lint": { "severities": { "OKF0301": "error" } } }
            """);

        var run = Cli.RunIn(tree.Root, tree.Root, "lint", "--verbose");

        Assert.Contains($"okf: project config: {config}", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("okf: project config: none", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every rule is reported with its effective severity and the layer that decided it —
    /// every rule, not only the reconfigured ones, because a rule sitting at a default the
    /// reader did not expect is exactly as surprising as one a config layer moved.
    /// </summary>
    [Fact]
    public void LintVerboseReportsEveryRulesEffectiveSeverityAndItsSource()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Cli.RunIn(
            tree.Root, tree.Root, "lint", "--verbose", "--severity", "OKF0301=error");

        foreach (var rule in Okf.Core.OkfRules.All)
        {
            Assert.Contains($"okf: severity {rule.Id} = ", run.Error, StringComparison.Ordinal);
        }

        Assert.Contains(
            "okf: severity OKF0301 = error (from command line)",
            run.Error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The run metadata `--json` cannot carry — counts of files, rules and diagnostics —
    /// goes to stderr after the report, so the JSON on stdout stays the bare array
    /// PRD CLI-11/CLI-15 pins.
    /// </summary>
    [Fact]
    public void LintVerboseReportsTheRunMetadataOnStandardErrorEvenUnderJson()
    {
        using var tree = new TempTree();
        tree.CopyFixture("warnings-only", Path.Combine("okf", "bundles", "noisy"));

        var run = Cli.RunIn(tree.Root, tree.Root, "lint", "--json", "--verbose");

        Assert.StartsWith("[", run.Output.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("okf: checked ", run.Error, StringComparison.Ordinal);
        Assert.Contains(" active, ", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: diagnostics ", run.Error, StringComparison.Ordinal);
        Assert.Contains(" at hidden severity", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf bundle --verbose</c> names each bundle it is packaging and the shape and
    /// destination it is writing (AD-37, PRD CLI-14).
    /// </summary>
    [Fact]
    public void BundleVerboseNamesWhatItPackagesAndWhatItWrites()
    {
        using var tree = new TempTree();
        var clean = tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var destination = Path.Combine(tree.Root, "dist.tar.gz");

        var run = Cli.RunIn(tree.Root, tree.Root, "bundle", "--out", destination, "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains($"okf: packaging {clean}", run.Error, StringComparison.Ordinal);
        Assert.Contains($"to {destination}", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf search --verbose</c> reports the scope and the layer that chose it, the
    /// parsed query, and the match mode — the three things that decide which results came
    /// back (PRD CLI-3, CLI-4, AD-28).
    /// </summary>
    [Fact]
    public void SearchVerboseReportsScopeQueryAndMatchMode()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Cli.RunIn(tree.Root, tree.Root, "search", "concept", "--scope", "project", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf: scope project (from command line)", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: query ", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: match mode ", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf inbox --verbose</c> reports how much it read, not only what it found: an
    /// empty inbox over zero concepts and an empty inbox over a hundred are different
    /// answers (PRD CLI-12).
    /// </summary>
    [Fact]
    public void InboxVerboseReportsWhatItRead()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Cli.RunIn(tree.Root, tree.Root, "inbox", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Matches(@"okf: read \d+ concepts?, \d+ items? on the inbox", run.Error);
    }

    /// <summary>
    /// <c>okf init --verbose</c> names the vault it is about to scaffold, and says when
    /// that vault is the personal one — which is chosen by environment rather than by the
    /// working directory (decisions.md §6), so it is the case a reader cannot infer.
    /// </summary>
    [Theory]
    [InlineData(false, "okf: vault '")]
    [InlineData(true, "okf: personal vault '")]
    public void InitVerboseNamesTheVaultItScaffolds(bool personal, string expected)
    {
        using var tree = new TempTree();
        var project = tree.CreateDirectory("project");
        var okfHome = Path.Combine(tree.Root, "personal");
        string[] args = personal ? ["init", "--personal", "--verbose"] : ["init", "--verbose"];

        var run = Cli.Run(Cli.Environment(project, tree.Root, okfHome), args);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf registry --verbose</c> names the registry file it read. It is machine-wide
    /// state outside any vault (AD-51), so where it lives is the first thing a reader
    /// needs.
    /// </summary>
    [Fact]
    public void RegistryVerboseNamesTheRegistryFile()
    {
        using var home = new TempTree();

        var run = Cli.RunIn(home.Root, home.Root, "registry", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf: registry ", run.Error, StringComparison.Ordinal);
        Assert.Contains(home.Root, run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The registry's <c>--verbose</c> report reads the global configuration as the global
    /// layer, which is the only layer where <c>autoRegister</c> is a legal setting: read as
    /// a project file it would be refused outright, and the setting a person put on their
    /// own machine would become an error (AD-51, PRD CLI-2). It is reported and does
    /// nothing — registration stays explicit — so the report is the whole of its effect.
    /// </summary>
    [Fact]
    public void RegistryVerboseReportsTheGlobalAutoRegisterSetting()
    {
        using var tree = new TempTree();
        tree.Write(Path.Combine(".config", "okf", "okf.json"), """{ "autoRegister": true }""");

        var run = Cli.RunIn(tree.Root, tree.Root, "registry", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf: autoRegister true (from ", run.Error, StringComparison.Ordinal);
        Assert.Contains("nothing auto-registers", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf lint</c> reads the same file as the same layer, so a machine-level
    /// <c>autoRegister</c> does not turn every lint run into a configuration error.
    /// </summary>
    [Fact]
    public void LintAcceptsAGlobalAutoRegisterSetting()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        tree.Write(Path.Combine(".config", "okf", "okf.json"), """{ "autoRegister": true }""");

        var run = Cli.RunIn(tree.Root, tree.Root, "lint", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf: global config: ", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("okf: global config: none", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf mcp --verbose</c> reports the scope it was launched with and the layer that
    /// chose it before it says it is ready. AD-30 fixes the scope at launch and no tool
    /// argument can widen it, so this line is the only place a client's operator can see
    /// what the server will serve.
    /// </summary>
    [Fact]
    public void McpVerboseReportsTheFixedScopeAndThatItIsReady()
    {
        using var tree = new TempTree();
        var clean = tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Mcp.Run(Cli.Environment(tree.Root, tree.Root), ["--scope", "project", "--verbose"]);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf: scope project (from command line)", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: resolved ", run.Error, StringComparison.Ordinal);
        Assert.Contains($"okf: bundle {clean}", run.Error, StringComparison.Ordinal);
        Assert.Contains("okf: mcp server ready on stdio", run.Error, StringComparison.Ordinal);
    }

    /// <summary>Without <c>--verbose</c> the server starts silently, so stdio carries only JSON-RPC.</summary>
    [Fact]
    public void McpWithoutVerboseStartsSilently()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Mcp.Run(Cli.Environment(tree.Root, tree.Root), []);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Empty(run.Error);
    }

    /// <summary>
    /// A resolution note sits between the sentence and the bundle list, because it explains
    /// the sentence: `okf mcp --scope all` reports why a registered vault contributed
    /// nothing, and a reader who met that line after the bundles would read it as being
    /// about the last bundle listed.
    /// </summary>
    [Fact]
    public void AResolutionNoteIsReportedBetweenTheSentenceAndTheBundles()
    {
        using var tree = new TempTree();
        var bundle = tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var workingSet = Okf.Core.OkfDiscovery.Resolve(tree.Root, Cli.Environment(tree.Root, tree.Root));
        using var error = new StringWriter();

        VerboseReport.WorkingSet(error, workingSet, ["no project vault: nothing here"]);

        Assert.Equal(
            [
                $"okf: resolved {workingSet.Resolution}",
                "okf: no project vault: nothing here",
                $"okf: bundle {bundle}",
            ],
            error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }
}
