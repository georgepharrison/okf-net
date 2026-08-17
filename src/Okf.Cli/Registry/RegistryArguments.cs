using Okf.Core;

namespace Okf.Cli.Registry;

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

    /// <summary>
    /// Every option <see cref="Parse" /> accepts, and therefore every option all three
    /// registry verbs accept, since one parser serves them (see
    /// <see cref="InitArguments.Flags" />).
    /// </summary>
    public static readonly string[] Flags = ["--help", "-h", "--verbose", "-v", "--json", "--format"];

    /// <summary>Parses one registry command line.</summary>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="verb">The verb, for error messages.</param>
    /// <param name="subcommands">Whether a bare word names an action rather than a target.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static RegistryArguments Parse(string[] args, string verb, bool subcommands)
    {
        RegistryArguments parsed = new RegistryArguments();
        bool actionSeen = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
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
                    parsed.Json = CliArguments.ParseFormat(FormatValue(argument, args, ref index));
                    break;

                default:
                    parsed.TakeOperand(argument, verb, subcommands, ref actionSeen);
                    break;
            }
        }

        return parsed;
    }

    private static string FormatValue(string argument, string[] args, ref int index) =>
        argument.Length > "--format".Length
            ? argument["--format=".Length..]
            : CliArguments.Next(args, ref index, "--format");

    private void TakeOperand(string argument, string verb, bool subcommands, ref bool actionSeen)
    {
        if (argument.StartsWith('-') && argument.Length > 1)
        {
            throw new OkfConfigException($"Unknown option '{argument}'.");
        }

        if (subcommands && !actionSeen)
        {
            Action = ParseAction(argument);
            actionSeen = true;
            return;
        }

        if (Target is null && !subcommands)
        {
            Target = argument;
            return;
        }

        throw new OkfConfigException(
            $"`okf {verb}` takes at most one argument; got '{Target ?? argument}' and '{argument}'.");
    }

    private static RegistryAction ParseAction(string argument) => argument switch
    {
        "list" => RegistryAction.List,
        "prune" => RegistryAction.Prune,
        _ => throw new OkfConfigException(
            $"Unknown `okf registry` subcommand '{argument}'; expected 'list' or 'prune'."),
    };
}
