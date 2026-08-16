using System.Globalization;
using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// <c>okf bundle [path]</c> — packages a vault's bundles for consume-only distribution,
/// and <c>--verify</c>s one that was packaged earlier (PRD §5, decisions.md §1).
/// </summary>
/// <remarks>
/// The command ships <c>bundles/</c> and nothing else: no custodian directory, no
/// <c>raw/</c>, no <c>okf.json</c>, no repo-facing README. What a consumer receives is
/// readable markdown plus one <c>okf-bundle.json</c> at the root recording what was
/// packaged, which links now dangle, and the SHA-256 of every file shipped.
/// </remarks>
internal static class BundleCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>bundle</c>.</param>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors, warnings, and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        BundleArguments parsed;
        try
        {
            parsed = BundleArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf bundle --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return parsed.Verify is { } target
                ? Verify(target, environment, output, error)
                : Package(parsed, environment, output, error);
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

    private static int Package(
        BundleArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        // PRD CLI-1: the same working set every other command resolves.
        var workingSet = OkfDiscovery.Resolve(arguments.Path, environment);
        var plan = OkfBundler.Plan(
            workingSet,
            new OkfBundlerOptions
            {
                Bundles = arguments.Bundles,
                GeneratedAt = arguments.GeneratedAt ?? DateTimeOffset.UtcNow,

                // The §7 actor form, carrying the semantic version and not the build
                // metadata: an archive should be reproducible from a tag, and which
                // machine compiled the binary is not part of what was packaged.
                Generator = $"okf/{CliApplication.Version}",
            });

        var format = arguments.EffectiveFormat();
        var destination = Path.GetFullPath(Path.Combine(environment.CurrentDirectory, arguments.Output!));

        if (arguments.Verbose)
        {
            error.WriteLine($"okf: resolved {workingSet.Resolution}");
            foreach (var bundle in plan.Bundles)
            {
                error.WriteLine($"okf: packaging {bundle.Root}");
            }

            error.WriteLine($"okf: writing {Name(format)} to {destination}");
        }

        OkfBundler.Write(plan, destination, format);

        // §6.1 makes a dangling link legal and obliges consumers to tolerate one, so this
        // is a warning and never a refusal — but an unannounced dangling link is a
        // consumer's surprise, so every one of them is said out loud here and recorded in
        // the manifest.
        foreach (var link in plan.ExternalLinks)
        {
            error.WriteLine($"okf: warning: {Describe(link)}");
        }

        output.WriteLine(
            $"Packaged {DiagnosticWriter.Plural(plan.Bundles.Count, "bundle")} " +
            $"({DiagnosticWriter.Plural(plan.Entries.Count, "file")}, {Size(plan.TotalBytes)}) " +
            $"into {DiagnosticWriter.Display(destination, environment.CurrentDirectory)}");

        output.WriteLine(plan.ExternalLinks.Count == 0
            ? "No link leaves the packaged bundles."
            : $"{DiagnosticWriter.Plural(plan.ExternalLinks.Count, "link")} " +
              $"{(plan.ExternalLinks.Count == 1 ? "leaves" : "leave")} the packaged bundles and " +
              $"{(plan.ExternalLinks.Count == 1 ? "is" : "are")} recorded in {OkfDistributionManifest.FileName}.");

        return arguments.Lint
            ? Lint(plan, destination, format, output, error)
            : CliApplication.ExitSuccess;
    }

    /// <summary>
    /// Lints what shipped, as a stranger would: the packaged bundles alone, with no
    /// <c>okf.json</c> to promote anything and no vault around them, so only the built-in
    /// severities apply and only §11 conformance can fail. A distribution that carries
    /// cross-bundle links reports <c>OKF0309</c> at info, which is the expected shape
    /// rather than a defect.
    /// </summary>
    private static int Lint(
        OkfDistributionPlan plan,
        string destination,
        OkfDistributionFormat format,
        TextWriter output,
        TextWriter error)
    {
        var directory = format == OkfDistributionFormat.Directory ? destination : null;
        var temporary = directory is null
            ? Path.Combine(Path.GetTempPath(), "okf-bundle", Path.GetRandomFileName())
            : null;

        try
        {
            if (temporary is not null)
            {
                // The same writer that produced the archive, so what is linted is what was
                // packaged rather than a second rendering of it.
                OkfBundler.Write(plan, temporary, OkfDistributionFormat.Directory);
                directory = temporary;
            }

            var bundles = plan.Bundles
                .Select(bundle => new OkfBundle(
                    Path.Combine(directory!, OkfDiscovery.BundlesDirectoryName, bundle.Name)))
                .ToList();

            var result = new OkfLinter(new OkfLintOptions()).Lint(bundles);
            var reported = result.Diagnostics.Where(d => d.Severity != OkfSeverity.Hidden).ToList();

            output.WriteLine();
            output.WriteLine("Linting the distribution as a consumer would (default severities, no config):");
            DiagnosticWriter.WriteText(reported, result, directory!, output);

            if (!result.HasErrors)
            {
                return CliApplication.ExitSuccess;
            }

            error.WriteLine(
                "okf: error: the packaged distribution does not conform; it was written, and it should not be shipped.");
            return CliApplication.ExitDiagnostics;
        }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static int Verify(string target, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        var full = Path.GetFullPath(Path.Combine(environment.CurrentDirectory, target));
        var result = OkfBundler.Verify(full);
        var display = DiagnosticWriter.Display(full, environment.CurrentDirectory);

        foreach (var finding in result.Findings)
        {
            output.WriteLine($"{finding.Path}: {Name(finding.Issue)}: {finding.Detail}");
        }

        if (result.Manifest is { } manifest)
        {
            output.WriteLine(
                $"Verified {DiagnosticWriter.Plural(result.Checked, "file")} in {display} " +
                $"(packaged by {manifest.Generator} at {manifest.GeneratedAt}): " +
                (result.IsValid
                    ? "every file matches its recorded sha256."
                    : $"{DiagnosticWriter.Plural(result.Findings.Count, "problem")}."));
        }

        if (result.IsValid)
        {
            return CliApplication.ExitSuccess;
        }

        error.WriteLine($"okf: error: {display} is not what its {OkfDistributionManifest.FileName} says it is.");
        return CliApplication.ExitDiagnostics;
    }

    private static string Describe(OkfExternalLink link)
    {
        var location = link.Line > 0
            ? $"{link.From}:{link.Line.ToString(CultureInfo.InvariantCulture)}"
            : link.From;

        return link.Bundle is { } bundle
            ? $"{location} links `{link.To}` into bundle `{bundle}`, which this distribution does not carry; " +
              "the link will dangle (§6.1)."
            : $"{location} links `{link.To}`, which is outside every packaged bundle; the link will dangle (§6.1).";
    }

    private static string Name(OkfDistributionFormat format) => format switch
    {
        OkfDistributionFormat.Zip => "zip",
        OkfDistributionFormat.Directory => "directory",
        _ => "tar.gz",
    };

    private static string Name(OkfDistributionIssue issue) => issue switch
    {
        OkfDistributionIssue.Missing => "missing",
        OkfDistributionIssue.Modified => "modified",
        OkfDistributionIssue.Unlisted => "unlisted",
        _ => "unreadable",
    };

    private static string Size(long bytes) =>
        bytes < 1024
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : $"{(bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB";

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf bundle [path] --out <file-or-directory> [options]
            okf bundle --verify <archive-or-directory>

            Packages a vault's bundles for consume-only distribution. What ships is
            `bundles/<name>/**` — concepts, indexes, log.md, about files, references/ —
            and one `okf-bundle.json` at the root. What never ships: raw/, custodian/,
            okf.json, the vault README, named tool state (.git/, .obsidian/, .DS_Store and
            their peers), and editor droppings. A dot-prefixed file a producer wrote is
            content and ships, because `okf lint` judged it.

            The archive is deterministic: entries are sorted, timestamps fixed, modes
            normalized. The same vault packaged with the same --generated-at is
            byte-identical, which is what a release artifact needs.

            Links from a packaged bundle into one you left out are left dangling — legal
            under §6.1, which obliges consumers to tolerate a broken link — and every one
            of them is warned about here and recorded in okf-bundle.json. Nothing is
            vendored: a copied concept would duplicate its trust state and rot silently.

            Arguments:
              path                          A vault (a directory holding bundles/), a
                                            project root (holding okf/bundles/), or a
                                            single bundle root. Defaults to the vault found
                                            by walking up from the working directory.

            Options:
              --out, -o <path>              Where to write the distribution (required)
              --format <tar.gz|zip|dir>     Output shape (default: from --out, else tar.gz)
              --bundle <name>               Package only this bundle; repeatable
              --lint                        Lint the result as a consumer would see it:
                                            default severities, no config, no vault
              --generated-at <instant>      Stamp the manifest with this RFC 3339 instant
                                            instead of now, for a reproducible archive
              --verify <path>               Re-hash a packaged archive or directory against
                                            its own manifest and report; packages nothing
              --verbose, -v                 Report resolution and what is being written
              --help, -h                    Show this help

            Exit codes:
              0  packaged (and, with --lint, conformant); or verified clean
              1  --verify found a mismatch, or --lint found errors
              2  usage or environment failure
            """);
    }
}
