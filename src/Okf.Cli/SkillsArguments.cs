using Okf.Core;

namespace Okf.Cli;

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

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Parses <c>okf skills</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>skills</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static SkillsArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var parsed = new SkillsArguments();

        var start = 0;
        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            parsed.Action = args[0] switch
            {
                "list" => SkillsAction.List,
                "path" => SkillsAction.Path,
                "install" => SkillsAction.Install,
                _ => throw new OkfConfigException(
                    $"Unknown subcommand '{args[0]}'; expected 'list', 'path' or 'install'."),
            };
            start = 1;
        }

        for (var index = start; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = CliArguments.Split(argument);

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

                case "--host":
                    if (parsed.Hosts.Count > 0)
                    {
                        throw new OkfConfigException("Option '--host' was given more than once.");
                    }

                    parsed.Hosts = ParseHosts(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--dir":
                    if (parsed.Directory is not null)
                    {
                        throw new OkfConfigException("Option '--dir' was given more than once.");
                    }

                    parsed.Directory = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                case "--scope":
                    parsed.Scope = (inlineValue ?? CliArguments.Next(args, ref index, name)) switch
                    {
                        "user" => OkfSkillScope.User,
                        "project" => OkfSkillScope.Project,
                        var value => throw new OkfConfigException(
                            $"Unknown --scope value '{value}'; expected 'user' or 'project'."),
                    };
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OkfConfigException($"Unknown option '{argument}'.");
                    }

                    if (parsed.SkillName is not null)
                    {
                        throw new OkfConfigException(
                            $"`okf skills {Verb(parsed.Action)}` takes at most one skill name; " +
                            $"got '{parsed.SkillName}' and '{argument}'.");
                    }

                    parsed.SkillName = argument;
                    break;
            }
        }

        if (parsed.ShowHelp)
        {
            return parsed;
        }

        if (parsed.Action == SkillsAction.Path && parsed.SkillName is null)
        {
            throw new OkfConfigException(
                $"`okf skills path` needs a skill name: one of {string.Join(", ", OkfSkills.Names)}.");
        }

        if (parsed.Action != SkillsAction.Path && parsed.SkillName is not null)
        {
            throw new OkfConfigException(
                $"`okf skills {Verb(parsed.Action)}` takes no skill name; got '{parsed.SkillName}'.");
        }

        // `--dir` IS the generic host: naming a directory and no host means that
        // directory and nothing else, and naming one beside `--host claude` adds it to
        // that host rather than being ignored.
        if (parsed.Directory is not null && !parsed.Hosts.Contains(OkfSkillHost.Directory))
        {
            parsed.Hosts = [.. parsed.Hosts, OkfSkillHost.Directory];
        }

        if (parsed.Hosts.Contains(OkfSkillHost.Directory) && parsed.Directory is null)
        {
            throw new OkfConfigException("`--host generic` needs `--dir <path>` to write into.");
        }

        return parsed;
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
