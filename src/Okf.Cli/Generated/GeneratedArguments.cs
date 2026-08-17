using Okf.Core;

namespace Okf.Cli.Generated;

/// <summary>The parsed form of <c>okf generated</c>'s command line.</summary>
internal sealed class GeneratedArguments
{
    private readonly List<string> _paths = [];

    private GeneratedArguments()
    {
    }

    /// <summary>The subcommand — <c>stamp</c> — or null when none was given.</summary>
    public string? Verb { get; private set; }

    /// <summary>The concept paths to stamp, in the order given.</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>The generating actor (<c>--by</c>), which is never defaulted.</summary>
    public string? By { get; private set; }

    /// <summary>The pinned instant (<c>--at</c>), or null for now.</summary>
    public DateTimeOffset? At { get; private set; }

    /// <summary>Whether to report what would be written and write nothing.</summary>
    public bool DryRun { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags = ["--help", "-h", "--dry-run", "--by", "--at"];

    /// <summary>Parses <c>okf generated</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>generated</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static GeneratedArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        GeneratedArguments parsed = new GeneratedArguments();

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            (string name, string? inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--dry-run":
                    parsed.DryRun = true;
                    break;

                case "--by":
                    parsed.TakeBy(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--at":
                    parsed.TakeAt(inlineValue ?? CliArguments.Next(args, ref index, name), name);
                    break;

                default:
                    parsed.TakeOperand(argument);
                    break;
            }
        }

        return parsed;
    }

    private void TakeBy(string value)
    {
        if (By is not null)
        {
            throw new OkfConfigException("Option '--by' may be given once; a stamp names one actor.");
        }

        By = value;
    }

    private void TakeAt(string value, string option)
    {
        if (At is not null)
        {
            throw new OkfConfigException("Option '--at' may be given once; a stamp names one instant.");
        }

        At = CliArguments.ParseInstant(value, option);
    }

    private void TakeOperand(string argument)
    {
        if (argument.StartsWith('-') && argument.Length > 1)
        {
            throw new OkfConfigException($"Unknown option '{argument}'.");
        }

        if (Verb is null)
        {
            Verb = argument;
            return;
        }

        _paths.Add(argument);
    }
}
