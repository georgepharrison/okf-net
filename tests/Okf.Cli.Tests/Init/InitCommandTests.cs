using Okf.Core;

namespace Okf.Cli.Tests.Init;

/// <summary>
/// End-to-end <c>okf init</c> behavior: the flags, the exit-code contract (PRD CLI-8,
/// CLI-14), idempotency, and structural parity with the vault this repository built by
/// hand before the command existed.
/// </summary>
public class InitCommandTests
{
    [Fact]
    public void HelpExitsZeroAndDescribesTheFlags()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "init", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf init [path] [options]", run.Output, StringComparison.Ordinal);
        Assert.Contains("--name <bundle>", run.Output, StringComparison.Ordinal);
        Assert.Contains("--personal", run.Output, StringComparison.Ordinal);
        Assert.Contains("--no-agents-md", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHappyPathAlsoWritesTheAgentsMdAndClaudeMdPointerAtTheProjectRoot()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("pointers");

        var run = CliHarness.RunIn(project, home.Root, "init");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("AGENTS.md: created", run.Output, StringComparison.Ordinal);
        Assert.Contains("CLAUDE.md: created", run.Output, StringComparison.Ordinal);

        var agentsMd = File.ReadAllText(Path.Combine(project, "AGENTS.md"));
        Assert.Contains(OkfAgentPointer.BeginMarker, agentsMd, StringComparison.Ordinal);
        Assert.Contains("okf-vault", agentsMd, StringComparison.Ordinal);

        var claudeMd = File.ReadAllText(Path.Combine(project, "CLAUDE.md"));
        Assert.Equal(OkfAgentPointer.ClaudeMdContent, claudeMd);
    }

    [Fact]
    public void NoAgentsMdSkipsBothPointerFiles()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("no-pointer");

        var run = CliHarness.RunIn(project, home.Root, "init", "--no-agents-md");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.DoesNotContain("AGENTS.md", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("CLAUDE.md", run.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(project, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(project, "CLAUDE.md")));
    }

    [Fact]
    public void APersonalVaultNeverWritesAgentsMdOrClaudeMdIntoTheHomeDirectory()
    {
        // The personal vault's parent is the home directory (decisions.md §6): writing a
        // context pointer there would drop AGENTS.md at ~, which is not a project.
        using var home = new TempTree();
        var elsewhere = Path.Combine(home.Root, "mine");

        var run = CliHarness.Run(CliHarness.Environment(home.Root, home.Root, okfHome: elsewhere), "init", "--personal");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.DoesNotContain("AGENTS.md", run.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(home.Root, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(home.Root, "CLAUDE.md")));
    }

    [Fact]
    public void ASecondInitRunSplicesTheFenceRatherThanReportingCreated()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("respliced");
        CliHarness.RunIn(project, home.Root, "init");

        // Simulate the block having gone stale, exactly as an older okf would have left it.
        var agentsMd = Path.Combine(project, "AGENTS.md");
        File.WriteAllText(
            agentsMd,
            File.ReadAllText(agentsMd).Replace("okf-vault", "some-old-skill-name", StringComparison.Ordinal));

