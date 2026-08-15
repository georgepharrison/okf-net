using System.Globalization;
using System.Text;
using Okf.Core;

namespace Okf.Cli;

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
        SearchArguments parsed;
        try
        {
            parsed = SearchArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf search --help` for usage.");
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
        catch (OkfDiscoveryException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
        catch (IOException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
        catch (UnauthorizedAccessException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
    }

    private static int Search(
        SearchArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        var query = arguments.ToQuery();
        if (query.IsEmpty)
        {
            // Nothing to match and nothing to filter by. Returning the whole vault would
            // be a listing command, which this is not.
            error.WriteLine("okf: error: no query. Give at least one search term or a `type:`/`tag:` filter.");
            error.WriteLine("Run `okf search --help` for usage.");
            return CliApplication.ExitUsage;
        }

        // PRD CLI-1/CLI-3: search resolves its working set exactly as `okf lint` does, so
        // the two commands never disagree about which bundles they are looking at, and the
        // default scope is the project vault — identical results on every machine.
        var workingSet = OkfDiscovery.Resolve(arguments.Path, environment);

        var outcome = OkfSearchEngine.Search(
            workingSet.Bundles,
            query,
            new OkfSearchOptions
            {
                Limit = arguments.Limit,
                Today = DateOnly.FromDateTime(DateTime.Now),
            });

        if (arguments.Verbose)
        {
            WriteVerbose(error, workingSet, query, outcome);
        }

        if (arguments.Json)
        {
            output.Write(SearchJson.Write(outcome));
            output.Write(Environment.NewLine);
        }
        else
        {
            WriteText(outcome, environment.CurrentDirectory, output);
        }

        // PRD CLI-11 and the §3 CLI-surface table: an empty result set is not an error, so
        // the grep convention (1 = no matches) deliberately does not apply here. 1 stays
        // reserved for diagnostics at error severity (CLI-14).
        return CliApplication.ExitSuccess;
    }

    private static void WriteVerbose(
        TextWriter error,
        OkfWorkingSet workingSet,
        OkfSearchQuery query,
        OkfSearchOutcome outcome)
    {
        error.WriteLine($"okf: resolved {workingSet.Resolution}");
        foreach (var bundle in workingSet.Bundles)
        {
            error.WriteLine($"okf: bundle {bundle.Root}");
        }

        error.WriteLine($"okf: query {query}");
        error.WriteLine($"okf: match mode {SearchJson.MatchMode(outcome.MatchMode)}");
        if (outcome.SkippedCount > 0)
        {
            error.WriteLine(
                $"okf: skipped {DiagnosticWriter.Plural(outcome.SkippedCount, "file")} whose frontmatter does not " +
                "parse (run `okf lint`)");
        }
    }

    private static void WriteText(OkfSearchOutcome outcome, string baseDirectory, TextWriter output)
    {
        if (outcome.UsedFallback)
        {
            output.WriteLine(
                $"No concept matched all {DiagnosticWriter.Plural(outcome.Query.Terms.Count, "term")}; " +
                "showing concepts matching any of them.");
        }

        var rank = 0;
        foreach (var result in outcome.Results)
        {
            rank++;
            var type = result.Type is { Length: > 0 } value ? $" ({value})" : string.Empty;
            output.WriteLine(
                $"{rank.ToString(CultureInfo.InvariantCulture)}. " +
                $"{result.Score.ToString("F4", CultureInfo.InvariantCulture)}  " +
                $"{DiagnosticWriter.Display(result.AbsolutePath, baseDirectory)}  " +
                $"{result.Title}{type}  [{Markers(result)}]");

            if (result.Snippet.Length > 0)
            {
                output.WriteLine($"    {result.Snippet}");
            }
        }

        var summary = new StringBuilder()
            .Append("Found ")
            .Append(DiagnosticWriter.Plural(outcome.TotalMatches, "result"))
            .Append(" in ")
            .Append(DiagnosticWriter.Plural(outcome.ConceptCount, "concept"))
            .Append(" across ")
            .Append(DiagnosticWriter.Plural(outcome.BundleCount, "bundle"));

        if (outcome.Truncated)
        {
            summary.Append("; showing the top ")
                .Append(outcome.Results.Count.ToString(CultureInfo.InvariantCulture));
        }

        output.WriteLine(summary.Append('.').ToString());
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
