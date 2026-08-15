using System.Globalization;
using Okf.Core;

namespace Okf.Cli;

/// <summary>The parsed form of <c>okf search</c>'s command line.</summary>
internal sealed class SearchArguments
{
    private readonly List<string> types = [];
    private readonly List<string> tags = [];

    private SearchArguments()
    {
    }

    /// <summary>The query text, or <see langword="null" /> when none was given.</summary>
    public string? Query { get; private set; }

    /// <summary>The explicit target path, or <see langword="null" /> to discover one.</summary>
    public string? Path { get; private set; }

    /// <summary>How many results to report at most (PRD CLI-11).</summary>
    public int Limit { get; private set; } = 10;

    /// <summary>The <c>--type</c> filters, equivalent to inline <c>type:</c> filters.</summary>
    public IReadOnlyList<string> Types => this.types;

    /// <summary>The <c>--tag</c> filters, equivalent to inline <c>tag:</c> filters.</summary>
    public IReadOnlyList<string> Tags => this.tags;

    /// <summary>Whether to emit the stable JSON array instead of human-readable lines.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report vault resolution and the effective query.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Parses <c>okf search</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>search</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static SearchArguments Parse(string[] args)
    {
        var parsed = new SearchArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = Split(argument);

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
                    var format = inlineValue ?? Next(args, ref index, name);
                    parsed.Json = format switch
                    {
                        "json" => true,
                        "text" => false,
                        _ => throw new OkfConfigException($"Unknown --format value '{format}'; expected 'text' or 'json'."),
                    };
                    break;

                case "--limit":
                    var limit = inlineValue ?? Next(args, ref index, name);
                    parsed.Limit = int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                        && count > 0
                        ? count
                        : throw new OkfConfigException($"--limit expects a positive whole number; got '{limit}'.");
                    break;

                case "--type":
                    parsed.types.Add(inlineValue ?? Next(args, ref index, name));
                    break;

                case "--tag":
                    parsed.tags.Add(inlineValue ?? Next(args, ref index, name));
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OkfConfigException($"Unknown option '{argument}'.");
                    }

                    if (parsed.Query is null)
                    {
                        parsed.Query = argument;
                    }
                    else if (parsed.Path is null)
                    {
                        parsed.Path = argument;
                    }
                    else
                    {
                        // A multi-word query has to be quoted, because the second bare
                        // argument is the path (PRD §3's `okf search <query> [path]`).
                        throw new OkfConfigException(
                            $"`okf search` takes one query and at most one path; got '{parsed.Query}', " +
                            $"'{parsed.Path}', and '{argument}'. Quote a multi-word query.");
                    }

                    break;
            }
        }

        return parsed;
    }

    /// <summary>
    /// Builds the query: the inline filter syntax from the query text, widened by the
    /// <c>--type</c> and <c>--tag</c> flags, which are the same filters by another
    /// spelling (decisions.md Q7).
    /// </summary>
    /// <returns>The parsed query.</returns>
    public OkfSearchQuery ToQuery()
    {
        var query = OkfSearchQuery.Parse(Query);
        foreach (var type in this.types)
        {
            query.AddTypeFilter(type);
        }

        foreach (var tag in this.tags)
        {
            query.AddTagFilter(tag);
        }

        return query;
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
