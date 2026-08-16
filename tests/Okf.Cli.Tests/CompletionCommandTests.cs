using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Okf.Cli.Tests;

/// <summary>
/// <c>okf completion</c> and the table it renders from (issue #51).
/// </summary>
/// <remarks>
/// The two tests that matter are the ones that stop the table from drifting: the verb set
/// must equal <see cref="CliApplication.Verbs" />, which is the array the dispatcher reads,
/// and each verb's flags must equal the <c>Flags</c> array its parser declares — which is
/// itself checked against the parser's own <c>case</c> labels. AD-44: the golden files were
/// produced by the generator and then read by hand against each shell's documented syntax,
/// and every shell check below runs the real shell over the real output rather than
/// asserting a substring of it.
/// </remarks>
public class CompletionCommandTests
{
    /// <summary>
    /// The <c>Flags</c> array each verb's parser declares. A verb with no entry here fails
    /// <see cref="EveryVerbDeclaresItsFlags" />, so a new verb cannot arrive without one.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> ParserFlags =
        new(StringComparer.Ordinal)
        {
            ["init"] = InitArguments.Flags,
            ["lint"] = LintArguments.Flags,
            ["index"] = IndexArguments.Flags,
            ["search"] = SearchArguments.Flags,
            ["register"] = RegistryArguments.Flags,
            ["unregister"] = RegistryArguments.Flags,
            ["registry"] = RegistryArguments.Flags,
            ["inbox"] = InboxArguments.Flags,
            ["verify"] = VerifyArguments.Flags,
            ["capture"] = CaptureArguments.Flags,
            ["generated"] = GeneratedArguments.Flags,
            ["bundle"] = BundleArguments.Flags,
            ["site"] = SiteArguments.Flags,
            ["skills"] = SkillsArguments.Flags,
            ["mcp"] = McpCommand.Flags,
            ["completion"] = CompletionCommand.Flags,
            ["help"] = CliApplication.HelpFlags,
            ["version"] = CliApplication.VersionFlags,
        };

    /// <summary>The source file whose <c>case</c> labels are each verb's parser.</summary>
    private static readonly Dictionary<string, string> ParserSource =
        new(StringComparer.Ordinal)
        {
            ["init"] = "InitArguments.cs",
            ["lint"] = "LintArguments.cs",
            ["index"] = "IndexArguments.cs",
            ["search"] = "SearchArguments.cs",
            ["registry"] = "RegistryArguments.cs",
            ["inbox"] = "InboxArguments.cs",
            ["verify"] = "VerifyArguments.cs",
            ["capture"] = "CaptureArguments.cs",
            ["generated"] = "GeneratedArguments.cs",
            ["bundle"] = "BundleArguments.cs",
            ["site"] = "SiteArguments.cs",
            ["skills"] = "SkillsArguments.cs",
            ["mcp"] = "McpCommand.cs",
            ["completion"] = "CompletionCommand.cs",
        };

