using Okf.Core;

namespace Okf.Cli;

/// <summary>The parsed form of <c>okf site</c>'s command line.</summary>
internal sealed class SiteArguments
{
    private SiteArguments()
    {
    }

    /// <summary>The explicit target path, or <see langword="null" /> to discover one.</summary>
    public string? Path { get; private set; }

    /// <summary>The output directory. Required unless the command is only printing help.</summary>
    public string? Out { get; private set; }

    /// <summary>The site's display name, or <see langword="null" /> to take the vault's.</summary>
    public string? Name { get; private set; }

    /// <summary>Whether to emit the whole site as one <c>index.html</c>.</summary>
    public bool SingleFile { get; private set; }

    /// <summary>Whether to emit the stable JSON report instead of human-readable lines.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report vault resolution.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Parses <c>okf site</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>site</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static SiteArguments Parse(string[] args)
    {
        var parsed = new SiteArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--out" or "-o":
                    var output = inlineValue ?? Next(args, ref index, name);
                    if (parsed.Out is not null)
                    {
                        throw new OkfConfigException($"Option '{name}' was given twice.");
                    }

                    parsed.Out = output.Length > 0
                        ? output
                        : throw new OkfConfigException($"Option '{name}' requires a directory.");
                    break;

                case "--name":
                    parsed.Name = inlineValue ?? Next(args, ref index, name);
                    break;

                case "--single-file":
                    parsed.SingleFile = true;
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
                            $"`okf site` takes at most one path; got '{parsed.Path}' and '{argument}'.");
                    }

                    parsed.Path = argument;
                    break;
            }
        }

        // Checked here rather than in the command, so a missing --out is a usage failure
        // reported the same way as a malformed one — before any bundle is read.
        if (!parsed.ShowHelp && parsed.Out is null)
        {
            throw new OkfConfigException("`okf site` requires an output directory: --out <dir>.");
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
