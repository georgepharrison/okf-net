using Okf.Core;

namespace Okf.Cli.Verify;

/// <summary>The parsed form of <c>okf verify</c>'s command line.</summary>
internal sealed class VerifyArguments
{
    private readonly List<string> _paths = [];

    private VerifyArguments()
    {
    }

    /// <summary>The concept paths to stamp, in the order given.</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>
    /// The <c>--by</c> override: the actor to stamp instead of the resolved human
    /// identity, for a second actor recording a machine-confirmed verification (§5.3).
    /// </summary>
    public string? By { get; private set; }

    /// <summary>Whether to report what would be written and write nothing.</summary>
    public bool DryRun { get; private set; }

    /// <summary>Whether to report identity resolution.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>The project config to read <c>verify.actor</c> from, instead of the vault's.</summary>
    public string? ConfigPath { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags = ["--help", "-h", "--verbose", "-v", "--dry-run", "--by", "--config"];

    /// <summary>Parses <c>okf verify</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>verify</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static VerifyArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        VerifyArguments parsed = new VerifyArguments();

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

                case "--dry-run":
                    parsed.DryRun = true;
                    break;

                case "--by":
                    parsed.TakeBy(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--config":
                    parsed.ConfigPath = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                default:
                    parsed.TakePath(argument);
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

    private void TakePath(string argument)
    {
        if (argument.StartsWith('-') && argument.Length > 1)
        {
            throw new OkfConfigException($"Unknown option '{argument}'.");
        }

        _paths.Add(argument);
    }
}
