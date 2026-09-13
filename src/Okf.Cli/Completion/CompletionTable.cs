using System.Linq;
using Okf.Core;

namespace Okf.Cli.Completion;

/// <summary>
/// What a flag or operand accepts, as much of it as a completion script can offer without
/// reading a vault. <see cref="Candidates" /> is the fixed value set; the two kinds a
/// script resolves for itself are <c>path</c> (the shell's own file completion) and
/// <c>skill</c> (a call to <c>okf skills list --names</c>).
/// </summary>
/// <param name="Kind">The kind name, which is what the generated scripts branch on.</param>
/// <param name="TakesValue">Whether the flag consumes the next word.</param>
/// <param name="Candidates">The values worth offering, in the order they are offered.</param>
internal sealed record CompletionValue(string Kind, bool TakesValue, IReadOnlyList<string> Candidates)
{
    /// <summary>A switch: it consumes no value at all.</summary>
    public static readonly CompletionValue None = new("none", false, []);

    /// <summary>A value nothing can usefully guess — an actor, a tag, a count.</summary>
    public static readonly CompletionValue Text = new("text", true, []);

    /// <summary>A filesystem path, completed by the shell rather than by okf.</summary>
    public static readonly CompletionValue Path = new("path", true, []);

    /// <summary>A skill this binary carries, asked for at completion time.</summary>
    public static readonly CompletionValue Skill = new("skill", true, []);

    /// <summary>A shell <c>okf completion</c> can emit a script for.</summary>
    public static readonly CompletionValue Shell = new("shell", true, CompletionTable.Shells);

    /// <summary>The output format flag every reporting verb takes.</summary>
    public static readonly CompletionValue Format = new("format", true, ["json", "text"]);

    /// <summary>
    /// A search scope (PRD CLI-3). Deliberately distinct from
    /// <see cref="InstallScope" />: `okf search --scope` and `okf skills --scope` spell the
    /// same flag and share none of their values.
    /// </summary>
    public static readonly CompletionValue Scope =
        new("scope", true, [.. OkfScopeKindExtensions.Names]);

    /// <summary>Where `okf skills install` writes — the user's home or this project.</summary>
    public static readonly CompletionValue InstallScope = new("install-scope", true, ["project", "user"]);

    /// <summary>An agent host `okf skills install` knows.</summary>
    public static readonly CompletionValue Host = new("host", true, ["all", "claude", "generic", "pi"]);

    /// <summary>
    /// A rule identifier, which is the half of <c>--severity</c>'s
    /// <c>OKF####=&lt;level&gt;</c> a script can offer.
    /// </summary>
    public static readonly CompletionValue Severity =
        new("severity", true, [.. OkfRules.All.Select(rule => rule.Id)]);

    /// <summary>The shape a raw/ capture takes on disk.</summary>
    public static readonly CompletionValue Form = new("form", true, ["flat", "packet"]);

    /// <summary>A release channel `okf upgrade` will follow.</summary>
    public static readonly CompletionValue Channel = new("channel", true, ["rc", "stable"]);

    /// <summary>The shape `okf bundle` writes; not the <c>text|json</c> `--format`.</summary>
    public static readonly CompletionValue Distribution = new("distribution", true, ["dir", "tar.gz", "zip"]);
}

/// <summary>One option, in one verb's completion entry.</summary>
/// <param name="Name">The option as it is typed, including its dashes.</param>
/// <param name="Description">One line, shown by the shells that show descriptions.</param>
/// <param name="Value">What it accepts.</param>
internal sealed record CompletionFlag(string Name, string Description, CompletionValue Value);

/// <summary>One verb, in the completion table.</summary>
/// <param name="Name">The verb.</param>
/// <param name="Summary">One line, shown while completing the verb itself.</param>
/// <param name="Subcommands">The words that may follow the verb, empty when it takes none.</param>
/// <param name="Flags">Every option the verb's parser accepts, in the order they are offered.</param>
/// <param name="Operand">What a bare word after the verb (and its subcommand) is.</param>
internal sealed record CompletionVerb(
    string Name,
    string Summary,
    IReadOnlyList<string> Subcommands,
    IReadOnlyList<CompletionFlag> Flags,
    CompletionValue Operand)
{
    /// <summary>
    /// The subcommands the operand belongs to; empty means every one. `okf skills path`
    /// takes a skill name and its two siblings take none, so offering one after them would
    /// be offering a word the parser rejects.
    /// </summary>
    public IReadOnlyList<string> OperandSubcommands { get; init; } = [];
}

