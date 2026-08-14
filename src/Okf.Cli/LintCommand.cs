using Okf.Core;

namespace Okf.Cli;

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
        var workingSet = OkfDiscovery.Resolve(arguments.Path, environment);

        // PRD CLI-4: CLI args > project config > global config, with the built-in
        // defaults underneath. OKF_HOME is not a layer here — it moves the personal
        // vault (and therefore which bundles are linted), never a severity.
        var layers = new List<OkfSeverityLayer>();
        var global = OkfConfig.TryLoad(environment.GlobalConfigPath);
        var projectPath = arguments.ConfigPath is { } explicitConfig
            ? Path.GetFullPath(Path.Combine(environment.CurrentDirectory, explicitConfig))
            : workingSet.ProjectConfigPath;

        if (arguments.ConfigPath is not null && !File.Exists(projectPath))
        {
            throw new OkfConfigException($"No such config file: '{projectPath}'.");
        }

        var project = projectPath is null ? null : OkfConfig.TryLoad(projectPath);

        foreach (var config in new[] { global, project })
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

        var options = new OkfLintOptions
        {
            Severities = new OkfSeverityResolver(layers),
            TagRegistry = project?.TagRegistry ?? global?.TagRegistry,
            Today = DateOnly.FromDateTime(DateTime.Now),
        };

        if (arguments.Verbose)
        {
            WriteVerbose(error, workingSet, global, project, projectPath, options.Severities);
        }

        var result = new OkfLinter(options).Lint(workingSet.Bundles);
        var reported = result.Diagnostics.Where(d => d.Severity != OkfSeverity.Hidden).ToList();

        if (arguments.Json)
        {
            output.Write(DiagnosticWriter.ToJson(reported, environment.CurrentDirectory));
        }
        else
        {
            DiagnosticWriter.WriteText(reported, result, environment.CurrentDirectory, output);
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
        error.WriteLine($"okf: resolved {workingSet.Resolution}");
        foreach (var bundle in workingSet.Bundles)
        {
            error.WriteLine($"okf: bundle {bundle.Root}");
        }

        error.WriteLine(global is null
            ? "okf: global config: none"
            : $"okf: global config: {global.Source}");
        error.WriteLine(project is null
            ? $"okf: project config: none{(projectPath is null ? string.Empty : $" (looked for {projectPath})")}"
            : $"okf: project config: {project.Source}");

        foreach (var rule in OkfRules.All)
        {
            var (severity, source) = severities.ResolveWithSource(rule.Id);
            if (severity != rule.DefaultSeverity)
            {
                error.WriteLine($"okf: severity {rule.Id} = {severity.ToConfigString()} (from {source})");
            }
        }
    }

    private static void WriteRules(TextWriter output)
    {
        foreach (var rule in OkfRules.All)
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
