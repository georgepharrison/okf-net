using System.Globalization;
using Okf.Core;

namespace Okf.Cli.Inbox;

/// <summary>
/// <c>okf inbox [path]</c> — lists the concepts in the resolved bundles that are waiting
/// on a person: unacknowledged, stale, or citing a source that moved (PRD CLI-12,
/// CORE-15).
/// </summary>
/// <remarks>
/// It is a report, not a gate. Exit 0 whatever it finds, because an inbox that fails a
/// pipeline is an inbox people route around — <c>--fail-if-any</c> exists for the caller
/// that genuinely wants the branch, and is off by default.
/// </remarks>
internal static class InboxCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>inbox</c>.</param>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        InboxArguments parsed;
        try
        {
            parsed = InboxArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf inbox --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Report(parsed, environment, output, error);
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

    private static int Report(
        InboxArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        // PRD CLI-1: the same working set `okf lint` and `okf search` resolve, so the three
        // never disagree about which bundles they are looking at.
        var workingSet = OkfDiscovery.Resolve(arguments.Path, environment);
        var result = OkfInboxScanner.Scan(
            workingSet.Bundles,
            new OkfInboxOptions { Today = DateOnly.FromDateTime(DateTime.Now) });

        if (arguments.Verbose)
        {
            VerboseReport.WorkingSet(error, workingSet);

            error.WriteLine(
                $"okf: read {DiagnosticWriter.Plural(result.ConceptCount, "concept")}, " +
                $"{DiagnosticWriter.Plural(result.Items.Count, "item")} on the inbox");
        }

        if (arguments.Json)
        {
            output.Write(InboxJson.Write(result, environment.CurrentDirectory));
            output.Write(Environment.NewLine);
        }
        else
        {
            WriteText(result, environment.CurrentDirectory, output);
        }

        return arguments.FailIfAny && !result.IsEmpty
            ? CliApplication.ExitDiagnostics
            : CliApplication.ExitSuccess;
    }

    private static void WriteText(OkfInboxResult result, string baseDirectory, TextWriter output)
    {
        foreach (var reason in OkfInboxReasonExtensions.All)
        {
            var items = result.For(reason).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            output.WriteLine(
                $"{reason.ToHeading()} ({items.Count.ToString(CultureInfo.InvariantCulture)})");
            foreach (var item in items)
            {
                var type = item.Concept.Type is { Length: > 0 } value ? $" ({value})" : string.Empty;
                output.WriteLine(
                    $"  {DiagnosticWriter.Display(item.Concept.AbsolutePath, baseDirectory)}  " +
                    $"{item.Concept.Title}{type}");
                output.WriteLine($"    {Detail(item, reason)}");
            }

            output.WriteLine();
        }

        var summary =
            $"Checked {DiagnosticWriter.Plural(result.ConceptCount, "concept")} in " +
            $"{DiagnosticWriter.Plural(result.Bundles.Count, "bundle")}: ";

        output.WriteLine(result.IsEmpty
            ? summary + "nothing needs attention."
            : summary
                + $"{DiagnosticWriter.Plural(result.Items.Count, "concept")} "
                + $"{(result.Items.Count == 1 ? "needs" : "need")} attention "
                + $"({result.Count(OkfInboxReason.Unacknowledged).ToString(CultureInfo.InvariantCulture)} unacknowledged, "
                + $"{result.Count(OkfInboxReason.Stale).ToString(CultureInfo.InvariantCulture)} stale, "
                + $"{result.Count(OkfInboxReason.SourceDrift).ToString(CultureInfo.InvariantCulture)} with source drift).");

        if (result.SkippedCount > 0)
        {
            output.WriteLine(
                $"Skipped {DiagnosticWriter.Plural(result.SkippedCount, "file")} whose frontmatter does not " +
                "parse (run `okf lint`).");
        }
    }

    /// <summary>The one line under a row that says what the reason actually is for this concept.</summary>
    private static string Detail(OkfInboxItem item, OkfInboxReason reason) => reason switch
    {
        OkfInboxReason.Unacknowledged => Acknowledgment(item),
        OkfInboxReason.Stale => $"stale_after {item.StaleAfter} ({Ago(item.StaleDays)})",
        OkfInboxReason.SourceDrift => Drift(item),
        _ => string.Empty,
    };

    private static string Acknowledgment(OkfInboxItem item)
    {
        var generated = item.GeneratedAt is null
            ? "no generated stamp"
            : $"generated {item.GeneratedBy ?? "an unrecorded actor"} {item.GeneratedAt} ({Ago(item.AgeDays)})";

        var acknowledgment = item.VerifiedAt is null
            ? "never verified"
            : $"last verified {item.VerifiedBy ?? "an unrecorded actor"} {item.VerifiedAt}";

        var draft = string.Equals(item.Status, OkfInboxScanner.DraftStatus, StringComparison.OrdinalIgnoreCase)
            ? "status: draft; "
            : string.Empty;

        return $"{draft}{generated}; {acknowledgment}";
    }

    private static string Drift(OkfInboxItem item)
    {
        var sources = string.Join(
            ", ",
            item.DriftedSources.Select(source => $"{source.Display} ({source.LastModified})"));
        return $"generated {item.GeneratedAt}; moved since: {sources}";
    }

    /// <summary>Renders a day count as an age, or says the date is still ahead.</summary>
    private static string Ago(int? days) => days switch
    {
        null => "undated",
        0 => "today",
        < 0 => $"in {DiagnosticWriter.Plural(-days.Value, "day")}",
        _ => $"{DiagnosticWriter.Plural(days.Value, "day")} ago",
    };

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf inbox [path] [options]

            Lists the concepts in the resolved bundles that are waiting on a person,
            grouped by why. A concept appears under every reason that applies:

              Unacknowledged  `generated.at` is newer than the latest `verified[].at`;
                              or nothing has verified content an agent generated; or the
                              concept carries `status: draft`.
              Stale           today is on or after `stale_after`.
              Source drift    a cited `sources[].last_modified` is later than
                              `generated.at`, so the source moved after this was written.

            `okf verify <concept>` clears the first. The other two are re-checked against
            the source and then restamped — see the okf-custodian skill's staleness-refresh
            procedure.

            Arguments:
              path                          A bundle root, a vault (a directory holding
                                            bundles/), or a project root (holding
                                            okf/bundles/). Defaults to the vault found by
                                            walking up from the working directory, then to
                                            the personal vault (OKF_HOME, else ~/okf).

            Options:
              --format <text|json>          Output format (default: text)
              --json                        Alias for --format json
              --fail-if-any                 Exit 1 when anything is listed (for CI that
                                            wants the branch; off by default)
              --verbose, -v                 Report vault resolution and the scan counts
              --help, -h                    Show this help

            Exit codes:
              0  the scan ran, with or without items
              1  items were listed and --fail-if-any was given
              2  usage or environment failure
            """);
    }
}