/// <summary>
/// The CLI surface as a completion script needs it: every verb <see cref="CliApplication" />
/// dispatches, its subcommands, and every option its parser accepts, each with the values
/// worth offering (issue #51).
/// </summary>
/// <remarks>
/// It lives in <c>Okf.Cli</c> and not in <c>Okf.Core</c> because it is knowledge about this
/// adapter's command line and nothing else — AD-6 puts judgement in the library, and there
/// is no judgement here. Two tests hold it to the code it describes: the verb set must equal
/// <see cref="CliApplication.Verbs" />, which is the array the dispatcher itself reads, and
/// each verb's flag set must equal the <c>Flags</c> array its parser declares.
/// </remarks>
internal static class CompletionTable
{
    /// <summary>The shells <c>okf completion</c> emits a script for.</summary>
    public static readonly string[] Shells = ["bash", "fish", "pwsh", "zsh"];

    private static readonly CompletionFlag Help = new("--help", "Show this help", CompletionValue.None);
    private static readonly CompletionFlag HelpShort = new("-h", "Show this help", CompletionValue.None);
    private static readonly CompletionFlag Verbose =
        new("--verbose", "Report vault resolution and effective configuration", CompletionValue.None);
    private static readonly CompletionFlag VerboseShort =
        new("-v", "Report vault resolution and effective configuration", CompletionValue.None);
    private static readonly CompletionFlag Json =
        new("--json", "Emit the stable JSON form instead of text", CompletionValue.None);
    private static readonly CompletionFlag Format =
        new("--format", "Output format", CompletionValue.Format);

