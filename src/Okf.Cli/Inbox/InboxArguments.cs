using Okf.Core;

namespace Okf.Cli.Inbox;

/// <summary>The parsed form of <c>okf inbox</c>'s command line.</summary>
internal sealed class InboxArguments
{
    private InboxArguments()
    {
    }

    /// <summary>The explicit target path, or <see langword="null" /> to discover one.</summary>
    public string? Path { get; private set; }

    /// <summary>Whether to emit the stable JSON array instead of human-readable lines.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report vault resolution and the scan counts.</summary>
    public bool Verbose { get; private set; }

    /// <summary>
    /// Whether a non-empty inbox should exit 1. Off by default: the inbox is a report, and
    /// a report that fails a pipeline stops being read (PRD CLI-12, §1.3's non-goal).
    /// </summary>
    public bool FailIfAny { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
        ["--help", "-h", "--verbose", "-v", "--json", "--fail-if-any", "--format"];

    /// <summary>Parses <c>okf inbox</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>inbox</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static InboxArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        InboxArguments parsed = new InboxArguments();

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            (string name, string? inlineValue) = CliArguments.Split(argument);

            switch (name)
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

                case "--fail-if-any":
                    parsed.FailIfAny = true;
                    break;

                case "--format":
                    parsed.Json = CliArguments.ParseFormat(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OkfConfigException($"Unknown option '{argument}'.");
                    }

                    if (parsed.Path is not null)
                    {
                        throw new OkfConfigException(
                            $"`okf inbox` takes at most one path; got '{parsed.Path}' and '{argument}'.");
                    }

                    parsed.Path = argument;
                    break;
            }
        }

        return parsed;
    }
}
