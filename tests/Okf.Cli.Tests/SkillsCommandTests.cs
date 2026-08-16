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

    [Theory]
    [InlineData("wat")]
    [InlineData("install", "--host", "codex")]
    [InlineData("install", "--scope", "everywhere")]
    [InlineData("install", "--host", "generic")]
    [InlineData("install", "okf-capture")]
    [InlineData("path")]
    public void AMalformedCommandLineIsAUsageFailure(params string[] arguments)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, ["skills", .. arguments]);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
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
}
