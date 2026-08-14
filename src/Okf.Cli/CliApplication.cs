using System.Reflection;
using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// Command dispatch for the <c>okf</c> binary. The CLI is a thin wrapper over
/// <c>Okf.Core</c> (decisions.md §4): it parses arguments, resolves configuration, and
/// renders results — every rule and every judgement lives in the library.
/// </summary>
/// <remarks>
/// Argument parsing is hand-rolled rather than taken from a library. The surface is a
/// handful of flags, the binary must publish NativeAOT-clean with no reflection on the
/// path (PRD CLI-17), and the exit-code contract (CLI-14) is specific enough that a
/// framework's own usage/error paths would have to be overridden anyway.
/// </remarks>
internal static class CliApplication
{
    /// <summary>Success: no diagnostics at error severity (PRD CLI-14).</summary>
    public const int ExitSuccess = 0;

    /// <summary>Diagnostics at error severity were reported (PRD CLI-14).</summary>
    public const int ExitDiagnostics = 1;

    /// <summary>Usage or environment failure (PRD CLI-14).</summary>
    public const int ExitUsage = 2;

    /// <summary>Runs one command.</summary>
    /// <param name="args">The command-line arguments, without the executable name.</param>
    /// <param name="environment">The environment to resolve vaults and configuration against.</param>
    /// <param name="output">Where results go.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            WriteUsage(error);
            return ExitUsage;
        }

        switch (args[0])
        {
            case "help":
            case "--help":
            case "-h":
                WriteUsage(output);
                return ExitSuccess;

            case "version":
            case "--version":
                output.WriteLine(Version);
                return ExitSuccess;

            case "lint":
                return LintCommand.Run(args[1..], environment, output, error);

            default:
                error.WriteLine($"okf: error: unknown command '{args[0]}'.");
                WriteUsage(error);
                return ExitUsage;
        }
    }

    /// <summary>The binary's informational version.</summary>
    public static string Version =>
        typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf — tooling for OKF v0.2 knowledge bundles.

            Usage:
              okf lint [path] [options]   Validate §11 conformance plus the configured warning set
              okf help                    Show this help
              okf version                 Show the version

            Run `okf lint --help` for the lint options.
            """);
    }
}
