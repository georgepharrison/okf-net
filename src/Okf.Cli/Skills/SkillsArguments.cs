using Okf.Core;

namespace Okf.Cli.Skills;

/// <summary>Which <c>okf skills</c> subcommand was asked for.</summary>
internal enum SkillsAction
{
    /// <summary>List the skills the binary carries.</summary>
    List = 0,

    /// <summary>Print where one skill is installed on this machine.</summary>
    Path,

    /// <summary>Write the skills onto this machine.</summary>
    Install,
}

/// <summary>The parsed form of <c>okf skills</c>'s command line.</summary>
internal sealed class SkillsArguments
{
    private SkillsArguments()
    {
    }

    /// <summary>The subcommand.</summary>
    public SkillsAction Action { get; private set; }

    /// <summary>The skill named by <c>okf skills path</c>.</summary>
    public string? SkillName { get; private set; }

    /// <summary>
    /// The hosts <c>--host</c> named, empty when it was not given — which is the
    /// auto-detecting default, not "no hosts".
    /// </summary>
    public IReadOnlyList<OkfSkillHost> Hosts { get; private set; } = [];

    /// <summary>The directory <c>--dir</c> named.</summary>
    public string? Directory { get; private set; }

    /// <summary>Whether host targets are the user's or the project's.</summary>
    public OkfSkillScope Scope { get; private set; } = OkfSkillScope.User;

    /// <summary>Whether a file whose bytes differ is overwritten.</summary>
    public bool Force { get; private set; }

    /// <summary>
    /// Whether to skip writing the <c>AGENTS.md</c> / <c>CLAUDE.md</c> context pointer
    /// (<c>--no-agents-md</c>). Written by default, and only when <c>--scope project</c>.
    /// </summary>
    public bool NoAgentsMd { get; private set; }

    /// <summary>
    /// Whether <c>list</c> prints bare names, one per line — the form a completion script
    /// reads (issue #51). It answers from the binary, so it needs no vault and touches no
    /// file.
    /// </summary>
    public bool Names { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
        ["--help", "-h", "--force", "--names", "--host", "--dir", "--scope", "--no-agents-md"];

    /// <summary>Parses <c>okf skills</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>skills</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static SkillsArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        SkillsArguments parsed = new SkillsArguments();

        int start = parsed.TakeAction(args);
        for (int index = start; index < args.Length; index++)
        {
            string argument = args[index];
            (string name, string? inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--force":
                    parsed.Force = true;
                    break;

                case "--no-agents-md":
                    parsed.NoAgentsMd = true;
                    break;
                case "--names":
                    parsed.Names = true;
                    break;

                case "--host":
                    parsed.TakeHosts(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--dir":
                    parsed.TakeDirectory(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--scope":
                    parsed.TakeScope(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                default:
                    parsed.TakeSkillName(argument);
                    break;
            }
        }

        parsed.Validate();
        return parsed;
    }

    private int TakeAction(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            return 0;
        }

        Action = args[0] switch
        {
            "list" => SkillsAction.List,
            "path" => SkillsAction.Path,
            "install" => SkillsAction.Install,
            _ => throw new OkfConfigException(
                $"Unknown subcommand '{args[0]}'; expected 'list', 'path' or 'install'."),
        };
        return 1;
    }

    private void TakeHosts(string value)
    {
        if (Hosts.Count > 0)
        {
            throw new OkfConfigException("Option '--host' was given more than once.");
        }

        Hosts = ParseHosts(value);
    }

    private void TakeDirectory(string value)
    {
        if (Directory is not null)
        {
            throw new OkfConfigException("Option '--dir' was given more than once.");
        }

        Directory = value;
    }

    private void TakeScope(string value)
    {
        Scope = value switch
        {
            "user" => OkfSkillScope.User,
            "project" => OkfSkillScope.Project,
            _ => throw new OkfConfigException(
                $"Unknown --scope value '{value}'; expected 'user' or 'project'."),
        };
    }

    private void TakeSkillName(string argument)
    {
        if (argument.StartsWith('-') && argument.Length > 1)
        {
            throw new OkfConfigException($"Unknown option '{argument}'.");
        }

        if (SkillName is not null)
        {
            throw new OkfConfigException(
                $"`okf skills {Verb(Action)}` takes at most one skill name; " +
                $"got '{SkillName}' and '{argument}'.");
        }

        SkillName = argument;
    }

    private void Validate()
    {
        if (ShowHelp)
        {
            return;
        }

        RefuseNamesOnNonList();
        RequirePathName();
        IncludeDirectoryHost();
    }

    /// <summary>
    /// `--names` names a rendering of the list and nothing else. Accepting it on
    /// `install` would mean accepting a flag that changes nothing, which is how a flag
    /// ends up meaning two things later.
    /// </summary>
    private void RefuseNamesOnNonList()
    {
        if (Names && Action != SkillsAction.List)
        {
            throw new OkfConfigException(
                $"`--names` belongs to `okf skills list`; `okf skills {Verb(Action)}` does not take it.");
        }
    }

    private void RequirePathName()
    {
        if (Action == SkillsAction.Path && SkillName is null)
        {
            throw new OkfConfigException(
                $"`okf skills path` needs a skill name: one of {string.Join(", ", OkfSkills.Names)}.");
        }

        if (Action != SkillsAction.Path && SkillName is not null)
        {
            throw new OkfConfigException(
                $"`okf skills {Verb(Action)}` takes no skill name; got '{SkillName}'.");
        }
    }

    /// <summary>
    /// `--dir` IS the generic host: naming a directory and no host means that
    /// directory and nothing else, and naming one beside `--host claude` adds it to
    /// that host rather than being ignored.
    /// </summary>
    private void IncludeDirectoryHost()
    {
        if (Directory is not null && !Hosts.Contains(OkfSkillHost.Directory))
        {
            Hosts = [.. Hosts, OkfSkillHost.Directory];
        }

        if (Hosts.Contains(OkfSkillHost.Directory) && Directory is null)
        {
            throw new OkfConfigException("`--host generic` needs `--dir <path>` to write into.");
        }
    }

    /// <summary>The subcommand's name, as it is spelled on the command line.</summary>
    /// <param name="action">The subcommand.</param>
    /// <returns>The verb.</returns>
    public static string Verb(SkillsAction action) => action switch
    {
        SkillsAction.Path => "path",
        SkillsAction.Install => "install",
        _ => "list",
    };

    private static IReadOnlyList<OkfSkillHost> ParseHosts(string value) => value switch
    {
        "claude" => [OkfSkillHost.Claude],
        "pi" => [OkfSkillHost.Pi],
        "generic" => [OkfSkillHost.Directory],

        // `all` is every host okf-net knows, whether or not this machine shows one — the
        // way to install for a host that is not installed yet.
        "all" => [OkfSkillHost.Data, OkfSkillHost.Claude, OkfSkillHost.Pi],
        _ => throw new OkfConfigException(
            $"Unknown --host value '{value}'; expected 'claude', 'pi', 'generic' or 'all'."),
    };
}
