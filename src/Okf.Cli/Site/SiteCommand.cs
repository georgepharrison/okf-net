using System.Globalization;
using System.Text;
using Okf.Core;

namespace Okf.Cli.Site;

/// <summary>
/// <c>okf site [path] --out &lt;dir&gt;</c> — renders the resolved bundles as a static site:
/// a trust dashboard whose tiles are filters, a force-directed graph, and one browsable page
/// per concept. The output is self-contained and makes no network request, so it hosts on
/// GitLab Pages and opens straight from <c>file://</c>.
/// </summary>
internal static class SiteCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>site</c>.</param>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        SiteArguments parsed;
        try
        {
            parsed = SiteArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf site --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Generate(parsed, environment, output, error);
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

    private static int Generate(
        SiteArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        // PRD CLI-1: the same working-set resolution as `okf lint` and `okf index`.
        var workingSet = OkfDiscovery.Resolve(arguments.Path, environment);
        var outputDirectory = Path.GetFullPath(
            Path.Combine(environment.CurrentDirectory, arguments.Out!));

        if (File.Exists(outputDirectory))
        {
            error.WriteLine($"okf: error: '{outputDirectory}' is a file; --out names a directory.");
            return CliApplication.ExitUsage;
        }

        foreach (var bundle in workingSet.Bundles)
        {
            // Writing a site into the bundle it was generated from would make the next run
            // read its own output — and `okf lint` would then find a tree full of HTML.
            if (outputDirectory.StartsWith(bundle.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(outputDirectory, bundle.Root, StringComparison.Ordinal))
            {
                error.WriteLine(
                    $"okf: error: --out '{outputDirectory}' is inside bundle '{bundle.Root}'. " +
                    "Generate the site outside the bundles it renders.");
                return CliApplication.ExitUsage;
            }
        }

        if (arguments.Verbose)
        {
            VerboseReport.WorkingSet(error, workingSet);

            error.WriteLine(arguments.SingleFile
                ? $"okf: writing one self-contained file into {outputDirectory}"
                : $"okf: writing a multi-page site into {outputDirectory}");
        }

        var plan = OkfSiteGenerator.Plan(workingSet, new OkfSiteOptions
        {
            Name = arguments.Name,
            SingleFile = arguments.SingleFile,
            Today = DateOnly.FromDateTime(DateTime.Now),
        });

        // The directory check above catches `--out` pointing *into* a bundle. It does not
        // catch `--out` pointing at a bundle's parent, where the site's own per-bundle
        // subdirectory lands back inside it: `--out <vault>/bundles` writes
        // `<vault>/bundles/<slug>/**.html` straight into the bundle slugged `<slug>`. The
        // planned paths answer that exactly, and nothing has been written yet.
        if (Collision(plan, outputDirectory, workingSet) is { } collision)
        {
            error.WriteLine(
                $"okf: error: --out '{outputDirectory}' would write '{collision.Path}' " +
                $"inside bundle '{collision.Root}'. " +
                "Generate the site outside the bundles it renders.");
            return CliApplication.ExitUsage;
        }

        OkfSiteGenerator.Apply(plan, outputDirectory);

        var landing = Path.Combine(outputDirectory, OkfSiteBuilder.IndexHref);
        if (arguments.Json)
        {
            output.Write(ToJson(plan, outputDirectory, landing));
        }
        else
        {
            WriteReport(plan, outputDirectory, landing, environment.CurrentDirectory, output);
        }

        return CliApplication.ExitSuccess;
    }

    /// <summary>The first planned file that would land inside a bundle being rendered.</summary>
    /// <param name="plan">The rendered site.</param>
    /// <param name="outputDirectory">The absolute output directory.</param>
    /// <param name="workingSet">The bundles being rendered.</param>
    /// <returns>The offending path and the bundle it falls in, or <see langword="null" />.</returns>
    private static (string Path, string Root)? Collision(
        OkfSitePlan plan,
        string outputDirectory,
        OkfWorkingSet workingSet)
    {
        foreach (var file in plan.Files)
        {
            var path = Path.GetFullPath(
                Path.Combine(outputDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar)));

            foreach (var bundle in workingSet.Bundles)
            {
                if (path.StartsWith(bundle.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    return (path, bundle.Root);
                }
            }
        }

        return null;
    }

    private static void WriteReport(
        OkfSitePlan plan,
        string outputDirectory,
        string landing,
        string baseDirectory,
        TextWriter output)
    {
        var counts = plan.Model.Counts;
        var summary = new StringBuilder()
            .Append("Generated ")
            .Append(DiagnosticWriter.Plural(plan.Files.Count, "file"))
            .Append(" in ")
            .Append(DiagnosticWriter.Display(outputDirectory, baseDirectory))
            .Append(": ")
            .Append(DiagnosticWriter.Plural(counts.Concepts, "concept"))
            .Append(" in ")
            .Append(DiagnosticWriter.Plural(counts.Bundles, "bundle"))
            .Append('.');

        output.WriteLine(summary.ToString());
        output.WriteLine(
            $"Trust: {Number(counts.HumanReviewed)} human-reviewed, " +
            $"{Number(counts.MachineConfirmed)} machine-confirmed, " +
            $"{Number(counts.Unverified)} unverified; " +
            $"{Number(counts.Stale)} stale, {Number(counts.Draft)} draft.");
        output.WriteLine($"Open {DiagnosticWriter.Display(landing, baseDirectory)}");
    }

    private static string ToJson(OkfSitePlan plan, string outputDirectory, string landing)
    {
        var counts = plan.Model.Counts;
        return JsonOutput.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("out", outputDirectory);
            writer.WriteString("entry", landing);
            writer.WriteString("name", plan.Model.Name);
            writer.WriteBoolean("singleFile", plan.Model.SingleFile);
            writer.WriteNumber("files", plan.Files.Count);
            writer.WriteNumber("pages", plan.Model.Pages.Count);
            writer.WriteNumber("edges", plan.Model.Edges.Count);

            writer.WriteStartObject("counts");
            writer.WriteNumber("bundles", counts.Bundles);
            writer.WriteNumber("concepts", counts.Concepts);
            writer.WriteNumber("humanReviewed", counts.HumanReviewed);
            writer.WriteNumber("machineConfirmed", counts.MachineConfirmed);
            writer.WriteNumber("unverified", counts.Unverified);
            writer.WriteNumber("stale", counts.Stale);
            writer.WriteNumber("draft", counts.Draft);
            writer.WriteEndObject();

            writer.WriteStartArray("bundles");
            foreach (var bundle in plan.Model.Bundles)
            {
                writer.WriteStartObject();
                writer.WriteString("name", bundle.Name);
                writer.WriteString("slug", bundle.Slug);
                writer.WriteNumber("concepts", bundle.Concepts);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }) + Environment.NewLine;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf site [path] --out <dir> [options]

            Renders the resolved bundles as a static website: a trust dashboard whose
            tiles are clickable filters (bundles, concepts, human-reviewed,
            machine-confirmed, unverified, stale, draft), a force-directed graph of
            cross-links coloured by trust tier, and one browsable page per concept with
            its frontmatter, its sources, and a "Cited by" backlinks list.

            The output is self-contained: the stylesheet and the client script ship
            inside this binary, no page requests anything over the network, and every
            link is relative. That makes the same directory hostable on GitLab Pages and
            openable straight from the filesystem.

            Arguments:
              path                          A bundle root, a vault (a directory holding
                                            bundles/), or a project root (holding
                                            okf/bundles/). Defaults to the vault found by
                                            walking up from the working directory, then to
                                            the personal vault (OKF_HOME, else ~/okf).

            Options:
              --out <dir>, -o <dir>         Where to write the site. Required. Created if
                                            absent; existing files it does not generate
                                            are left alone.
              --name <title>                The site's display name (default: the vault's
                                            directory name)
              --single-file                 Emit the whole site as one index.html, with
                                            every concept embedded, for handing someone a
                                            knowledge base as a single attachment
              --format <text|json>          Output format (default: text)
              --json                        Alias for --format json
              --verbose, -v                 Report vault resolution
              --help, -h                    Show this help

            Exit codes:
              0  the site was written
              2  usage or environment failure
            """);
    }
}
