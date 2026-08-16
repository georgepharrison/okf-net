using System.Globalization;
using Okf.Core;

namespace Okf.Cli;

/// <summary>The parsed form of <c>okf capture</c>'s command line.</summary>
internal sealed class CaptureArguments
{
    private readonly List<string> operands = [];
    private readonly List<string> concepts = [];

    private CaptureArguments()
    {
    }

    /// <summary>The subcommand — <c>add</c> or <c>close</c> — or null when none was given.</summary>
    public string? Verb { get; private set; }

    /// <summary>The item path (<c>add</c>) or the entry id or path (<c>close</c>).</summary>
    public IReadOnlyList<string> Operands => this.operands;

    /// <summary>The <c>--concept</c> values, in the order given.</summary>
    public IReadOnlyList<string> Concepts => this.concepts;

    /// <summary>The capturing or ingesting actor (<c>--by</c>), which is never defaulted.</summary>
    public string? By { get; private set; }

    /// <summary>The pinned instant (<c>--captured-at</c> / <c>--at</c>), or null for now.</summary>
    public DateTimeOffset? At { get; private set; }

    /// <summary>The original URL (<c>--url</c>).</summary>
    public string? Url { get; private set; }

    /// <summary>The artifact's title (<c>--title</c>).</summary>
    public string? Title { get; private set; }

    /// <summary>The artifact's own last-modified date (<c>--source-last-modified</c>).</summary>
    public string? SourceLastModified { get; private set; }

    /// <summary>The asserted form (<c>--form</c>), checked against the item's actual shape.</summary>
    public OkfCaptureForm? Form { get; private set; }

    /// <summary>Whether to report the written entry as JSON rather than a summary line.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
    [
        "--help",
        "-h",
        "--json",
        "--by",
        "--url",
        "--title",
        "--source-last-modified",
        "--form",
        "--concept",
        "--captured-at",
        "--at",
    ];

    /// <summary>Parses <c>okf capture</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>capture</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static CaptureArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var parsed = new CaptureArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--json":
                    parsed.Json = true;
                    break;

                case "--by":
                    if (parsed.By is not null)
                    {
                        throw new OkfConfigException("Option '--by' may be given once; an entry names one actor.");
                    }

                    parsed.By = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                case "--url":
                    parsed.Url = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                case "--title":
                    parsed.Title = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                case "--source-last-modified":
                    parsed.SourceLastModified = ReadDate(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--form":
                    parsed.Form = ReadForm(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--concept":
                    parsed.concepts.Add(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--captured-at" or "--at":
                    if (parsed.At is not null)
                    {
                        throw new OkfConfigException($"Option '{name}' may be given once; an entry names one instant.");
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
                        parsed.operands.Add(argument);
                    }

                    break;
            }
        }

        return parsed;
    }

    private static OkfCaptureForm ReadForm(string value) => value switch
    {
        "flat" => OkfCaptureForm.Flat,
        "packet" => OkfCaptureForm.Packet,
        _ => throw new OkfConfigException($"Unknown --form value '{value}'; expected 'flat' or 'packet'."),
    };

    private static string ReadDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? value
            : throw new OkfConfigException(
                $"'{value}' is not a date. `--source-last-modified` becomes the ingested concept's "
                + "`sources[].last_modified`, which SPEC §5.1 writes YYYY-MM-DD.");
}
