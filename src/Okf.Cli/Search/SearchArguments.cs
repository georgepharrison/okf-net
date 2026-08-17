using System.Globalization;
using Okf.Core;

namespace Okf.Cli.Search;

/// <summary>The parsed form of <c>okf search</c>'s command line.</summary>
internal sealed class SearchArguments
{
    private readonly List<string> _types = [];
    private readonly List<string> _tags = [];

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
    public IReadOnlyList<string> Types => _types;

    /// <summary>The <c>--tag</c> filters, equivalent to inline <c>tag:</c> filters.</summary>
    public IReadOnlyList<string> Tags => _tags;

    /// <summary>
    /// The <c>--scope</c> flag, or <see langword="null" /> when configuration decides
    /// (PRD CLI-3, CLI-4).
    /// </summary>
    public OkfScopeKind? Scope { get; private set; }

    /// <summary>Whether to emit the stable JSON array instead of human-readable lines.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report vault resolution and the effective query.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
        ["--help", "-h", "--verbose", "-v", "--json", "--format", "--limit", "--scope", "--type", "--tag"];

    /// <summary>Parses <c>okf search</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>search</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static SearchArguments Parse(string[] args)
    {
        SearchArguments parsed = new SearchArguments();

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
                    string format = inlineValue ?? CliArguments.Next(args, ref index, name);
                    parsed.Json = CliArguments.ParseFormat(format);
                    break;

                case "--limit":
                    string limit = inlineValue ?? CliArguments.Next(args, ref index, name);
                    parsed.Limit = int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                        && count > 0
                        ? count
                        : throw new OkfConfigException($"--limit expects a positive whole number; got '{limit}'.");
                    break;

                case "--scope":
                    string scope = inlineValue ?? CliArguments.Next(args, ref index, name);
                    parsed.Scope = OkfScopeKindExtensions.TryParse(scope, out OkfScopeKind kind)
                        ? kind
                        : throw new OkfConfigException(
                            $"Unknown --scope value '{scope}'; expected one of " +
                            string.Join(", ", OkfScopeKindExtensions.Names) + ".");
                    break;

                case "--type":
                    parsed._types.Add(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--tag":
                    parsed._tags.Add(inlineValue ?? CliArguments.Next(args, ref index, name));
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
        OkfSearchQuery query = OkfSearchQuery.Parse(Query);
        foreach (string type in _types)
        {
            query.AddTypeFilter(type);
        }

        foreach (string tag in _tags)
        {
            query.AddTagFilter(tag);
        }

        return query;
    }
}
