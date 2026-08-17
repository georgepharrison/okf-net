using Okf.Core;

namespace Okf.Cli.Skills;

/// <summary>
/// <c>okf skills list|path|install</c> — the skills that ship inside the binary, where
/// they are on this machine, and how they get there (work item #41).
/// </summary>
/// <remarks>
/// Every form is non-interactive and asks nothing: the installers call
/// <c>okf skills install</c> after placing the binary, and a command that stopped for a
/// prompt inside `curl … | sh` would hang the install. The interactive first-run
/// walkthrough is issue #50.
/// </remarks>
internal static class SkillsCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>skills</c>.</param>
    /// <param name="environment">The environment targets resolve against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        SkillsArguments parsed;
        try
        {
            parsed = SkillsArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf skills --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return parsed.Action switch
            {
                SkillsAction.Path => WritePath(parsed, environment, output, error),
                SkillsAction.Install => Install(parsed, environment, output, error),
                _ => WriteList(parsed, output),
            };
        }
        catch (IOException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
        catch (UnauthorizedAccessException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
    }

    private static int WriteList(SkillsArguments arguments, TextWriter output)
    {
        if (arguments.Names)
        {
            foreach (string name in OkfSkills.Names)
            {
                output.WriteLine(name);
            }

            return CliApplication.ExitSuccess;
        }

        int width = OkfSkills.Names.Max(name => name.Length);
        foreach (OkfSkill skill in OkfSkills.All)
        {
            output.WriteLine($"{skill.Name.PadRight(width)}  {skill.Description}");
        }

        return CliApplication.ExitSuccess;
    }

    private static int WritePath(
        SkillsArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        string name = arguments.SkillName!;
        if (OkfSkills.Find(name) is null)
        {
            error.WriteLine(
                $"okf: error: no skill named '{name}'; this binary carries " +
                $"{string.Join(", ", OkfSkills.Names)}.");
            return CliApplication.ExitUsage;
        }

        if (OkfSkillInstaller.Locate(name, environment) is { } path)
        {
            output.WriteLine(path);
            return CliApplication.ExitSuccess;
        }

        // Exit 1 rather than 2: the question was well formed and the answer is that the
        // skill is not installed, which is a finding about this machine and not a usage
        // failure. A caller can branch on it; a person gets the command that fixes it.
        error.WriteLine($"okf: error: '{name}' is not installed; run `okf skills install`.");
        return CliApplication.ExitDiagnostics;
    }

    private static int Install(
        SkillsArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        IReadOnlyList<OkfSkillInstallFile> files;
        try
        {
            files = OkfSkillInstaller.Install(
                environment,
                new OkfSkillInstallOptions
                {
                    Hosts = arguments.Hosts,
                    Scope = arguments.Scope,
                    Directory = arguments.Directory,
                    Force = arguments.Force,
                });
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }

        foreach (OkfSkillInstallFile file in files)
        {
            output.WriteLine(
                $"{DiagnosticWriter.Display(file.Path, environment.CurrentDirectory)}: {Verb(file.Status)}");
        }

        int written = files.Count(file => file.Status == OkfSkillInstallStatus.Written);
        int skipped = files.Count(file => file.Status == OkfSkillInstallStatus.SkippedModified);

        output.WriteLine(
            $"{DiagnosticWriter.Plural(OkfSkills.All.Count, "skill")} in " +
            $"{DiagnosticWriter.Plural(files.Count, "file")}: " +
            $"{written.ToString(System.Globalization.CultureInfo.InvariantCulture)} written, " +
            $"{(files.Count - written - skipped).ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            "unchanged, " +
            $"{skipped.ToString(System.Globalization.CultureInfo.InvariantCulture)} skipped.");

        if (skipped > 0)
        {
            // Exit 0: a skill somebody edited is that person's copy, and reporting it as a
            // failed install would make every installer run on that machine warn forever.
            output.WriteLine("Skipped files differ from the ones this okf carries; `--force` overwrites them.");
        }

        // The context pointer belongs beside a *project*'s own copy of the skills — a
        // user-scoped install has no project root to write AGENTS.md into.
        if (arguments.Scope == OkfSkillScope.Project && !arguments.NoAgentsMd)
        {
            AgentPointerReport.Write(environment.CurrentDirectory, environment, output);
        }

        return CliApplication.ExitSuccess;
    }

    private static string Verb(OkfSkillInstallStatus status) => status switch
    {
        OkfSkillInstallStatus.Written => "written",
        OkfSkillInstallStatus.Unchanged => "unchanged",
        _ => "skipped (modified)",
    };

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf skills <list|path|install> [options]

            The agent skills ship inside this binary, so installing them needs no network
            and no second download. Every form is non-interactive.

            Subcommands:
              list                          The skills this binary carries, with the
                                            one-line description a host preloads
                                            (--names for bare names, one per line)
              path <name>                   Where that skill is installed on this machine
                                            (exit 1 when it is not)
              install                       Write the skills onto this machine

            Options (list):
              --names                       Bare names, one per line, for a script to read
                                            (this is what the shell completions call)

            Options (install):
              --host <claude|pi|generic|all>
                                            Where to write. Omitted: okf's own data
                                            directory, plus Claude Code and pi when this
                                            machine already has them. `generic` needs --dir.
              --dir <path>                  Write into this directory as well (or only,
                                            when --host is omitted)
              --scope <user|project>        Whether a host target is the user's home or the
                                            working directory (default: user)
              --force                       Overwrite a file whose bytes differ
              --no-agents-md                With --scope project, skip writing the
                                            AGENTS.md / CLAUDE.md context pointer
              --help, -h                    Show this help

            Where the skills land:
              data       $XDG_DATA_HOME/okf/skills (else ~/.local/share/okf/skills;
                         %LOCALAPPDATA%\okf\skills on Windows)
              claude     ~/.claude/skills, or .claude/skills with --scope project
              pi         ~/.pi/agent/skills, or .pi/agent/skills with --scope project

            A file whose bytes differ from this binary's copy is never overwritten without
            --force; it is reported as `skipped (modified)` and the run still exits 0.

            Exit codes:
              0  the skills are installed, whether this run wrote anything or not
              1  `path` was asked about a skill that is not installed
              2  usage failure
            """);
    }
}