    [Fact]
    public void TheTableCoversExactlyTheVerbsTheCliDispatches()
    {
        Assert.Equal(
            CliApplication.Verbs.OrderBy(verb => verb, StringComparer.Ordinal),
            CompletionTable.Names.OrderBy(verb => verb, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryVerbDeclaresItsFlags() =>
        Assert.DoesNotContain(CompletionTable.Names, verb => !ParserFlags.ContainsKey(verb));

    [Fact]
    public void TheTableCoversExactlyTheFlagsEachParserAccepts()
    {
        foreach (var verb in CompletionTable.Verbs)
        {
            Assert.Equal(
                ParserFlags[verb.Name].OrderBy(flag => flag, StringComparer.Ordinal),
                verb.Flags.Select(flag => flag.Name).OrderBy(flag => flag, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The <c>Flags</c> arrays are only worth something if they say what the parsers do, and
    /// a parser's `case` labels are the parser. Reading them is what makes a flag added to a
    /// `switch` and nowhere else a red test rather than a completion that silently lags.
    /// </summary>
    [SkippableFact]
    public void EveryFlagArrayMatchesItsParsersCaseLabels()
    {
        Skip.If(Repository.Root is null, "the tests are running outside a checkout");
        var source = Path.Combine(Repository.Root!, "src", "Okf.Cli");

        foreach (var (verb, file) in ParserSource)
        {
            var labels = CaseLabels(File.ReadAllText(Path.Combine(source, file)));
            Assert.Equal(
                ParserFlags[verb].OrderBy(flag => flag, StringComparer.Ordinal),
                labels.OrderBy(flag => flag, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Every option spelling a C# <c>case</c> label in one parser names. The <c>=</c>
    /// spellings (`--format=json`) are the same option written inline, not another one.
    /// </summary>
    private static IReadOnlyList<string> CaseLabels(string source) =>
        [.. Regex.Matches(source, @"^\s*case .*:$", RegexOptions.Multiline, TimeSpan.FromSeconds(5))
            .SelectMany(line => Regex.Matches(line.Value, "\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Select(literal => literal.Groups[1].Value)
            .Where(literal => literal.StartsWith('-') && !literal.Contains('=', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("pwsh")]
    public void EachShellGetsAScriptOnStdout(string shell)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "completion", shell);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        Assert.Contains("okf", run.Output, StringComparison.Ordinal);
        Assert.EndsWith("\n", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A script is bytes a shell parses, and a stray non-ASCII character in a description
    /// reaches a terminal whose encoding nobody promised. It is also what makes the golden
    /// files diffable.
    /// </summary>
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("pwsh")]
    public void EachScriptIsPureAsciiAndDeterministic(string shell)
    {
        var script = CompletionCommand.Render(shell)!;

        Assert.DoesNotContain(script, character => character > '\x7e' || (character < ' ' && character != '\n'));
        Assert.Equal(script, CompletionCommand.Render(shell));
    }

    [Fact]
    public void AnUnknownShellIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "completion", "ksh");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Unknown shell 'ksh'", run.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public void NamingNoShellIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "completion");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("needs a shell", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpIsNotAScript()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "completion", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf completion <bash|zsh|fish|pwsh>", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("complete -F", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing a completion script does may read a vault (issue #51): the tab key is not a
    /// place to start a directory walk. The only command any script runs is the one that
    /// answers from the binary.
    /// </summary>
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("pwsh")]
    public void NoScriptRunsAnythingButSkillsList(string shell)
    {
        // Comments name commands for a reader to type; code is what runs.
        var code = string.Join(
            "\n",
            CompletionCommand.Render(shell)!
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith('#')));

        foreach (var verb in CliApplication.Verbs.Where(name => name != "skills"))
        {
            Assert.DoesNotContain($"okf {verb}", code, StringComparison.Ordinal);
        }

        Assert.Contains("skills list --names", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The golden files. Regenerate them with
    /// <c>OKF_UPDATE_COMPLETIONS=1 dotnet test --filter CompletionCommandTests</c> and read
    /// the diff before committing it: these bytes are what a user's shell sources.
    /// </summary>
    [SkippableTheory]
    [InlineData("bash", "okf.bash")]
    [InlineData("zsh", "okf.zsh")]
    [InlineData("fish", "okf.fish")]
    [InlineData("pwsh", "okf.ps1")]
    public void EachScriptMatchesItsGoldenFile(string shell, string file)
    {
        Skip.If(Repository.Root is null, "the tests are running outside a checkout");
        var path = Path.Combine(Repository.Root!, "tests", "Okf.Cli.Tests", "completions", file);
        var script = CompletionCommand.Render(shell)!;

        if (Environment.GetEnvironmentVariable("OKF_UPDATE_COMPLETIONS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, script);
        }

        Assert.True(File.Exists(path), $"no golden file at {path}");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), script);
    }

    [SkippableFact]
    public void TheBashScriptParses() => AssertParses("bash", "-n", "okf.bash");

    [SkippableFact]
    public void TheZshScriptParses() => AssertParses("zsh", "-n", "okf.zsh");

    [SkippableFact]
    public void TheFishScriptParses() => AssertParses("fish", "-n", "okf.fish");

    /// <summary>
    /// bash, driven the way bash drives a completion: the script is sourced, COMP_WORDS and
    /// COMP_CWORD are set the way readline sets them, <c>_okf</c> is called, and COMPREPLY
    /// is read back. This is the one shell the suite can exercise end to end without a
    /// terminal.
    /// </summary>
    [SkippableFact]
    public void BashCompletesVerbsSubcommandsAndFlagValues()
    {
        Skip.IfNot(Tooling.IsOnPath("bash"), "bash is not installed");
        using var tree = new TempTree();
        var script = tree.Write("okf.bash", CompletionCommand.Render("bash")!);

        Assert.Equal("lint", Complete(script, "okf", "li"));
        Assert.Equal("install list path", Complete(script, "okf", "skills", ""));
        Assert.Equal("json text", Complete(script, "okf", "lint", "--format", ""));
        Assert.Equal("project personal registered all", Complete(script, "okf", "search", "--scope", ""));
        Assert.Equal("bash fish pwsh zsh", Complete(script, "okf", "completion", ""));
        Assert.Equal("--help --check --format --json --verbose", Complete(script, "okf", "index", "--"));

        // The operand after a subcommand that takes none: `okf skills install` is followed
        // by nothing, so nothing is what it offers.
        Assert.Equal(string.Empty, Complete(script, "okf", "skills", "install", ""));
    }

    /// <summary>
    /// zsh, loaded the way `eval "$(okf completion zsh)"` loads it: under a real compinit,
    /// with the function defined and registered as okf's completer afterwards. Driving a
    /// completion to its answers needs a terminal, which this suite does not have.
    /// </summary>
    [SkippableFact]
    public void ZshLoadsTheScriptUnderCompinit()
    {
        Skip.IfNot(Tooling.IsOnPath("zsh"), "zsh is not installed");
        using var tree = new TempTree();
        var script = tree.Write("_okf", CompletionCommand.Render("zsh")!);
        var dump = Path.Combine(tree.Root, "zcompdump");

        var run = Shell(
            "zsh",
            "-c",
            $"autoload -U compinit; compinit -u -d {dump}; source {script}; " +
            "print -r -- \"function:${+functions[_okf]} completer:${_comps[okf]}\"");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("function:1 completer:_okf", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one dynamic answer, and the flag that makes it cheap: no vault is resolved and no
    /// directory is read, so it works in an empty directory on a machine with no okf vault.
    /// </summary>
    [Fact]
    public void SkillsListNamesPrintsOneBareNamePerLine()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "skills", "list", "--names");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(Okf.Core.OkfSkills.Names, run.OutputLines);
    }

    [Theory]
    [InlineData("install")]
    [InlineData("path")]
    public void NamesBelongsToListAlone(string action)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "skills", action, "--names");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("`--names` belongs to `okf skills list`", run.Error, StringComparison.Ordinal);
    }

    private static void AssertParses(string shell, string flag, string name)
    {
        Skip.IfNot(Tooling.IsOnPath(shell), $"{shell} is not installed");
        using var tree = new TempTree();
        var script = tree.Write(name, CompletionCommand.Render(shell == "pwsh" ? "pwsh" : shell)!);

        var run = Shell(shell, flag, script);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Error.Trim());
    }

    /// <summary>Runs one bash completion and returns COMPREPLY as a single line.</summary>
    private static string Complete(string script, params string[] words)
    {
        var array = string.Join(" ", words.Select(word => $"\"{word}\""));
        var run = Shell(
            "bash",
            "-c",
            $"source {script}; COMP_WORDS=({array}); COMP_CWORD={words.Length - 1}; _okf; " +
            "echo \"${COMPREPLY[*]}\"");

        Assert.Equal(0, run.ExitCode);
        return run.Output.Trim();
    }

    private static (int ExitCode, string Output, string Error) Shell(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
