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
    /// <param name="input">
    /// The input stream, used only by <c>okf mcp</c>, which reads JSON-RPC requests from it.
    /// Unset means no input: every other command reads files, never stdin.
    /// </param>
    /// <returns>The process exit code.</returns>
    public static int Run(
        string[] args,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error,
        TextReader? input = null)
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
                output.WriteLine(VersionDisplay);
                return ExitSuccess;

            case "init":
                return InitCommand.Run(args[1..], environment, output, error);

            case "lint":
                return LintCommand.Run(args[1..], environment, output, error);

            case "index":
                return IndexCommand.Run(args[1..], environment, output, error);

            case "search":
                return SearchCommand.Run(args[1..], environment, output, error);

            case "inbox":
                return InboxCommand.Run(args[1..], environment, output, error);

            case "verify":
                return VerifyCommand.Run(args[1..], environment, output, error);

            case "bundle":
                return BundleCommand.Run(args[1..], environment, output, error);

            case "skills":
                return SkillsCommand.Run(args[1..], environment, output, error);

            case "site":
                return SiteCommand.Run(args[1..], environment, output, error);

            case "mcp":
                return McpCommand.Run(args[1..], environment, input ?? TextReader.Null, output, error);

            default:
                error.WriteLine($"okf: error: unknown command '{args[0]}'.");
                WriteUsage(error);
                return ExitUsage;
        }
    }

    /// <summary>
    /// The version reported when nothing stamped the assembly at all — a missing or empty
    /// attribute, which no `dotnet build` of this repo produces (Directory.Build.props
    /// defaults to <c>0.0.0-dev</c>) but a hand-assembled binary might.
    /// </summary>
    internal const string UnknownVersion = "0.0.0";

    /// <summary>
    /// What <c>okf version</c> prints: the full informational version, including the
    /// <c>+&lt;short-sha&gt;</c> build metadata a stamped build carries. A bug report
    /// quoting this line names the exact commit the binary was built from, which
    /// `1.0.0-rc.14` alone does not (an rc tag can be rebuilt).
    /// </summary>
    public static string VersionDisplay => Describe(InformationalVersion);

    /// <summary>
    /// The semantic version, without build metadata — what MCP reports as
    /// <c>serverInfo.version</c>, where a client may compare or parse it.
    /// </summary>
    public static string Version => SemanticVersion(InformationalVersion);

    private static string? InformationalVersion =>
        typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

    /// <summary>Renders the informational version for display, metadata and all.</summary>
    internal static string Describe(string? informationalVersion) =>
        informationalVersion is { Length: > 0 } value ? value : UnknownVersion;

    /// <summary>Drops the <c>+</c> build metadata from an informational version.</summary>
    internal static string SemanticVersion(string? informationalVersion)
    {
        var described = Describe(informationalVersion);
        var metadata = described.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? described : described[..metadata];
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf — tooling for OKF v0.2 knowledge bundles.

            Usage:
              okf init [path] [options]   Scaffold a vault: bundles/, raw/, custodian/, and the config
              okf lint [path] [options]   Validate §11 conformance plus the configured warning set
              okf index [path] [options]  Generate the index.md files for a bundle (--check to verify)
              okf search <query> [path]   Search the resolved bundles, ranked and links-first
              okf inbox [path]            List the concepts waiting on a person
              okf verify <concept>...     Stamp human verification on one or more concepts
              okf bundle [path] --out <f> Package the bundles for consume-only distribution
              okf site [path] --out <dir> Render the bundles as a static site (dashboard, graph, pages)
              okf skills <list|path|install>
                                          The agent skills this binary carries, and where they install
              okf mcp [path]              Run the read-only MCP server over stdio
              okf help                    Show this help
              okf version                 Show the version

            Run `okf <command> --help` for a command's options.
            """);
    }
}