        var run = CliHarness.RunIn(project, home.Root, "init");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("AGENTS.md: updated", run.Output, StringComparison.Ordinal);
        Assert.Contains("CLAUDE.md: skipped (exists)", run.Output, StringComparison.Ordinal);
        Assert.Contains("okf-vault", File.ReadAllText(agentsMd), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsReachableFromTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf init [path]", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--nope")]
    [InlineData("--name")]
    public void AMalformedCommandLineIsAUsageFailure(string argument)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "init", argument);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void ASecondPathIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "init", "one", "two");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("at most one path", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonalAndAPathTogetherAreAUsageFailure()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "init", ".", "--personal");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("cannot be combined", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(home.Root, "okf")));
    }

    [Fact]
    public void TheHappyPathScaffoldsAVaultThatLintsCleanAndHasNoIndexDrift()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");

        var init = CliHarness.RunIn(project, home.Root, "init");
        var lint = CliHarness.RunIn(project, home.Root, "lint", "okf");
        var index = CliHarness.RunIn(project, home.Root, "index", "--check", "okf");

        Assert.Equal(CliApplication.ExitSuccess, init.ExitCode);
        Assert.Contains("okf/bundles/widgets/about-this-bundle.md: created", init.Output, StringComparison.Ordinal);
        Assert.Contains("10 files written, 0 already present", init.Output, StringComparison.Ordinal);

        Assert.Equal(CliApplication.ExitSuccess, lint.ExitCode);
        Assert.Contains("0 errors, 0 warnings, 0 infos.", lint.Output, StringComparison.Ordinal);
        Assert.Empty(lint.DiagnosticLines);

        Assert.Equal(CliApplication.ExitSuccess, index.ExitCode);
        Assert.Contains("0 drifted.", index.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBundleNameCanBeGiven()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");

        var run = CliHarness.RunIn(project, home.Root, "init", "--name", "gadgets");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(project, "okf", "bundles", "gadgets", "index.md")));
        Assert.False(Directory.Exists(Path.Combine(project, "okf", "bundles", "widgets")));
    }

    [Fact]
    public void TheBundleNameCanBeGivenInline()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");

        var run = CliHarness.RunIn(project, home.Root, "init", "--name=gadgets");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(project, "okf", "bundles", "gadgets", "index.md")));
    }

    [Fact]
    public void APersonalVaultsBundleCanStillBeNamed()
    {
        using var home = new TempTree();
        var elsewhere = Path.Combine(home.Root, "mine");

        var run = CliHarness.Run(CliHarness.Environment(home.Root, home.Root, okfHome: elsewhere), "init", "--personal", "--name", "notes");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(elsewhere, "bundles", "notes", "index.md")));
        Assert.False(Directory.Exists(Path.Combine(elsewhere, "bundles", "personal")));
    }

    [Fact]
    public void ResolutionIsReportedOnlyWhenAskedFor()
    {
        using var home = new TempTree();
        var quiet = CliHarness.RunIn(home.CreateDirectory("one"), home.Root, "init");
        var loud = CliHarness.RunIn(home.CreateDirectory("two"), home.Root, "init", "--verbose");

        Assert.Empty(quiet.Error);
        Assert.Contains("okf: vault ", loud.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondRunReportsWhatExistsAndWritesNothing()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        CliHarness.RunIn(project, home.Root, "init");
        var before = Snapshot(Path.Combine(project, "okf"));

        var run = CliHarness.RunIn(project, home.Root, "init");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("is already initialized: 10 files already present, 0 written", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(": created", run.Output, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(Path.Combine(project, "okf")));
    }

    [Fact]
    public void PointingInitAtAnInitializedVaultIsAlsoANoOp()
    {
        // A directory that already holds bundles/ IS the vault, so `okf init okf/` fills
        // the vault in rather than nesting a second one at okf/okf/.
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        CliHarness.RunIn(project, home.Root, "init");

        var run = CliHarness.RunIn(project, home.Root, "init", "okf");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("already initialized", run.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(project, "okf", "okf")));
    }

    [Fact]
    public void InitializingInsideABundleRootIsRefusedAndWritesNothing()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        CliHarness.RunIn(project, home.Root, "init");
        var bundleRoot = Path.Combine(project, "okf", "bundles", "widgets");
        var before = Snapshot(Path.Combine(project, "okf"));

        var run = CliHarness.RunIn(bundleRoot, home.Root, "init");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Refusing to initialize", run.Error, StringComparison.Ordinal);
        Assert.Contains("§11 conformance", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
        Assert.Equal(before, Snapshot(Path.Combine(project, "okf")));
    }

    [Fact]
    public void ADifferentNameOnAnInitializedVaultAddsASecondBundle()
    {
        // The vault-level files are already there and are left alone; what a second
        // `--name` adds is a second bundle, complete and lint-clean, beside the first.
        // Deliberate: `bundles/` is plural, and refusing here would mean the only way to
        // start a second bundle in a vault is by hand.
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        CliHarness.RunIn(project, home.Root, "init");
        var readme = File.ReadAllText(Path.Combine(project, "okf", "README.md"));

        var run = CliHarness.RunIn(project, home.Root, "init", "--name", "gadgets");
        var lint = CliHarness.RunIn(project, home.Root, "lint", "okf");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("3 files written, 7 already present", run.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project, "okf", "bundles", "widgets", "index.md")));
        Assert.True(File.Exists(Path.Combine(project, "okf", "bundles", "gadgets", "index.md")));

        // The first bundle's vault-level files still describe the first bundle: nothing
        // was rewritten to mention the newcomer.
        Assert.Equal(readme, File.ReadAllText(Path.Combine(project, "okf", "README.md")));

        Assert.Equal(CliApplication.ExitSuccess, lint.ExitCode);
        Assert.Contains("2 bundles", lint.Output, StringComparison.Ordinal);
        Assert.Empty(lint.DiagnosticLines);
    }

    [SkippableFact]
    public void ASymlinkIntoABundleRootIsRefusedWithTheUsageExitCode()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        CliHarness.RunIn(project, home.Root, "init");
        var bundleRoot = Path.Combine(project, "okf", "bundles", "widgets");
        var link = Path.Combine(home.Root, "shortcut");

        try
        {
            Directory.CreateSymbolicLink(link, bundleRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SkipException($"This platform will not create directory symlinks: {exception.Message}");
        }

        var before = Snapshot(Path.Combine(project, "okf"));
        var run = CliHarness.RunIn(home.Root, home.Root, "init", "shortcut");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Refusing to initialize", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
        Assert.Equal(before, Snapshot(Path.Combine(project, "okf")));
    }

    [Fact]
    public void PersonalTargetsOkfHomeAndScaffoldsTheSameShape()
    {
        using var home = new TempTree();
        var elsewhere = Path.Combine(home.Root, "vaults", "mine");
        var environment = CliHarness.Environment(home.Root, home.Root, okfHome: elsewhere);

        var init = CliHarness.Run(environment, "init", "--personal", "--verbose");
        var lint = CliHarness.Run(environment, "lint");

        Assert.Equal(CliApplication.ExitSuccess, init.ExitCode);
        Assert.Contains($"okf: personal vault '{elsewhere}' (OKF_HOME, else ~/okf)", init.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(elsewhere, "bundles", "personal", "index.md")));
        Assert.True(File.Exists(Path.Combine(elsewhere, "raw", "manifest.json")));

        // `okf lint` with no path falls through to the personal vault, so the freshly
        // initialized one is discoverable by exactly the route decisions.md §6 describes.
        Assert.Equal(CliApplication.ExitSuccess, lint.ExitCode);
        Assert.Contains("0 errors, 0 warnings, 0 infos.", lint.Output, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void TheScaffoldedVaultMatchesThisRepositorysHandBuiltOne()
    {
        // PRD ACC-5's vault was built by hand, deliberately before `okf init` existed, to
        // pressure-test the conventions while they were cheap to change. It is therefore
        // the specification this command has to reproduce — structurally, not word for
        // word: the file set, the frontmatter keys, and the config's shape.
        var dogfood = Repository.DogfoodVault;
        Skip.If(dogfood is null || !Directory.Exists(dogfood), "The dogfood vault 'okf/' is not present.");

        using var home = new TempTree();
        var project = home.CreateDirectory("okf-net");
        Assert.Equal(CliApplication.ExitSuccess, CliHarness.RunIn(project, home.Root, "init").ExitCode);
        var scaffolded = Path.Combine(project, "okf");

        // 1. Every file init writes has a counterpart in the hand-built vault. The
        //    dogfood vault holds more — nineteen concepts, a capture, a python check —
        //    but nothing init produces may be absent from it.
        var missing = Relative(scaffolded)
            .Where(path => !File.Exists(Path.Combine(dogfood!, path)))
            .ToArray();
        Assert.Empty(missing);

        // 2. The first concept carries the same frontmatter keys, less the ones only a
        //    written-on concept can have: the dogfood bundle's about-this-bundle.md cites
        //    two sources, and a bundle scaffolded five seconds ago cites nothing.
        var scaffoldedKeys = FrontmatterKeys(Path.Combine(scaffolded, "bundles", "okf-net", "about-this-bundle.md"));
        var dogfoodKeys = FrontmatterKeys(Path.Combine(dogfood!, "bundles", "okf-net", "about-this-bundle.md"));

        Assert.Equal(["description", "generated", "tags", "title", "type"], scaffoldedKeys);
        Assert.Empty(scaffoldedKeys.Except(dogfoodKeys, StringComparer.Ordinal));

        // 3. The bundle-root index carries §12's version declaration and okf-net's
        //    generated marker in both.
        foreach (var index in (string[])[
            Path.Combine(scaffolded, "bundles", "okf-net", "index.md"),
            Path.Combine(dogfood!, "bundles", "okf-net", "index.md")])
        {
            var text = File.ReadAllText(index);
            Assert.Contains(OkfIndexGenerator.RootFrontmatter, text, StringComparison.Ordinal);
            Assert.True(OkfIndexGenerator.IsGenerated(text));
        }

        // 4. Every rule init promotes is promoted at least as far in the hand-built vault,
        //    and both carry a tag registry.
        var scaffoldedConfig = OkfConfig.Load(Path.Combine(scaffolded, OkfDiscovery.ConfigFileName));
        var dogfoodConfig = OkfConfig.Load(Path.Combine(dogfood!, OkfDiscovery.ConfigFileName));

        foreach (var (rule, severity) in scaffoldedConfig.Severities.Severities)
        {
            Assert.True(
                dogfoodConfig.Severities.Severities.TryGetValue(rule, out var theirs) && theirs >= severity,
                $"{rule} is {severity.ToConfigString()} in a scaffolded vault; the dogfood vault has " +
                $"{(dogfoodConfig.Severities.Severities.TryGetValue(rule, out var found) ? found.ToConfigString() : "no setting")}.");
        }

        Assert.NotNull(scaffoldedConfig.TagRegistry);
        Assert.NotNull(dogfoodConfig.TagRegistry);
    }

    /// <summary>Every file in a vault, as vault-relative paths with <c>/</c> separators.</summary>
    private static string[] Relative(string vault) =>
        [.. Directory.EnumerateFiles(vault, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(vault, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>A concept's top-level frontmatter keys, sorted.</summary>
    private static string[] FrontmatterKeys(string path) =>
        [.. OkfDocument.Parse(File.ReadAllText(path)).Frontmatter.Entries
            .Select(entry => entry.Key)
            .OfType<OkfScalar>()
            .Select(key => key.Value)
            .Order(StringComparer.Ordinal)];

    /// <summary>Path and bytes of every file in a tree, so a test can prove a run wrote nothing.</summary>
    private static string Snapshot(string root) =>
        string.Join(
            "\n",
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(file =>
                    $"{file}|{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))}"));
}
