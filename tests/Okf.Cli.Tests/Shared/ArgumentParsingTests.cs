using System.Text.Json;

namespace Okf.Cli.Tests.Shared;

/// <summary>
/// The argument parsers' refusals and their two spellings of an option value. Every verb
/// hand-rolls its own loop over one shared shape (see <see cref="CliArguments" />), so the
/// contract is asserted as a table across the verbs rather than once per parser: a refusal
/// is exit 2 (PRD §3's exit-code column, AD-5) and its message names the option that
/// caused it, which is the half a person acts on.
/// </summary>
public class ArgumentParsingTests
{
    /// <summary>
    /// Every option that takes a value refuses the line that ends with it, rather than
    /// reading past the end of the argument list or silently defaulting.
    /// </summary>
    [Theory]
    [InlineData(new[] { "lint", "--format" }, "--format")]
    [InlineData(new[] { "lint", "--severity" }, "--severity")]
    [InlineData(new[] { "lint", "--config" }, "--config")]
    [InlineData(new[] { "index", "--format" }, "--format")]
    [InlineData(new[] { "inbox", "--format" }, "--format")]
    [InlineData(new[] { "init", "--name" }, "--name")]
    [InlineData(new[] { "search", "--format" }, "--format")]
    [InlineData(new[] { "search", "--limit" }, "--limit")]
    [InlineData(new[] { "search", "--scope" }, "--scope")]
    [InlineData(new[] { "search", "--type" }, "--type")]
    [InlineData(new[] { "search", "--tag" }, "--tag")]
    [InlineData(new[] { "site", "--out" }, "--out")]
    [InlineData(new[] { "site", "-o" }, "-o")]
    [InlineData(new[] { "site", "--out", "site", "--name" }, "--name")]
    [InlineData(new[] { "site", "--out", "site", "--format" }, "--format")]
    [InlineData(new[] { "verify", "--by" }, "--by")]
    [InlineData(new[] { "verify", "--config" }, "--config")]
    [InlineData(new[] { "capture", "add", "--by" }, "--by")]
    [InlineData(new[] { "capture", "add", "--url" }, "--url")]
    [InlineData(new[] { "capture", "add", "--title" }, "--title")]
    [InlineData(new[] { "capture", "add", "--source-last-modified" }, "--source-last-modified")]
    [InlineData(new[] { "capture", "add", "--form" }, "--form")]
    [InlineData(new[] { "capture", "add", "--concept" }, "--concept")]
    [InlineData(new[] { "capture", "add", "--captured-at" }, "--captured-at")]
    [InlineData(new[] { "capture", "add", "--at" }, "--at")]
    [InlineData(new[] { "generated", "stamp", "--by" }, "--by")]
    [InlineData(new[] { "generated", "stamp", "--at" }, "--at")]
    [InlineData(new[] { "bundle", "--out" }, "--out")]
    [InlineData(new[] { "bundle", "--verify" }, "--verify")]
    [InlineData(new[] { "bundle", "--bundle" }, "--bundle")]
    [InlineData(new[] { "bundle", "--format" }, "--format")]
    [InlineData(new[] { "bundle", "--generated-at" }, "--generated-at")]
    [InlineData(new[] { "upgrade", "--format" }, "--format")]
    [InlineData(new[] { "upgrade", "--version" }, "--version")]
    [InlineData(new[] { "upgrade", "--channel" }, "--channel")]
    [InlineData(new[] { "skills", "install", "--host" }, "--host")]
    [InlineData(new[] { "skills", "install", "--dir" }, "--dir")]
    [InlineData(new[] { "skills", "install", "--scope" }, "--scope")]
    [InlineData(new[] { "registry", "--format" }, "--format")]
    [InlineData(new[] { "register", "--format" }, "--format")]
    public void AnOptionMissingItsValueIsRefusedByName(string[] args, string option)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, args);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains($"Option '{option}' requires a value.", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// An option no verb declares is refused by that verb, and the message quotes the
    /// argument back so a typo is visible.
    /// </summary>
    [Theory]
    [InlineData("init")]
    [InlineData("lint")]
    [InlineData("index")]
    [InlineData("search")]
    [InlineData("register")]
    [InlineData("unregister")]
    [InlineData("registry")]
    [InlineData("inbox")]
    [InlineData("verify")]
    [InlineData("capture")]
    [InlineData("generated")]
    [InlineData("bundle")]
    [InlineData("site")]
    [InlineData("skills")]
    [InlineData("mcp")]
    [InlineData("upgrade")]
    public void AnUnknownOptionIsRefusedByName(string verb)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, verb, "--frobnicate");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Unknown option '--frobnicate'.", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bare <c>-</c> is a one-character argument, not an option: the parsers guard the
    /// unknown-option refusal with a length test so a path spelled <c>-</c> reaches the
    /// positional branch. Asserted through the second <c>-</c>, whose refusal names the
    /// positional the first one became.
    /// </summary>
    [Theory]
    [InlineData(new[] { "lint", "-", "-" }, "`okf lint` takes at most one path; got '-' and '-'.")]
    [InlineData(new[] { "index", "-", "-" }, "`okf index` takes at most one path; got '-' and '-'.")]
    [InlineData(new[] { "inbox", "-", "-" }, "`okf inbox` takes at most one path; got '-' and '-'.")]
    [InlineData(new[] { "init", "-", "-" }, "`okf init` takes at most one path; got '-' and '-'.")]
    [InlineData(new[] { "mcp", "-", "-" }, "`okf mcp` takes at most one path; got '-' and '-'.")]
    [InlineData(
        new[] { "site", "--out", "site", "-", "-" },
        "`okf site` takes at most one path; got '-' and '-'.")]
    [InlineData(
        new[] { "bundle", "--out", "dist", "-", "-" },
        "`okf bundle` takes at most one path; got '-' and '-'.")]
    [InlineData(
        new[] { "search", "-", "-", "-" },
        "`okf search` takes one query and at most one path; got '-', '-', and '-'.")]
    [InlineData(
        new[] { "register", "-", "-" },
        "`okf register` takes at most one argument; got '-' and '-'.")]
    [InlineData(
        new[] { "skills", "list", "-", "-" },
        "`okf skills list` takes at most one skill name; got '-' and '-'.")]
    public void ABareDashIsAPositionalNotAnOption(string[] args, string expected)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, args);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown option", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--option=value</c> and <c>--option value</c> name the same value. Each row picks
    /// a value the option itself rejects, so the message quotes the value back: a parser
    /// that ignored the inline half would instead consume the next argument, or refuse the
    /// whole <c>--option=value</c> token as unknown, and say something else.
    /// </summary>
    [Theory]
    [InlineData(new[] { "lint", "--format=xml" }, "Unknown --format value 'xml'")]
    [InlineData(new[] { "index", "--format=xml" }, "Unknown --format value 'xml'")]
    [InlineData(new[] { "inbox", "--format=xml" }, "Unknown --format value 'xml'")]
    [InlineData(new[] { "search", "--format=xml" }, "Unknown --format value 'xml'")]
    [InlineData(new[] { "site", "--out=site", "--format=xml" }, "Unknown --format value 'xml'")]
    [InlineData(new[] { "upgrade", "--format=xml" }, "Unknown --format value 'xml'")]
    [InlineData(new[] { "registry", "--format", "xml" }, "Unknown --format value 'xml'")]
    [InlineData(new[] { "lint", "--severity=OKF0301" }, "--severity expects")]
    [InlineData(new[] { "search", "q", "--limit=0" }, "--limit expects a positive whole number; got '0'.")]
    [InlineData(new[] { "search", "q", "--limit=six" }, "--limit expects a positive whole number; got 'six'.")]
    [InlineData(new[] { "search", "q", "--scope=everywhere" }, "Unknown --scope value 'everywhere'")]
    [InlineData(new[] { "site", "--out=" }, "Option '--out' requires a directory.")]
    [InlineData(new[] { "site", "--out=a", "--out=b" }, "Option '--out' was given twice.")]
    [InlineData(new[] { "init", "--name=a", "--name=b" }, "Option '--name' was given more than once.")]
    [InlineData(new[] { "verify", "--by=a", "--by=b" }, "Option '--by' may be given once")]
    [InlineData(new[] { "capture", "add", "item", "--by=a", "--by=b" }, "Option '--by' may be given once")]
    [InlineData(
        new[] { "capture", "add", "item", "--form=scroll" },
        "Unknown --form value 'scroll'; expected 'flat' or 'packet'.")]
    [InlineData(
        new[] { "capture", "add", "item", "--source-last-modified=yesterday" },
        "'yesterday' is not a date.")]
    [InlineData(new[] { "capture", "add", "item", "--at=yesterday" }, "'yesterday' is not a canonical instant")]
    [InlineData(new[] { "generated", "stamp", "c.md", "--by=a", "--by=b" }, "Option '--by' may be given once")]
    [InlineData(
        new[] { "generated", "stamp", "c.md", "--at=2026-08-16T14:00:00Z", "--at=2026-08-16T15:00:00Z" },
        "Option '--at' may be given once")]
    [InlineData(new[] { "generated", "stamp", "c.md", "--at=noon" }, "'noon' is not a canonical instant")]
    [InlineData(new[] { "bundle", "--out=a", "--out=b" }, "Option '--out' was given twice ('a' and 'b').")]
    [InlineData(
        new[] { "bundle", "--verify=a", "--verify=b" },
        "Option '--verify' was given twice ('a' and 'b').")]
    [InlineData(new[] { "bundle", "--out=dist", "--format=cpio" }, "Unknown --format value 'cpio'")]
    [InlineData(new[] { "bundle", "--verify=dist", "--bundle=one" }, "cannot be combined with --bundle")]
    [InlineData(new[] { "bundle", "--out=dist", "--generated-at=soon" }, "'soon' is not a readable instant")]
    [InlineData(new[] { "upgrade", "--channel=nightly" }, "Unknown --channel value 'nightly'")]
    [InlineData(new[] { "upgrade", "--version=1.0.0", "--version=1.0.1" }, "Option '--version' was given more than once")]
    [InlineData(new[] { "upgrade", "--version" }, "Option '--version' requires a value")]
    [InlineData(new[] { "upgrade", "--version=" }, "Option '--version' requires a value")]
    [InlineData(new[] { "upgrade", "--channel=stable", "--channel=rc" }, "Option '--channel' was given more than once")]
    [InlineData(
        new[] { "upgrade", "--version=1.2.3", "--channel=rc" },
        "'--version' pins one release, so '--channel' has nothing left to choose")]
    [InlineData(new[] { "skills", "install", "--host=emacs" }, "Unknown --host value 'emacs'")]
    [InlineData(new[] { "skills", "install", "--host=claude", "--host=pi" }, "Option '--host' was given more than once")]
    [InlineData(
        new[] { "skills", "install", "--scope=everyone" },
        "Unknown --scope value 'everyone'; expected 'user' or 'project'.")]
    [InlineData(new[] { "skills", "install", "--dir=a", "--dir=b" }, "Option '--dir' was given more than once.")]
    [InlineData(new[] { "mcp", "--scope=everywhere" }, "Unknown --scope value 'everywhere'")]
    [InlineData(new[] { "registry", "list", "prune" }, "takes at most one argument")]
    public void AnInlineValueIsTheOptionsValue(string[] args, string expected)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, args);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf registry</c> spells its inline <c>--format</c> as two literal case labels
    /// rather than splitting on <c>=</c> (it is the one parser that does), so an inline
    /// value it does not list is an unknown option and not an unknown format.
    /// </summary>
    [Fact]
    public void RegistryRefusesAnInlineFormatValueItDoesNotList()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "registry", "--format=xml");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Unknown option '--format=xml'.", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>okf mcp --scope</c> with nothing after it reports the empty value rather than
    /// reading past the end of the line: its scope is parsed inline, without
    /// <see cref="CliArguments.Next" />'s refusal.
    /// </summary>
    [Fact]
    public void McpScopeWithoutAValueIsRefused()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "mcp", "--scope");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("Unknown --scope value ''", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--format json</c> and <c>--format text</c> choose the same two shapes
    /// <c>--json</c> and the default do, in both spellings, for every verb that reports
    /// (PRD §3's CLI surface). Text output is asserted to be text, not merely "not the
    /// JSON I expected": a parser that treated <c>text</c> as <c>json</c> would pass an
    /// assertion that only checked the JSON case.
    /// </summary>
    [Theory]
    [InlineData("lint")]
    [InlineData("index")]
    [InlineData("inbox")]
    [InlineData("registry")]
    public void FormatChoosesBetweenJsonAndText(string verb)
    {
        // A tree apiece: `okf index` writes the indexes it reports on, so a second run in
        // the same tree describes a different vault than the first. The tree's own root is
        // rewritten out of the output, since it is the one thing that differs by design.
        var spaced = InFreshVault(verb, "--format", "json");
        var inline = InFreshVault(verb, "--format=json");
        var text = InFreshVault(verb, "--format", "text");

        Assert.Equal(spaced, inline);
        using var parsed = JsonDocument.Parse(spaced);
        Assert.True(parsed.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object);
        Assert.False(
            text.TrimStart().StartsWith('[') || text.TrimStart().StartsWith('{'),
            $"`okf {verb} --format text` emitted JSON: {text}");
    }

    /// <summary>
    /// <c>okf search</c>'s <c>--type</c> and <c>--tag</c> are the flag spelling of the
    /// inline <c>type:</c> and <c>tag:</c> filters (decisions.md Q7), so a filter that
    /// excludes the only match leaves nothing to report.
    /// </summary>
    [Theory]
    [InlineData("--type", "concept", 2)]
    [InlineData("--type", "reference", 0)]
    [InlineData("--tag", "alpha", 1)]
    [InlineData("--tag", "omega", 0)]
    public void SearchFiltersNarrowTheResults(string flag, string value, int expected)
    {
        using var tree = new TempTree();
        WriteSearchableBundle(tree);

        var run = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget", flag, value, "--format", "json");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var parsed = JsonDocument.Parse(run.Output);
        Assert.Equal(expected, parsed.RootElement.GetArrayLength());
    }

    /// <summary>
    /// <c>--limit</c> caps the reported results (PRD CLI-11), in both spellings.
    /// </summary>
    [Theory]
    [InlineData("--limit", "1")]
    [InlineData("--limit=1", null)]
    public void LimitCapsTheResults(string first, string? second)
    {
        using var tree = new TempTree();
        WriteSearchableBundle(tree);
        string[] args = second is null
            ? ["search", "widget", first, "--format", "json"]
            : ["search", "widget", first, second, "--format", "json"];

        var unlimited = CliHarness.RunIn(tree.Root, tree.Root, "search", "widget", "--format", "json");
        var limited = CliHarness.RunIn(tree.Root, tree.Root, args);

        using var all = JsonDocument.Parse(unlimited.Output);
        using var capped = JsonDocument.Parse(limited.Output);
        Assert.Equal(2, all.RootElement.GetArrayLength());
        Assert.Equal(1, capped.RootElement.GetArrayLength());
    }

    private static string InFreshVault(params string[] args)
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        var run = CliHarness.RunIn(tree.Root, tree.Root, args);
        Assert.True(
            run.ExitCode == CliApplication.ExitSuccess,
            $"`okf {string.Join(' ', args)}` exited {run.ExitCode}: {run.Error}");
        return run.Output.Replace(tree.Root, "<vault>", StringComparison.Ordinal);
    }

    private static void WriteSearchableBundle(TempTree tree)
    {
        tree.Write(
            Path.Combine("okf", "bundles", "widgets", "bundle.yaml"),
            """
            okf_version: "0.2"
            name: widgets
            """);
        tree.Write(
            Path.Combine("okf", "bundles", "widgets", "concepts", "widget-one.md"),
            """
            ---
            id: widget-one
            type: concept
            title: Widget one
            tags: [alpha]
            ---

            # Widget one

            A widget that widgets.
            """);
        tree.Write(
            Path.Combine("okf", "bundles", "widgets", "concepts", "widget-two.md"),
            """
            ---
            id: widget-two
            type: concept
            title: Widget two
            tags: [beta]
            ---

            # Widget two

            Another widget, widget-shaped.
            """);
    }
}