    /// <summary>Every verb, in the order <c>okf help</c> lists them.</summary>
    public static IReadOnlyList<CompletionVerb> Verbs { get; } =
    [
        new(
            "init",
            "Scaffold a vault",
            [],
            [
                Help,
                HelpShort,
                new("--name", "The bundle directory to create", CompletionValue.Text),
                new("--no-agents-md", "Skip writing the AGENTS.md/CLAUDE.md pointer", CompletionValue.None),
                new("--personal", "Scaffold the personal vault rather than a project one", CompletionValue.None),
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "lint",
            "Validate conformance plus the configured warning set",
            [],
            [
                Help,
                HelpShort,
                new("--config", "Read this project config instead of the vault config", CompletionValue.Path),
                Format,
                Json,
                new("--list-rules", "Print the rule catalog and stop", CompletionValue.None),
                new("--severity", "Override the severity of one rule as OKF####=level", CompletionValue.Severity),
                new(
                    "--treat-all-warnings-as-errors",
                    "Promote every warning to an error",
                    CompletionValue.None),
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "index",
            "Generate the index.md files for a bundle",
            [],
            [
                Help,
                HelpShort,
                new("--check", "Write nothing and fail on drift", CompletionValue.None),
                Format,
                Json,
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "search",
            "Search the resolved bundles",
            [],
            [
                Help,
                HelpShort,
                Format,
                Json,
                new("--limit", "How many results to report at most", CompletionValue.Text),
                new("--scope", "Which bundles the search covers", CompletionValue.Scope),
                new("--tag", "Only concepts carrying this tag", CompletionValue.Text),
                new("--type", "Only concepts of this type", CompletionValue.Text),
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Text),
        new(
            "register",
            "Add a vault or bundle to the registry",
            [],
            [Help, HelpShort, Format, Json, Verbose, VerboseShort],
            CompletionValue.Path),
        new(
            "unregister",
            "Remove a registry entry",
            [],
            [Help, HelpShort, Format, Json, Verbose, VerboseShort],
            CompletionValue.Path),
        new(
            "registry",
            "Report the registry or drop entries whose path is gone",
            ["list", "prune"],
            [Help, HelpShort, Format, Json, Verbose, VerboseShort],
            CompletionValue.None),
        new(
            "inbox",
            "List the concepts waiting on a person",
            [],
            [
                Help,
                HelpShort,
                new("--fail-if-any", "Exit 1 when the inbox is not empty", CompletionValue.None),
                Format,
                Json,
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "candidates",
            "List the concepts nobody has ever verified",
            [],
            [Help, HelpShort, Format, Json, Verbose, VerboseShort],
            CompletionValue.Path),
        new(
            "verify",
            "Stamp human verification on one or more concepts",
            [],
            [
                Help,
                HelpShort,
                new("--by", "Stamp this actor instead of the resolved identity", CompletionValue.Text),
                new("--config", "Read verify.actor from this project config", CompletionValue.Path),
                new("--dry-run", "Report what would be written and write nothing", CompletionValue.None),
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "capture",
            "Record a raw/ capture or close its ingestion",
            ["add", "close"],
            [
                Help,
                HelpShort,
                new("--at", "Alias of --captured-at", CompletionValue.Text),
                new("--by", "The capturing or ingesting actor", CompletionValue.Text),
                new("--captured-at", "Pin the capture instant", CompletionValue.Text),
                new("--concept", "A concept the ingestion produced", CompletionValue.Path),
                new("--form", "The shape the item takes on disk", CompletionValue.Form),
                Json,
                new("--source-last-modified", "The last-modified date the artifact itself carries", CompletionValue.Text),
                new("--title", "The title of the captured artifact", CompletionValue.Text),
                new("--url", "The URL the artifact came from", CompletionValue.Text),
            ],
            CompletionValue.Path),
        new(
            "generated",
            "Write the generated stamp a producer owes",
            ["stamp"],
            [
                Help,
                HelpShort,
                new("--at", "Pin the stamp instant", CompletionValue.Text),
                new("--by", "The generating actor", CompletionValue.Text),
                new("--dry-run", "Report what would be written and write nothing", CompletionValue.None),
            ],
            CompletionValue.Path),
        new(
            "bundle",
            "Package the bundles for consume-only distribution",
            [],
            [
                Help,
                HelpShort,
                new("--bundle", "Package only this bundle", CompletionValue.Text),
                new("--format", "The distribution shape to write", CompletionValue.Distribution),
                new("--generated-at", "Pin the manifest instant", CompletionValue.Text),
                new("--lint", "Lint the packaged result the way a consumer sees it", CompletionValue.None),
                new("--out", "Where the distribution is written", CompletionValue.Path),
                new("-o", "Where the distribution is written", CompletionValue.Path),
                new("--verify", "Re-hash a finished distribution instead of packaging", CompletionValue.Path),
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "site",
            "Render the bundles as a static site",
            [],
            [
                Help,
                HelpShort,
                Format,
                Json,
                new("--name", "The display name for the site", CompletionValue.Text),
                new("--out", "The output directory", CompletionValue.Path),
                new("-o", "The output directory", CompletionValue.Path),
                new("--single-file", "Emit the whole site as one index.html", CompletionValue.None),
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "skills",
            "The agent skills this binary carries and where they install",
            ["install", "list", "path"],
            [
                Help,
                HelpShort,
                new("--dir", "Write into this directory as well", CompletionValue.Path),
                new("--force", "Overwrite a file whose bytes differ", CompletionValue.None),
                new("--host", "Where to write", CompletionValue.Host),
                new("--names", "List names only, one per line", CompletionValue.None),
                new("--no-agents-md", "Skip writing the AGENTS.md/CLAUDE.md pointer", CompletionValue.None),
                new("--scope", "Install for this user or beside this project", CompletionValue.InstallScope),
            ],
            CompletionValue.Skill)
        {
            OperandSubcommands = ["path"],
        },
        new(
            "mcp",
            "Run the read-only MCP server over stdio",
            [],
            [
                Help,
                HelpShort,
                new("--scope", "Which bundles the server serves", CompletionValue.Scope),
                Verbose,
                VerboseShort,
            ],
            CompletionValue.Path),
        new(
            "upgrade",
            "Replace this binary with the newest release",
            [],
            [
                Help,
                HelpShort,
                new("--channel", "The release channel to follow", CompletionValue.Channel),
                new("--check", "Report whether an upgrade is available and stop", CompletionValue.None),
                new("--dry-run", "Resolve and report without downloading anything", CompletionValue.None),
                Format,
                Json,
                new("--version", "Install this release instead of the newest", CompletionValue.Text),
            ],
            CompletionValue.None),
        new(
            "completion",
            "Print a shell completion script",
            [],
            [Help, HelpShort],
            CompletionValue.Shell),
        new("help", "Show the verb list", [], [], CompletionValue.None),
        new(
            "version",
            "Show the version",
            [],
            [
                new("--verbose", "Also print the commit this binary was built from", CompletionValue.None),
                new("-v", "Also print the commit this binary was built from", CompletionValue.None),
            ],
            CompletionValue.None),
    ];

    /// <summary>The verb names, in table order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Verbs.Select(verb => verb.Name)];
}
