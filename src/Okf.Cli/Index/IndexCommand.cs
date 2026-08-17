using System.Text;
using Okf.Core;

namespace Okf.Cli.Index;

/// <summary>
/// <c>okf index [path]</c> — writes the generated <c>index.md</c> files for the resolved
/// bundles, or, with <c>--check</c>, writes nothing and reports drift (PRD CLI-10,
/// CLI-14).
/// </summary>
internal static class IndexCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>index</c>.</param>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        IndexArguments parsed;
        try
        {
            parsed = IndexArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf index --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Index(parsed, environment, output, error);
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

    private static int Index(
        IndexArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        // PRD CLI-1: `okf index` resolves its working set exactly as `okf lint` does, so
        // the two commands never disagree about which bundle they are looking at.
        var workingSet = OkfDiscovery.Resolve(arguments.Path, environment);

        if (arguments.Verbose)
        {
            VerboseReport.WorkingSet(error, workingSet);

            error.WriteLine(arguments.Check
                ? "okf: --check: nothing will be written"
                : "okf: writing generated index.md files");
        }

        var plans = workingSet.Bundles.Select(bundle => OkfIndexGenerator.Plan(bundle)).ToList();

        if (!arguments.Check)
        {
            foreach (var plan in plans)
            {
                OkfIndexGenerator.Apply(plan);
            }
        }

        var indexes = plans.SelectMany(plan => plan.Indexes).ToList();

        if (arguments.Json)
        {
            output.Write(ToJson(plans, environment.CurrentDirectory));
        }
        else if (arguments.Check)
        {
            WriteCheck(indexes, plans.Count, environment.CurrentDirectory, output);
        }
        else
        {
            WriteWrite(indexes, plans.Count, environment.CurrentDirectory, output);
        }

        // Drift is an error only under --check, which is the CI and hook form of the
        // rule; a plain run has just fixed everything it could fix (PRD CLI-14).
        return arguments.Check && indexes.Any(index => index.IsDrift)
            ? CliApplication.ExitDiagnostics
            : CliApplication.ExitSuccess;
    }

    private static void WriteWrite(
        List<OkfIndex> indexes,
        int bundleCount,
        string baseDirectory,
        TextWriter output)
    {
        foreach (var index in indexes.Where(index => index.Status != OkfIndexStatus.Unchanged))
        {
            output.WriteLine($"{DiagnosticWriter.Display(index.Path, baseDirectory)}: {WriteVerb(index.Status)}");
        }

        var created = indexes.Count(index => index.Status == OkfIndexStatus.Created);
        var updated = indexes.Count(index => index.Status == OkfIndexStatus.Drifted);
        var unchanged = indexes.Count(index => index.Status == OkfIndexStatus.Unchanged);
        var replaced = indexes.Count(index => index.Status == OkfIndexStatus.Foreign);

        var summary = new StringBuilder()
            .Append("Generated ")
            .Append(DiagnosticWriter.Plural(indexes.Count, "index", "indexes"))
            .Append(" in ")
            .Append(DiagnosticWriter.Plural(bundleCount, "bundle"))
            .Append(": ")
            .Append(created.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(" created, ")
            .Append(updated.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(" updated, ")
            .Append(unchanged.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(" unchanged");

        // Replacing a hand-written index is destructive, so it is never folded silently
        // into "updated": generated indexes are outputs (PRD CORE-9), but the run says so.
        if (replaced > 0)
        {
            summary.Append(", ").Append(DiagnosticWriter.Plural(replaced, "hand-written index", "hand-written indexes"))
                .Append(" replaced");
        }

        output.WriteLine(summary.Append('.').ToString());
    }

    private static void WriteCheck(
        List<OkfIndex> indexes,
        int bundleCount,
        string baseDirectory,
        TextWriter output)
    {
        foreach (var index in indexes.Where(index => index.IsDrift))
        {
            output.WriteLine($"{DiagnosticWriter.Display(index.Path, baseDirectory)}: {CheckVerb(index.Status)}");
        }

        var drifted = indexes.Count(index => index.IsDrift);
        output.WriteLine(
            $"Checked {DiagnosticWriter.Plural(indexes.Count, "index", "indexes")} in " +
            $"{DiagnosticWriter.Plural(bundleCount, "bundle")}: " +
            $"{drifted.ToString(System.Globalization.CultureInfo.InvariantCulture)} drifted.");

        // An orphan is drift a regeneration cannot fix — the file has to go — so the hint
        // is printed only when regenerating would actually help.
        if (indexes.Any(index => index.Status == OkfIndexStatus.Drifted))
        {
            output.WriteLine("Run `okf index` to regenerate.");
        }
    }

    private static string ToJson(IReadOnlyList<OkfIndexPlan> plans, string baseDirectory)
    {
        return JsonOutput.Write(writer =>
        {
            writer.WriteStartArray();
            foreach (var plan in plans)
            {
                foreach (var index in plan.Indexes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", DiagnosticWriter.Display(index.Path, baseDirectory));
                    writer.WriteString("absolutePath", index.Path);
                    writer.WriteString("bundle", plan.Bundle.Root);
                    writer.WriteString("status", Status(index.Status));
                    writer.WriteBoolean("drift", index.IsDrift);
                    writer.WriteBoolean("bundleRoot", index.IsBundleRoot);
                    writer.WriteNumber("entries", index.Entries.Count);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }) + Environment.NewLine;
    }

    private static string Status(OkfIndexStatus status) => status switch
    {
        OkfIndexStatus.Created => "created",
        OkfIndexStatus.Unchanged => "unchanged",
        OkfIndexStatus.Drifted => "drifted",
        OkfIndexStatus.Foreign => "foreign",
        OkfIndexStatus.Orphaned => "orphaned",
        _ => "unknown",
    };

    private static string WriteVerb(OkfIndexStatus status) => status switch
    {
        OkfIndexStatus.Created => "created",
        OkfIndexStatus.Drifted => "updated",
        OkfIndexStatus.Foreign => "replaced (was hand-written)",
        OkfIndexStatus.Orphaned => "orphaned: nothing left to index, left in place",
        _ => "unchanged",
    };

    private static string CheckVerb(OkfIndexStatus status) => status switch
    {
        OkfIndexStatus.Orphaned => "orphaned: generated index for a directory with nothing left to index",
        _ => "out of date",
    };

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf index [path] [options]

            Generates the `index.md` files for every directory in a bundle that holds
            concepts (OKF v0.2 §8). Generation is deterministic and offline: the same tree
            always renders the same bytes, so running twice changes nothing.

            Every generated file carries a `<!-- generated by okf -->` marker. Only marked
            files are ever reported as drifted or overwritten silently — a hand-styled
            index from a foreign bundle is reported as such before it is replaced, and is
            never reported as drift.

            Arguments:
              path                          A bundle root, a vault (a directory holding
                                            bundles/), or a project root (holding
                                            okf/bundles/). Defaults to the vault found by
                                            walking up from the working directory, then to
                                            the personal vault (OKF_HOME, else ~/okf).

            Options:
              --check                       Write nothing; report generated indexes that no
                                            longer match the tree and exit 1 if any do
              --format <text|json>          Output format (default: text)
              --json                        Alias for --format json
              --verbose, -v                 Report vault resolution
              --help, -h                    Show this help

            Exit codes:
              0  indexes written, or --check found no drift
              1  --check found drift
              2  usage or environment failure
            """);
    }
}
