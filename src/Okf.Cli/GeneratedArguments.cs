using Okf.Core;

namespace Okf.Cli;

/// <summary>The parsed form of <c>okf generated</c>'s command line.</summary>
internal sealed class GeneratedArguments
{
    private readonly List<string> paths = [];

    private GeneratedArguments()
    {
    }

    /// <summary>The subcommand — <c>stamp</c> — or null when none was given.</summary>
    public string? Verb { get; private set; }

    /// <summary>The concept paths to stamp, in the order given.</summary>
    public IReadOnlyList<string> Paths => this.paths;

    /// <summary>The generating actor (<c>--by</c>), which is never defaulted.</summary>
    public string? By { get; private set; }

    /// <summary>The pinned instant (<c>--at</c>), or null for now.</summary>
    public DateTimeOffset? At { get; private set; }

    /// <summary>Whether to report what would be written and write nothing.</summary>
    public bool DryRun { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Parses <c>okf generated</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>generated</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static GeneratedArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var parsed = new GeneratedArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--dry-run":
                    parsed.DryRun = true;
                    break;

                case "--by":
                    if (parsed.By is not null)
                    {
                        throw new OkfConfigException("Option '--by' may be given once; a stamp names one actor.");
                    }

                    parsed.By = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                case "--at":
                    if (parsed.At is not null)
                    {
                        throw new OkfConfigException("Option '--at' may be given once; a stamp names one instant.");
                    }

                    parsed.At = CliArguments.ParseInstant(inlineValue ?? CliArguments.Next(args, ref index, name), name);
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OkfConfigException($"Unknown option '{argument}'.");
                    }

                    if (parsed.Verb is null)
                    {
                        parsed.Verb = argument;
                    }
                    else
                    {
                        parsed.paths.Add(argument);
                    }

                    break;
            }
        }

        return parsed;
    }
}
