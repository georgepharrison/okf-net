using System.Linq;
using System.Reflection;
using Okf.Core;

namespace Okf.Cli.Shared;

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

        // The flag spellings of the two verbs that have them; everything else is a verb or
        // nothing.
        string verb = args[0] switch
        {
            "--help" or "-h" => "help",
            "--version" => "version",
            var word => word,
        };

        foreach ((string Verb, CommandRunner Run) command in Commands)
        {
            if (string.Equals(command.Verb, verb, StringComparison.Ordinal))
            {
                return command.Run(args[1..], environment, output, error, input ?? TextReader.Null);
            }
        }

        error.WriteLine($"okf: error: unknown command '{args[0]}'.");
        WriteUsage(error);
        return ExitUsage;
    }

    /// <summary>What one verb's handler looks like.</summary>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="environment">The environment to resolve vaults and configuration against.</param>
    /// <param name="output">Where results go.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <param name="input">The input stream, which only <c>okf mcp</c> reads.</param>
    /// <returns>The process exit code.</returns>
    internal delegate int CommandRunner(
        string[] args,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error,
        TextReader input);

    /// <summary>
    /// The verb table. It is the dispatch — <see cref="Run" /> looks a verb up here and
    /// nowhere else — and it is what <see cref="CompletionTable" /> is held to, so a verb
    /// cannot ship without a completion entry (issue #51).
    /// </summary>
    internal static readonly (string Verb, CommandRunner Run)[] Commands =
    [
        ("init", static (args, environment, output, error, _) => InitCommand.Run(args, environment, output, error)),
        ("lint", static (args, environment, output, error, _) => LintCommand.Run(args, environment, output, error)),
        ("index", static (args, environment, output, error, _) => IndexCommand.Run(args, environment, output, error)),
        ("search", static (args, environment, output, error, _) => SearchCommand.Run(args, environment, output, error)),
        ("register",
            static (args, environment, output, error, _) => RegistryCommand.Register(args, environment, output, error)),
        ("unregister",
            static (args, environment, output, error, _) => RegistryCommand.Unregister(args, environment, output, error)),
        ("registry",
            static (args, environment, output, error, _) => RegistryCommand.Registry(args, environment, output, error)),
        ("inbox", static (args, environment, output, error, _) => InboxCommand.Run(args, environment, output, error)),
        ("verify", static (args, environment, output, error, _) => VerifyCommand.Run(args, environment, output, error)),
        ("capture", static (args, environment, output, error, _) => CaptureCommand.Run(args, environment, output, error)),
        ("generated",
            static (args, environment, output, error, _) => GeneratedCommand.Run(args, environment, output, error)),
        ("bundle", static (args, environment, output, error, _) => BundleCommand.Run(args, environment, output, error)),
        ("site", static (args, environment, output, error, _) => SiteCommand.Run(args, environment, output, error)),
        ("skills", static (args, environment, output, error, _) => SkillsCommand.Run(args, environment, output, error)),
        ("mcp", static (args, environment, output, error, input) =>
            McpCommand.Run(args, environment, input, output, error)),
        ("upgrade",
            static (args, environment, output, error, _) => UpgradeCommand.Run(args, environment, output, error)),
        ("completion", static (args, _, output, error, _) => CompletionCommand.Run(args, output, error)),
        ("help", static (_, _, output, _, _) =>
        {
            WriteUsage(output);
            return ExitSuccess;
        }),
        ("version", static (args, _, output, _, _) =>
        {
            output.WriteLine(VersionDisplay);
            if (args.Length > 0 && args[0] is "--verbose" or "-v")
            {
                output.WriteLine($"commit: {CommitDisplay}");
            }

            return ExitSuccess;
        }),
    ];

    /// <summary>Every verb the CLI dispatches, in the order the table declares them.</summary>
    internal static IReadOnlyList<string> Verbs { get; } = [.. Commands.Select(command => command.Verb)];

    /// <summary>
    /// Every option <c>okf version</c> accepts (see <see cref="InitArguments.Flags" />). It
    /// parses its own single flag inline, so there is no <c>VersionArguments</c> to hold it.
    /// </summary>
    internal static readonly string[] VersionFlags = ["--verbose", "-v"];

    /// <summary><c>okf help</c> takes no options.</summary>
    internal static readonly string[] HelpFlags = [];

    /// <summary>
    /// The version reported when nothing stamped the assembly at all — a missing or empty
    /// attribute, which no `dotnet build` of this repo produces (Directory.Build.props
    /// defaults to <c>0.0.0-dev</c>) but a hand-assembled binary might.
    /// </summary>
    internal const string UnknownVersion = "0.0.0";

    /// <summary>
    /// What <c>okf version</c> prints: the informational version exactly as stamped. A
    /// build the `publish` job cut from a tag carries the bare version — the tag already
    /// identifies the commit (issue #53) — while an untagged local build (`mise run
    /// publish-aot`, a plain `dotnet build`) still carries <c>+&lt;sha&gt;</c>, because
    /// nothing else says where it came from. Either way, <c>--verbose</c> names the commit
    /// on a second line, so a bug report is never a question short.
    /// </summary>
    public static string VersionDisplay => Describe(InformationalVersion);

    /// <summary>
    /// The semantic version, without build metadata — what MCP reports as
    /// <c>serverInfo.version</c>, where a client may compare or parse it.
    /// </summary>
    public static string Version => SemanticVersion(InformationalVersion);

    /// <summary>
    /// The version reported for <c>commit:</c> when nothing stamped it — no
    /// <c>-p:SourceRevisionId</c> and no <c>.git</c> for the SDK to read on its own (a
    /// source archive, most likely).
    /// </summary>
    internal const string UnknownCommit = "unknown";

    /// <summary>
    /// What <c>okf version --verbose</c> prints on its second line: the commit every
    /// build carries via <c>SourceRevisionId</c> (Directory.Build.props), independent of
    /// whether it also survived into <see cref="VersionDisplay"/> (issue #53).
    /// </summary>
    public static string CommitDisplay => DescribeCommit(Commit);

    private static string? InformationalVersion =>
        typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

    private static string? Commit =>
        typeof(CliApplication).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "Commit")
            ?.Value;

    /// <summary>Renders the informational version for display, metadata and all.</summary>
    internal static string Describe(string? informationalVersion) =>
        informationalVersion is { Length: > 0 } value ? value : UnknownVersion;

    /// <summary>Renders the commit metadata for display, or <see cref="UnknownCommit"/>.</summary>
    internal static string DescribeCommit(string? commit) =>
        commit is { Length: > 0 } value ? value : UnknownCommit;

    /// <summary>Drops the <c>+</c> build metadata from an informational version.</summary>
    internal static string SemanticVersion(string? informationalVersion)
    {
        string described = Describe(informationalVersion);
        int metadata = described.IndexOf('+', StringComparison.Ordinal);
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
              okf register [path]         Add a vault or bundle to the registry (idempotent)
              okf unregister [path|id]    Remove a registry entry (idempotent)
              okf registry [list|prune]   Report the registry, or drop entries whose path is gone
              okf inbox [path]            List the concepts waiting on a person
              okf verify <concept>...     Stamp human verification on one or more concepts
              okf capture <add|close>     Record a raw/ capture, or close its ingestion
              okf generated stamp <concept>...
                                          Write the `generated` stamp a producer owes
              okf bundle [path] --out <f> Package the bundles for consume-only distribution
              okf site [path] --out <dir> Render the bundles as a static site (dashboard, graph, pages)
              okf skills <list|path|install>
                                          The agent skills this binary carries, and where they install
              okf mcp [path]              Run the read-only MCP server over stdio
              okf upgrade [--check]       Replace this binary with the newest release
                                          (the one command that uses a network)
              okf completion <shell>      Print the completion script for bash, zsh, fish or pwsh
              okf help                    Show this help
              okf version [--verbose]     Show the version (--verbose also prints the commit)

            Run `okf <command> --help` for a command's options.
            """);
    }
}
