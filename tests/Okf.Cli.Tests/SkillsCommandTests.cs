using Okf.Core;

namespace Okf.Cli.Tests;

/// <summary>
/// End-to-end <c>okf skills</c> behavior: what it lists, where it installs, what it
/// refuses to overwrite, and the exit-code contract (work item #41, PRD CLI-14).
/// </summary>
/// <remarks>
/// Every run gets a temporary home, so no case reads or writes the developer's real
/// <c>~/.claude</c>, <c>~/.pi</c> or <c>~/.local/share</c>.
/// </remarks>
public class SkillsCommandTests
{
    [Fact]
    public void HelpExitsZeroAndDescribesTheSubcommands()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "skills", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf skills <list|path|install>", run.Output, StringComparison.Ordinal);
        Assert.Contains("--host <claude|pi|generic|all>", run.Output, StringComparison.Ordinal);
        Assert.Contains("--force", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsReachableFromTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf skills <list|path|install>", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ListNamesEverySkillWithTheDescriptionAHostPreloads()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "skills", "list");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(OkfSkills.All.Count, run.OutputLines.Length);
        foreach (var (skill, line) in OkfSkills.All.Zip(run.OutputLines))
        {
            Assert.StartsWith(skill.Name, line, StringComparison.Ordinal);
            Assert.EndsWith(skill.Description, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ListIsTheDefaultSubcommand()
    {
        using var home = new TempTree();

        Assert.Equal(
            Cli.RunIn(home.Root, home.Root, "skills", "list").Output,
            Cli.RunIn(home.Root, home.Root, "skills").Output);
    }

    /// <summary>
    /// Each refusal is named by the message it produces, not merely by exiting 2: every
    /// row here is a different guard, and "okf: error:" alone would pass whichever of them
    /// fired.
    /// </summary>
    [Theory]
    [InlineData(new[] { "wat" }, "Unknown subcommand 'wat'; expected 'list', 'path' or 'install'.")]
    [InlineData(new[] { "install", "--host", "codex" }, "Unknown --host value 'codex'")]
    [InlineData(new[] { "install", "--scope", "everywhere" }, "Unknown --scope value 'everywhere'")]
    [InlineData(new[] { "install", "--host", "generic" }, "`--host generic` needs `--dir <path>` to write into.")]
    [InlineData(new[] { "install", "okf-capture" }, "`okf skills install` takes no skill name; got 'okf-capture'.")]
    [InlineData(new[] { "path" }, "`okf skills path` needs a skill name")]
    [InlineData(new[] { "install", "--names" }, "`--names` belongs to `okf skills list`")]
    [InlineData(new[] { "path", "okf-vault", "--names" }, "`--names` belongs to `okf skills list`")]
    public void AMalformedCommandLineIsAUsageFailure(string[] arguments, string expected)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, ["skills", .. arguments]);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    /// <summary>
    /// `--help` short-circuits every one of those checks: asking what a command line means
    /// must not require it to be a legal one.
    /// </summary>
    [Theory]
    [InlineData("install", "--names")]
    [InlineData("path")]
    [InlineData("install", "--host", "generic")]
    public void HelpIsAnsweredEvenForACommandLineThatWouldOtherwiseBeRefused(params string[] arguments)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, ["skills", .. arguments, "--help"]);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf skills", run.Output, StringComparison.Ordinal);
        Assert.Empty(run.Error);
    }

