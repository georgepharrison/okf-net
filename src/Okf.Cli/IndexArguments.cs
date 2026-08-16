using Okf.Core;

namespace Okf.Cli;

/// <summary>The parsed form of <c>okf index</c>'s command line.</summary>
internal sealed class IndexArguments
{
    private IndexArguments()
    {
    }

    /// <summary>The explicit target path, or <see langword="null" /> to discover one.</summary>
    public string? Path { get; private set; }

    /// <summary>
    /// Whether to write nothing and instead report drift, exiting non-zero when any
    /// generated index no longer matches the tree (PRD CLI-10).
    /// </summary>
    public bool Check { get; private set; }

    /// <summary>Whether to emit the stable JSON array instead of human-readable lines.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report vault resolution.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
        ["--help", "-h", "--check", "--verbose", "-v", "--json", "--format"];

    /// <summary>Parses <c>okf index</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>index</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static IndexArguments Parse(string[] args)
    {
        var parsed = new IndexArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--check":
                    parsed.Check = true;
                    break;

                case "--verbose" or "-v":
                    parsed.Verbose = true;
                    break;

                case "--json":
                    parsed.Json = true;
                    break;

                case "--format":
                    var format = inlineValue ?? Next(args, ref index, name);
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

                    if (parsed.Path is not null)
                    {
                        throw new OkfConfigException(
                            $"`okf index` takes at most one path; got '{parsed.Path}' and '{argument}'.");
                    }

                    parsed.Path = argument;
                    break;
            }
        }

        return parsed;
    }

    private static (string Name, string? Value) Split(string argument)
    {
        var separator = argument.IndexOf('=', StringComparison.Ordinal);
        return argument.StartsWith("--", StringComparison.Ordinal) && separator > 0
            ? (argument[..separator], argument[(separator + 1)..])
            : (argument, null);
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
