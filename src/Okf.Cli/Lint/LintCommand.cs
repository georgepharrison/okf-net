using Okf.Core;

namespace Okf.Cli.Lint;

/// <summary>
/// <c>okf lint [path]</c> — walks the resolved bundles, applies the rules, and reports
/// diagnostics (PRD CLI-5 … CLI-7, CLI-14, CLI-15).
/// </summary>
internal static class LintCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>lint</c>.</param>
    /// <param name="environment">The environment to resolve vaults and configuration against.</param>
    /// <param name="output">Where diagnostics and the summary go.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        LintArguments parsed;
        try
        {
            parsed = LintArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf lint --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        if (parsed.ListRules)
        {
            WriteRules(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Lint(parsed, environment, output, error);
        }
        catch (OkfDiscoveryException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
        catch (OkfConfigException exception)
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

    private static int Lint(
        LintArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        OkfWorkingSet workingSet = OkfDiscovery.Resolve(arguments.Path, environment);

        // PRD CLI-4: CLI args > project config > global config, with the built-in
        // defaults underneath. OKF_HOME is not a layer here — it moves the personal
        // vault (and therefore which bundles are linted), never a severity.
        List<OkfSeverityLayer> layers = new List<OkfSeverityLayer>();
        OkfConfig? global = OkfConfig.TryLoad(environment.GlobalConfigPath, globalLayer: true);
        string? projectPath = arguments.ConfigPath is { } explicitConfig
            ? Path.GetFullPath(Path.Combine(environment.CurrentDirectory, explicitConfig))
            : workingSet.ProjectConfigPath;

        if (arguments.ConfigPath is not null && !File.Exists(projectPath))
        {
            throw new OkfConfigException($"No such config file: '{projectPath}'.");
        }

        OkfConfig? project = projectPath is null ? null : OkfConfig.TryLoad(projectPath);

        foreach (OkfConfig? config in new[] { global, project })
        {
            if (config is not null && !config.Severities.IsEmpty)
            {
                layers.Add(config.Severities);
            }
        }

        if (!arguments.CommandLine.IsEmpty)
        {
            layers.Add(arguments.CommandLine);
        }

        OkfLintOptions options = new OkfLintOptions
        {
            Severities = new OkfSeverityResolver(layers),
            TagRegistry = project?.TagRegistry ?? global?.TagRegistry,

            // The vault, not a bundle: `raw/` sits outside every bundle root, so OKF0310
            // is the one rule whose scope is the whole vault (decisions.md Q3). A working
            // set with no vault — a foreign bundle handed over by path — leaves it null,
            // and the rule reports as inapplicable rather than failing (PRD CLI-9).
            VaultRoot = workingSet.VaultRoot,
            Today = DateOnly.FromDateTime(DateTime.Now),
        };

        if (arguments.Verbose)
        {
            WriteVerbose(error, workingSet, global, project, projectPath, options.Severities);
        }

        OkfLintResult result = new OkfLinter(options).Lint(workingSet.Bundles);
        List<OkfDiagnostic> reported = result.Diagnostics.Where(d => d.Severity != OkfSeverity.Hidden).ToList();

        if (arguments.Json)
        {
            output.Write(DiagnosticWriter.ToJson(reported, environment.CurrentDirectory));
        }
        else
        {
            DiagnosticWriter.WriteText(reported, result, environment.CurrentDirectory, output);
        }

        if (arguments.Verbose)
        {
            // The run metadata goes to stderr, not into the JSON. `--json` emits a bare
            // array (PRD CLI-11/CLI-15, and MCP-3 pins the same shape for search), so
            // wrapping it in an envelope to carry counts would break every consumer that
            // reads element 0 as a diagnostic. stderr is already where `--verbose` speaks
            // and is never part of the machine-readable contract.
            WriteRunMetadata(error, result);
        }

        return result.HasErrors ? CliApplication.ExitDiagnostics : CliApplication.ExitSuccess;
    }

    private static void WriteVerbose(
        TextWriter error,
        OkfWorkingSet workingSet,
        OkfConfig? global,
        OkfConfig? project,
        string? projectPath,
        OkfSeverityResolver severities)
    {
        VerboseReport.WorkingSet(error, workingSet);

        error.WriteLine(global is null
            ? "okf: global config: none"
            : $"okf: global config: {global.Source}");
        error.WriteLine(project is null
            ? $"okf: project config: none{(projectPath is null ? string.Empty : $" (looked for {projectPath})")}"
            : $"okf: project config: {project.Source}");

        // Every rule, not only the reconfigured ones: a rule sitting at a default the
        // reader did not expect is exactly as surprising as one a config layer moved, and
        // "which rules are live" is the question a clean run raises (friction #11).
        foreach (OkfRule rule in OkfRules.All)
        {
            (OkfSeverity severity, string source) = severities.ResolveWithSource(rule.Id);
            error.WriteLine(
                $"okf: severity {rule.Id} = {severity.ToConfigString()} (from {source}) {rule.Nickname}");
        }
    }

    private static void WriteRunMetadata(TextWriter error, OkfLintResult result)
    {
        error.WriteLine(
            $"okf: checked {DiagnosticWriter.Plural(result.FileCount, "file")} in " +
            $"{DiagnosticWriter.Plural(result.Bundles.Count, "bundle")}");
        error.WriteLine(
            $"okf: {DiagnosticWriter.Plural(OkfRules.All.Count, "rule")}: " +
            $"{result.ActiveRuleCount} active, {result.HiddenRuleCount} hidden");
        error.WriteLine(
            $"okf: diagnostics {DiagnosticWriter.Plural(result.Count(OkfSeverity.Error), "error")}, " +
            $"{DiagnosticWriter.Plural(result.Count(OkfSeverity.Warning), "warning")}, " +
            $"{DiagnosticWriter.Plural(result.Count(OkfSeverity.Info), "info", "infos")}, " +
            $"{result.Count(OkfSeverity.Hidden)} at hidden severity");
    }

    private static void WriteRules(TextWriter output)
    {
        foreach (OkfRule rule in OkfRules.All)
        {
            output.WriteLine($"{rule.Id}  {rule.DefaultSeverity.ToConfigString(),-7}  {rule.Nickname}");
            output.WriteLine($"          {rule.Title}");
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf lint [path] [options]

            Validates OKF v0.2 §11 conformance plus the configured warning set. Only §11
            conformance failures are errors by default; everything else is consumer
            configuration.

            Arguments:
              path                          A bundle root, a vault (a directory holding
                                            bundles/), or a project root (holding
                                            okf/bundles/). Defaults to the vault found by
                                            walking up from the working directory, then to
                                            the personal vault (OKF_HOME, else ~/okf).

            Options:
              --format <text|json>          Output format (default: text)
              --json                        Alias for --format json
              --severity <OKF####=level>    Override one rule's severity; repeatable.
                                            Levels: hidden, info, warning, error
              --treat-all-warnings-as-errors
                                            Promote every rule at warning severity to error
              --config <path>               Use this project config instead of the vault's
              --list-rules                  List every diagnostic id and its default severity
              --verbose, -v                 Report vault resolution and effective configuration
              --help, -h                    Show this help

            Exit codes:
              0  no diagnostics at error severity
              1  diagnostics at error severity
              2  usage or environment failure
            """);
    }
}