    [Fact]
    public void PathExitsOneAndNamesTheCommandThatFixesItWhenNothingIsInstalled()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "skills", "path", "okf-capture");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("okf skills install", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void PathOfAnUnknownSkillIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "skills", "path", "okf-nonesuch");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no skill named 'okf-nonesuch'", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallWritesTheDataCopyAndPathThenFindsIt()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("project");

        var install = Cli.RunIn(project, home.Root, "skills", "install");
        Assert.Equal(CliApplication.ExitSuccess, install.ExitCode);

        var expected = Path.Combine(home.Root, ".local", "share", "okf", "skills", "okf-capture", "SKILL.md");
        Assert.True(File.Exists(expected));

        var path = Cli.RunIn(project, home.Root, "skills", "path", "okf-capture");
        Assert.Equal(CliApplication.ExitSuccess, path.ExitCode);
        Assert.Equal(expected, path.OutputLines.Single());
    }

    [Fact]
    public void EveryFileIsReportedOnItsOwnLine()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "skills", "install");

        Assert.Equal(OkfSkills.All.Count, run.OutputLines.Count(line => line.EndsWith(": written", StringComparison.Ordinal)));
        Assert.Contains(
            $"{OkfSkills.All.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} skills " +
            $"in {OkfSkills.All.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} files: " +
            $"{OkfSkills.All.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} written, " +
            "0 unchanged, 0 skipped.",
            run.OutputLines);
    }

    [Fact]
    public void ASecondInstallReportsEveryFileUnchangedAndExitsZero()
    {
        using var home = new TempTree();
        Cli.RunIn(home.Root, home.Root, "skills", "install");
        var run = Cli.RunIn(home.Root, home.Root, "skills", "install");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(
            OkfSkills.All.Count,
            run.OutputLines.Count(line => line.EndsWith(": unchanged", StringComparison.Ordinal)));
    }

    [Fact]
    public void InstallIsByteIdenticalBetweenRuns()
    {
        using var first = new TempTree();
        using var second = new TempTree();
        Cli.RunIn(first.Root, first.Root, "skills", "install", "--dir", "out");
        Cli.RunIn(second.Root, second.Root, "skills", "install", "--dir", "out");

        foreach (var name in OkfSkills.Names)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first.Root, "out", name, "SKILL.md")),
                File.ReadAllBytes(Path.Combine(second.Root, "out", name, "SKILL.md")));
        }
    }

    [Fact]
    public void AnEditedSkillIsSkippedAndTheRunStillExitsZero()
    {
        using var home = new TempTree();
        Cli.RunIn(home.Root, home.Root, "skills", "install");
        var edited = Path.Combine(home.Root, ".local", "share", "okf", "skills", "okf-vault", "SKILL.md");
        File.WriteAllText(edited, "mine now\n");

        var run = Cli.RunIn(home.Root, home.Root, "skills", "install");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(run.OutputLines, line => line.EndsWith(": skipped (modified)", StringComparison.Ordinal));
        Assert.Contains("`--force` overwrites them", run.Output, StringComparison.Ordinal);
        Assert.Equal("mine now\n", File.ReadAllText(edited));
    }

    [Fact]
    public void ForceOverwritesTheEditedSkill()
    {
        using var home = new TempTree();
        Cli.RunIn(home.Root, home.Root, "skills", "install");
        var edited = Path.Combine(home.Root, ".local", "share", "okf", "skills", "okf-vault", "SKILL.md");
        File.WriteAllText(edited, "mine now\n");

        var run = Cli.RunIn(home.Root, home.Root, "skills", "install", "--force");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(OkfSkills.Find("okf-vault")!.Content, File.ReadAllText(edited));
    }

    [Fact]
    public void ADetectedHostIsInstalledIntoAndAnAbsentOneIsNotCreated()
    {
        using var home = new TempTree();
        home.CreateDirectory(".claude");

        var run = Cli.RunIn(home.Root, home.Root, "skills", "install");

        Assert.True(File.Exists(Path.Combine(home.Root, ".claude", "skills", "okf-vault", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(home.Root, ".pi")));
        Assert.Equal(OkfSkills.All.Count * 2, run.OutputLines.Count(line => line.EndsWith(": written", StringComparison.Ordinal)));
    }

    [Fact]
    public void HostAllWritesEveryHostWhetherOrNotTheMachineShowsIt()
    {
        using var home = new TempTree();
        Cli.RunIn(home.Root, home.Root, "skills", "install", "--host", "all");

        Assert.True(File.Exists(Path.Combine(home.Root, ".claude", "skills", "okf-capture", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(home.Root, ".pi", "agent", "skills", "okf-capture", "SKILL.md")));
    }

    [Fact]
    public void HostClaudeWritesThatHostAndNothingElse()
    {
        using var home = new TempTree();
        Cli.RunIn(home.Root, home.Root, "skills", "install", "--host", "claude");

        Assert.True(File.Exists(Path.Combine(home.Root, ".claude", "skills", "okf-capture", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(home.Root, ".local", "share", "okf")));
    }

    [Fact]
    public void ProjectScopeWritesBesideTheProject()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("project");

        Cli.RunIn(project, home.Root, "skills", "install", "--host", "claude", "--scope", "project");

        Assert.True(File.Exists(Path.Combine(project, ".claude", "skills", "okf-capture", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(home.Root, ".claude")));
    }

    [Fact]
    public void ProjectScopeAlsoWritesTheAgentsMdContextPointer()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("pointer-project");

        var run = Cli.RunIn(project, home.Root, "skills", "install", "--scope", "project");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("AGENTS.md: created", run.Output, StringComparison.Ordinal);
        Assert.Contains("CLAUDE.md: created", run.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project, "AGENTS.md")));
        Assert.True(File.Exists(Path.Combine(project, "CLAUDE.md")));
    }

    [Fact]
    public void UserScopeNeverWritesTheAgentsMdContextPointer()
    {
        using var home = new TempTree();

        var run = Cli.RunIn(home.Root, home.Root, "skills", "install");

        Assert.DoesNotContain("AGENTS.md", run.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(home.Root, "AGENTS.md")));
    }

    [Fact]
    public void AnAgentsMdCarryingTwoFencesIsReportedAndLeftAlone()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("two-fences");
        var agentsMd = Path.Combine(project, "AGENTS.md");
        var before =
            "<!-- okf:begin -->\nold\n<!-- okf:end -->\n\n<!-- okf:begin -->\nolder\n<!-- okf:end -->\n";
        File.WriteAllText(agentsMd, before);

        var run = Cli.RunIn(project, home.Root, "skills", "install", "--scope", "project");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(
            "AGENTS.md: left as found (more than one okf fence; remove the extras and re-run)",
            run.Output,
            StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(agentsMd));
    }

    [Fact]
    public void NoAgentsMdSkipsThePointerUnderProjectScope()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("opted-out");

        var run = Cli.RunIn(project, home.Root, "skills", "install", "--scope", "project", "--no-agents-md");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.DoesNotContain("AGENTS.md", run.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(project, "AGENTS.md")));
    }

    [Fact]
    public void ADirectoryInstallWritesThereAndNowhereElse()
    {
        using var home = new TempTree();
        var target = home.CreateDirectory("elsewhere");

        var run = Cli.RunIn(home.Root, home.Root, "skills", "install", "--dir", target);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(target, "okf-capture", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(home.Root, ".local", "share", "okf")));
    }

    [Fact]
    public void AProjectCopyIsWhatPathReportsEvenWhenTheUserLevelOneExists()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("project");
        Cli.RunIn(project, home.Root, "skills", "install");
        var vendored = home.Write("project/skills/okf-custodian/SKILL.md", "vendored\n");

        var run = Cli.RunIn(project, home.Root, "skills", "path", "okf-custodian");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(vendored, run.OutputLines.Single());
    }

    [Fact]
    public void InitInAFreshProjectWritesPointersThatSayHowToResolveThem()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("fresh");

        var init = Cli.RunIn(project, home.Root, "init");

        Assert.Equal(CliApplication.ExitSuccess, init.ExitCode);
        Assert.Contains("okf skills install", init.Output, StringComparison.Ordinal);

        var recipe = File.ReadAllText(Path.Combine(project, "okf", "custodian", "recipe.json"));
        Assert.Contains("\"resource\": \"okf skills path okf-capture\"", recipe, StringComparison.Ordinal);
        Assert.DoesNotContain("skills/okf-capture/SKILL.md", recipe, StringComparison.Ordinal);
        Assert.DoesNotContain(home.Root, recipe, StringComparison.Ordinal);
        Assert.DoesNotContain("~/", recipe, StringComparison.Ordinal);
    }

    [Fact]
    public void InitAfterAProjectScopedInstallPointsAtTheInstalledFile()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("scoped");
        Cli.RunIn(project, home.Root, "skills", "install", "--host", "claude", "--scope", "project");

        var init = Cli.RunIn(project, home.Root, "init");

        Assert.DoesNotContain("okf skills install", init.Output, StringComparison.Ordinal);

        var recipe = File.ReadAllText(Path.Combine(project, "okf", "custodian", "recipe.json"));
        Assert.Contains(
            "\"resource\": \".claude/skills/okf-capture/SKILL.md\"",
            recipe,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project, ".claude", "skills", "okf-capture", "SKILL.md")));
    }

    [Fact]
    public void EveryRecipePointerResolvesAfterAnInstallIntoTheProjectsOwnSkillsDirectory()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("vendored");
        Cli.RunIn(project, home.Root, "skills", "install", "--dir", Path.Combine(project, "skills"));

        Cli.RunIn(project, home.Root, "init");

        // The end-to-end claim #41 exists for: every `resource` a scaffolded recipe wrote
        // names a file that is really there, read back from the JSON rather than assumed.
        var recipe = File.ReadAllText(Path.Combine(project, "okf", "custodian", "recipe.json"));
        var resources = ResourcesOf(recipe);

        Assert.Equal(2, resources.Count);
        foreach (var resource in resources)
        {
            Assert.True(
                File.Exists(Path.Combine(project, resource)),
                $"the recipe points at '{resource}', which does not exist in the project");
        }
    }

    [Fact]
    public void TheDescriptorInAScaffoldedRecipeIsACommandThisCliRunsAndItResolves()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("descriptor");
        Cli.RunIn(project, home.Root, "init");

        var descriptor = ResourcesOf(File.ReadAllText(
            Path.Combine(project, "okf", "custodian", "recipe.json")))[0].Split(' ');

        // A descriptor is only honest if it is the command that answers it. Run it — the
        // binary's own name dropped, exactly as a person would type the rest — and the
        // answer has to be a path that is really there.
        Assert.Equal("okf", descriptor[0]);
        Cli.RunIn(project, home.Root, "skills", "install");
        var run = Cli.RunIn(project, home.Root, descriptor[1..]);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.True(
            File.Exists(run.OutputLines.Single()),
            $"`{string.Join(' ', descriptor)}` printed '{run.Output.Trim()}', which is not a file");
    }

    private static IReadOnlyList<string> ResourcesOf(string recipe) =>
        [.. recipe
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("\"resource\":", StringComparison.Ordinal))
            .Select(line => line.Split('"')[3])];
}
