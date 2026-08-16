using Okf.Core;

namespace Okf.Cli;

/// <summary>What <c>okf registry</c> was asked to do.</summary>
internal enum RegistryAction
{
    /// <summary>Report every entry (the default).</summary>
    List,

    /// <summary>Remove the entries whose paths no longer exist.</summary>
    Prune,
}

/// <summary>
/// The parsed form of the three registry command lines: <c>okf register [path]</c>,
/// <c>okf unregister [path|id]</c>, and <c>okf registry list|prune</c> (PRD CLI-2).
/// </summary>
internal sealed class RegistryArguments
{
    private RegistryArguments()
    {
    }

    /// <summary>The path or id argument, or <see langword="null" /> when none was given.</summary>
    public string? Target { get; private set; }

    /// <summary>What <c>okf registry</c> should do.</summary>
    public RegistryAction Action { get; private set; } = RegistryAction.List;

    /// <summary>Whether to emit the stable JSON array instead of human-readable lines.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report the registry's location and the effective configuration.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Parses one registry command line.</summary>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="verb">The verb, for error messages.</param>
    /// <param name="subcommands">Whether a bare word names an action rather than a target.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static RegistryArguments Parse(string[] args, string verb, bool subcommands)
    {
        var parsed = new RegistryArguments();
        var actionSeen = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--verbose" or "-v":
                    parsed.Verbose = true;
                    break;

                case "--json":
                    parsed.Json = true;
                    break;

                case "--format" or "--format=json" or "--format=text":
                    var format = argument.Length > "--format".Length
                        ? argument["--format=".Length..]
                        : Next(args, ref index, "--format");
                    parsed.Json = format switch
                    {
                        "json" => true,
                        "text" => false,
                        _ => throw new OkfConfigException($"Unknown --format value '{format}'; expected 'text' or 'json'."),
                    };
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OkfConfigException($"Unknown option '{argument}'.");
                    }

                    if (subcommands && !actionSeen)
                    {
                        parsed.Action = argument switch
                        {
                            "list" => RegistryAction.List,
                            "prune" => RegistryAction.Prune,
                            _ => throw new OkfConfigException(
                                $"Unknown `okf registry` subcommand '{argument}'; expected 'list' or 'prune'."),
                        };
                        actionSeen = true;
                    }
                    else if (parsed.Target is null && !subcommands)
                    {
                        parsed.Target = argument;
                    }
                    else
                    {
                        throw new OkfConfigException(
                            $"`okf {verb}` takes at most one argument; got '{parsed.Target ?? argument}' and '{argument}'.");
                    }

                    break;
            }
        }

        return parsed;
    }

    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new OkfConfigException($"Option '{option}' requires a value.");
        }

        return args[++index];
    }
}
