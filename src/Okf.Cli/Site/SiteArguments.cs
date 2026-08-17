using Okf.Core;

namespace Okf.Cli.Site;

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

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
        ["--help", "-h", "--out", "-o", "--name", "--single-file", "--verbose", "-v", "--json", "--format"];

    /// <summary>Parses <c>okf site</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>site</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static SiteArguments Parse(string[] args)
    {
        SiteArguments parsed = new SiteArguments();

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            (string name, string? inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--out" or "-o":
                    string output = inlineValue ?? CliArguments.Next(args, ref index, name);
                    if (parsed.Out is not null)
                    {
                        throw new OkfConfigException($"Option '{name}' was given twice.");
                    }

                    parsed.Out = output.Length > 0
                        ? output
                        : throw new OkfConfigException($"Option '{name}' requires a directory.");
                    break;

                case "--name":
                    parsed.Name = inlineValue ?? CliArguments.Next(args, ref index, name);
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
                    string format = inlineValue ?? CliArguments.Next(args, ref index, name);
                    parsed.Json = CliArguments.ParseFormat(format);
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
}
