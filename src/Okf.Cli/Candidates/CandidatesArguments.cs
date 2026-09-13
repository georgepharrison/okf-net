using Okf.Core;

namespace Okf.Cli.Candidates;

/// <summary>The parsed form of <c>okf candidates</c>'s command line.</summary>
internal sealed class CandidatesArguments
{
    private CandidatesArguments()
    {
    }

    /// <summary>The explicit target path, or <see langword="null" /> to discover one.</summary>
    public string? Path { get; private set; }

    /// <summary>Whether to emit the stable JSON array instead of human-readable rows.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report vault resolution and the scan counts.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags = ["--help", "-h", "--verbose", "-v", "--json", "--format"];

    /// <summary>Parses <c>okf candidates</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>candidates</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static CandidatesArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        CandidatesArguments parsed = new CandidatesArguments();

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

                case "--format":
                    parsed.Json = CliArguments.ParseFormat(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                default:
                    parsed.TakePath(argument);
                    break;
            }
        }

        return parsed;
    }

    private void TakePath(string argument)
    {
        if (argument.StartsWith('-') && argument.Length > 1)
        {
            throw new OkfConfigException($"Unknown option '{argument}'.");
        }

        if (Path is not null)
        {
            throw new OkfConfigException(
                $"`okf candidates` takes at most one path; got '{Path}' and '{argument}'.");
        }

        Path = argument;
    }
}
