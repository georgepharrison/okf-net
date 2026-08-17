using System.Globalization;
using System.Text;
using Okf.Core;

namespace Okf.Cli.Search;

/// <summary>
/// <c>okf search &lt;query&gt; [path]</c> — ranks the concepts in the resolved bundles
/// against a query and reports them links-first (PRD CLI-11, CORE-11; decisions.md Q7).
/// </summary>
internal static class SearchCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>search</c>.</param>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="output">Where the results go.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        if (Parse(args, error) is not { } parsed)
        {
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Search(parsed, environment, output, error);
        }
        catch (Exception exception) when (IsEnvironmentFailure(exception))
        {
            // Scope resolution reads okf.json and, beyond `--scope project`, the registry:
            // a malformed one of either is an environment failure the caller can repair
            // (CLI-14's exit 2), never a stack trace out of the middle of a search.
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
    }

    private static SearchArguments? Parse(string[] args, TextWriter error)
    {
        try
        {
            return SearchArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf search --help` for usage.");
            return null;
        }
    }

    private static bool IsEnvironmentFailure(Exception exception) =>
        exception is OkfDiscoveryException or OkfConfigException or IOException or UnauthorizedAccessException;

    private static int Search(
        SearchArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        if (Query(arguments, error) is not { } query)
        {
            return CliApplication.ExitUsage;
        }

        if (Resolve(arguments, environment, error) is not { } resolved)
        {
            return CliApplication.ExitUsage;
        }

        OkfSearchOutcome outcome = Rank(arguments, resolved.WorkingSet, query);
        if (arguments.Verbose)
        {
            WriteVerbose(error, new VerboseSearch(resolved.WorkingSet, resolved.Scope, query, outcome));
        }

        WriteReport(new SearchReport(arguments, outcome, resolved.WorkingSet, environment.CurrentDirectory), output);

        // PRD CLI-11 and the §3 CLI-surface table: an empty result set is not an error, so
        // the grep convention (1 = no matches) deliberately does not apply here. 1 stays
        // reserved for diagnostics at error severity (CLI-14).
        return CliApplication.ExitSuccess;
    }

    private static OkfSearchOutcome Rank(
        SearchArguments arguments,
        OkfWorkingSet workingSet,
        OkfSearchQuery query) =>
        OkfSearchEngine.Search(
            workingSet.Bundles,
            query,
            new OkfSearchOptions
            {
                Limit = arguments.Limit,
                Today = DateOnly.FromDateTime(DateTime.Now),
            });

    private static OkfSearchQuery? Query(SearchArguments arguments, TextWriter error)
    {
        OkfSearchQuery query = arguments.ToQuery();
        if (!query.IsEmpty)
        {
            return query;
        }

        // Nothing to match and nothing to filter by. Returning the whole vault would
        // be a listing command, which this is not.
        error.WriteLine("okf: error: no query. Give at least one search term or a `type:`/`tag:` filter.");
        error.WriteLine("Run `okf search --help` for usage.");
        return null;
    }

    private sealed record ResolvedSearch(OkfWorkingSet WorkingSet, ScopeSettings Scope);

    /// <summary>
    /// PRD CLI-1/CLI-3: search resolves its working set exactly as `okf lint` does, so
    /// the two commands never disagree about which bundles they are looking at, and the
    /// default scope is the project vault — identical results on every machine.
    /// </summary>
    private static ResolvedSearch? Resolve(
        SearchArguments arguments,
        OkfEnvironment environment,
        TextWriter error)
    {
        ScopeSettings scope = ScopeSettings.Resolve(arguments.Scope, environment);
        if (arguments.Path is not null && scope.Scope != OkfScopeKind.Project && arguments.Scope is not null)
        {
            // A path names the bundles; a scope names how to find them. Honouring one would
            // mean silently ignoring the other, and a flag that does nothing is worse than a
            // refusal.
            error.WriteLine(
                $"okf: error: `--scope {scope.Scope.ToScopeString()}` and an explicit path are exclusive; " +
                "a path already says which bundles to search.");
            error.WriteLine("Run `okf search --help` for usage.");
            return null;
        }

        OkfScopeResolution resolution = arguments.Path is not null
            ? new OkfScopeResolution(OkfDiscovery.Resolve(arguments.Path, environment), [])
            : OkfScope.Resolve(scope.Scope, environment);
        WriteNotes(resolution, error);
        return new ResolvedSearch(resolution.WorkingSet, scope);
    }

    private static void WriteNotes(OkfScopeResolution resolution, TextWriter error)
    {
        foreach (string note in resolution.Notes)
        {
            // Reported once, on stderr, and never an error: a laptop that has not been set
            // up the same way must not fail a query (PRD CLI-2).
            error.WriteLine($"okf: {note}");
        }
    }

    private sealed record SearchReport(
        SearchArguments Arguments,
        OkfSearchOutcome Outcome,
        OkfWorkingSet WorkingSet,
        string BaseDirectory);

    private static void WriteReport(SearchReport report, TextWriter output)
    {
        if (report.Arguments.Json)
        {
            output.Write(SearchJson.Write(report.Outcome));
            output.Write(Environment.NewLine);
            return;
        }

        WriteText(report.Outcome, report.WorkingSet, report.BaseDirectory, output);
    }

    private sealed record VerboseSearch(
        OkfWorkingSet WorkingSet,
        ScopeSettings Scope,
        OkfSearchQuery Query,
        OkfSearchOutcome Outcome);

    private static void WriteVerbose(TextWriter error, VerboseSearch search)
    {
        VerboseReport.Scope(error, search.Scope);
        VerboseReport.WorkingSet(error, search.WorkingSet);

        error.WriteLine($"okf: query {search.Query}");
        error.WriteLine($"okf: match mode {SearchJson.MatchMode(search.Outcome.MatchMode)}");
        if (search.Outcome.SkippedCount > 0)
        {
            error.WriteLine(
                $"okf: skipped {DiagnosticWriter.Plural(search.Outcome.SkippedCount, "file")} whose frontmatter does not " +
                "parse (run `okf lint`)");
        }
    }

    /// <summary>
    /// The roots a working set spans: the vault a bundle sits in, or the bundle itself when
    /// it stands alone. More than one means a result's path has to name which one it came
    /// from — two vaults can hold a bundle of the same name holding a concept of the same
    /// name, and a bare relative path would make them indistinguishable.
    /// </summary>
    private static IReadOnlyList<string> Roots(OkfWorkingSet workingSet) =>
        [.. workingSet.Bundles
            .Select(bundle => Path.GetDirectoryName(bundle.Root) is { } parent
                && string.Equals(Path.GetFileName(parent), OkfDiscovery.BundlesDirectoryName, StringComparison.Ordinal)
                && Path.GetDirectoryName(parent) is { Length: > 0 } vault
                    ? vault
                    : bundle.Root)
            .Distinct(StringComparer.Ordinal)];

    private static void WriteText(
        OkfSearchOutcome outcome,
        OkfWorkingSet workingSet,
        string baseDirectory,
        TextWriter output)
    {
        IReadOnlyList<string> roots = Roots(workingSet);
        WriteFallback(outcome, output);
        WriteHits(outcome, roots, baseDirectory, output);
        output.WriteLine(Summary(outcome, roots));
    }

    private static void WriteFallback(OkfSearchOutcome outcome, TextWriter output)
    {
        if (!outcome.UsedFallback)
        {
            return;
        }

        output.WriteLine(
            $"No concept matched all {DiagnosticWriter.Plural(outcome.Query.Terms.Count, "term")}; " +
            "showing concepts matching any of them.");
    }

    private static void WriteHits(
        OkfSearchOutcome outcome,
        IReadOnlyList<string> roots,
        string baseDirectory,
        TextWriter output)
    {
        int rank = 0;
        foreach (OkfSearchResult result in outcome.Results)
        {
            rank++;
            string type = result.Type is { Length: > 0 } value ? $" ({value})" : string.Empty;
            output.WriteLine(
                $"{rank.ToString(CultureInfo.InvariantCulture)}. " +
                $"{result.Score.ToString("F4", CultureInfo.InvariantCulture)}  " +
                $"{HitPath(result, roots, baseDirectory)}  " +
                $"{result.Title}{type}  [{Markers(result)}]");

            if (result.Snippet.Length > 0)
            {
                output.WriteLine($"    {result.Snippet}");
            }
        }
    }

    private static string HitPath(OkfSearchResult result, IReadOnlyList<string> roots, string baseDirectory) =>
        roots.Count > 1 ? result.AbsolutePath : DiagnosticWriter.Display(result.AbsolutePath, baseDirectory);

    private static string Summary(OkfSearchOutcome outcome, IReadOnlyList<string> roots)
    {
        StringBuilder summary = new StringBuilder()
            .Append("Found ")
            .Append(DiagnosticWriter.Plural(outcome.TotalMatches, "result"))
            .Append(" in ")
            .Append(DiagnosticWriter.Plural(outcome.ConceptCount, "concept"))
            .Append(" across ")
            .Append(DiagnosticWriter.Plural(outcome.BundleCount, "bundle"));

        if (roots.Count > 1)
        {
            summary.Append(" in ").Append(DiagnosticWriter.Plural(roots.Count, "vault"));
        }

        if (outcome.Truncated)
        {
            summary.Append("; showing the top ")
                .Append(outcome.Results.Count.ToString(CultureInfo.InvariantCulture));
        }

        return summary.Append('.').ToString();
    }

    /// <summary>The trust and staleness markers a result carries, e.g. <c>human-reviewed, stale</c>.</summary>
    private static string Markers(OkfSearchResult result) =>
        result.TrustTier.ToSpecString() + (result.Stale ? ", stale" : string.Empty);

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf search <query> [path] [options]

            Ranks the concepts in the resolved bundles against a query and reports them
            links-first: a path, a title, a type, trust and staleness markers, and a short
            snippet — never a full concept body. Matching is tokenized and deterministic;
            the same tree and the same query always produce the same results.

            Query terms are AND-ed. When nothing matches every term the search falls back
            to matching any of them and says so. Filters may be written inline:

              okf search "widget pricing tag:catalog type:Playbook"

            Arguments:
              query                         The search terms. Quote a multi-word query —
                                            the second bare argument is the path.
              path                          A bundle root, a vault (a directory holding
                                            bundles/), or a project root (holding
                                            okf/bundles/). Defaults to the vault found by
                                            walking up from the working directory, then to
                                            the personal vault (OKF_HOME, else ~/okf).

            Options:
              --scope <project|personal|registered|all>
                                            Which vaults to search. project (the default) is
                                            the vault above; personal is OKF_HOME, else
                                            ~/okf; registered is every entry in okf's
                                            registry; all is the project plus the registry.
                                            Settable as `search.scope` in okf.json; a path
                                            argument and --scope are exclusive.
              --type <type>                 Only concepts with this `type`; repeatable (OR)
              --tag <tag>                   Only concepts carrying this tag; repeatable (AND)
              --limit <n>                   Report at most n results (default: 10)
              --format <text|json>          Output format (default: text)
              --json                        Alias for --format json
              --verbose, -v                 Report vault resolution and the effective query
              --help, -h                    Show this help

            Exit codes:
              0  the search ran, with or without results
              2  usage or environment failure
            """);
    }
}
