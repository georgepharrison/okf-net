using System.Text.Json;

namespace Okf.Cli.Tests.Mcp;

/// <summary>
/// <c>okf mcp</c> driven end to end against real bundles: okf-net's own dogfood bundle
/// (PRD ACC-5) and Google's four reference bundles (ACC-1), which okf-net did not produce
/// and cannot ask to cooperate.
/// </summary>
/// <remarks>
/// Both trees are read as-is and never written to; where either is absent the test skips
/// visibly rather than passing vacuously. <c>OKF_REFERENCE_BUNDLES</c> relocates the clone.
/// </remarks>
public class McpAcceptanceTests
{
    [SkippableFact]
    public void TheDogfoodBundleSupportsAWholeDisclosureWalk()
    {
        var vault = DogfoodVault();
        using var home = new TempTree();

        // The three moves the tool descriptions teach, in order: orient, retrieve, open.
        var run = McpHarness.Session(
            CliHarness.Environment(home.Root, home.Root),
            vault,
            McpHarness.Initialize(),
            McpHarness.Request(2, "tools/list"),
            McpHarness.Call(3, "okf_list"),
            McpHarness.Call(4, "okf_list", """{"bundle":"okf-net"}"""),
            McpHarness.Call(5, "okf_search", """{"query":"trust tier","limit":3}"""));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(5, run.Responses.Length);

        using var scope = JsonDocument.Parse(run.Content(2).Text);
        var bundles = scope.RootElement.GetProperty("bundles").EnumerateArray().ToList();
        Assert.Equal("okf-net", Assert.Single(bundles).GetProperty("name").GetString());
        Assert.True(bundles[0].GetProperty("concepts").GetInt32() > 5);

        using var listing = JsonDocument.Parse(run.Content(3).Text);

        // The dogfood bundle ships generated indexes, so the listing is the file okf wrote.
        Assert.Equal("index.md", listing.RootElement.GetProperty("source").GetString());
        Assert.Contains(
            listing.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("link").GetString() == "about-this-bundle.md");

        using var results = JsonDocument.Parse(run.Content(4).Text);
        var first = results.RootElement[0];
        Assert.Equal("okf-net", first.GetProperty("bundleName").GetString());

        // What search hands back is what read takes: the walk closes without the client
        // having to reconstruct a path.
        var second = McpHarness.Session(
            CliHarness.Environment(home.Root, home.Root),
            vault,
            McpHarness.Call(1, "okf_read", $$"""{"path":"{{first.GetProperty("path").GetString()}}"}"""),
            McpHarness.Call(2, "okf_read", $$"""{"path":"{{first.GetProperty("id").GetString()}}"}"""));

        var (text, isError) = second.Content(0);
        Assert.False(isError);
        Assert.Equal(text, second.Content(1).Text);

        using var concept = JsonDocument.Parse(text);
        Assert.Equal(first.GetProperty("title").GetString(), concept.RootElement.GetProperty("title").GetString());
        Assert.NotEmpty(concept.RootElement.GetProperty("body").GetString()!);
        Assert.Equal(
            first.GetProperty("trustTier").GetString(),
            concept.RootElement.GetProperty("trustTier").GetString());
    }

    [SkippableFact]
    public void TheReferenceBundlesAreSearchableThroughMcpExactlyAsThroughTheCli()
    {
        var vault = ReferenceVault();
        using var home = new TempTree();
        var environment = CliHarness.Environment(home.Root, home.Root);

        var cli = CliHarness.Run(environment, "search", "revenue recognition", vault, "--json", "--limit", "3");
        var run = McpHarness.Session(
            environment,
            vault,
            McpHarness.Call(1, "okf_search", """{"query":"revenue recognition","limit":3}"""));

        // PRD MCP-3, on bundles with no in-bundle cooperation of any kind.
        Assert.Equal(cli.Output.TrimEnd('\r', '\n'), run.Content(0).Text);

        using var results = JsonDocument.Parse(run.Content(0).Text);
        Assert.EndsWith(
            "acme_retail/policies/revenue-recognition.md",
            results.RootElement[0].GetProperty("absolutePath").GetString()!.Replace('\\', '/'),
            StringComparison.Ordinal);
    }

    [SkippableFact]
    public void AForeignBundleListsThroughItsOwnHandWrittenIndexAndReadsThroughMcp()
    {
        var vault = ReferenceVault();
        using var home = new TempTree();

        var run = McpHarness.Session(
            CliHarness.Environment(home.Root, home.Root),
            vault,
            McpHarness.Call(1, "okf_list"),
            McpHarness.Call(2, "okf_list", """{"bundle":"acme_retail"}"""),
            McpHarness.Call(3, "okf_list", """{"bundle":"acme_retail","path":"policies"}"""),
            McpHarness.Call(4, "okf_read", """{"path":"policies/revenue-recognition","bundle":"acme_retail"}"""));

        using var scope = JsonDocument.Parse(run.Content(0).Text);
        Assert.Equal(
            ["acme_retail", "crypto_bitcoin", "ga4", "stackoverflow"],
            scope.RootElement.GetProperty("bundles").EnumerateArray()
                .Select(bundle => bundle.GetProperty("name").GetString()));

        // The bundle ships a hand-styled index okf-net did not write; MCP-2 says serve it as
        // it is, not the synthesis of it.
        using var root = JsonDocument.Parse(run.Content(1).Text);
        Assert.Equal("index.md", root.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            File.ReadAllText(Path.Combine(ReferenceBundles.Bundle("acme_retail"), "index.md")),
            root.RootElement.GetProperty("listing").GetString());

        using var policies = JsonDocument.Parse(run.Content(2).Text);
        Assert.Contains(
            policies.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("link").GetString() == "revenue-recognition.md");

        var (text, isError) = run.Content(3);
        Assert.False(isError);
        using var concept = JsonDocument.Parse(text);
        Assert.Equal("acme_retail", concept.RootElement.GetProperty("bundleName").GetString());
        Assert.NotEmpty(concept.RootElement.GetProperty("body").GetString()!);

        // Frontmatter keys a producer invented survive the read: acme_retail's `not:`
        // extension is exactly the case PRD CORE-2 protects.
        Assert.True(concept.RootElement.GetProperty("frontmatter").EnumerateObject().Any());
    }

    [SkippableFact]
    public void AnMcpSessionWritesNothingToTheBundlesItServes()
    {
        var vault = ReferenceVault();
        var bundles = ReferenceBundles.Root;
        using var home = new TempTree();

        var before = ReferenceBundles.Snapshot(bundles);
        var run = McpHarness.Session(
            CliHarness.Environment(home.Root, home.Root),
            vault,
            McpHarness.Initialize(),
            McpHarness.Call(2, "okf_list", """{"bundle":"stackoverflow"}"""),
            McpHarness.Call(3, "okf_search", """{"query":"metric definition"}"""),
            McpHarness.Call(4, "okf_read", """{"path":"tables/events_","bundle":"ga4"}"""));

        Assert.False(run.Content(3).IsError);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);

        // MCP-4: no write tool, and no write anywhere on the read paths either — synthesis
        // included (PRD CORE-10 forbids writing into a bundle okf-net does not own).
        Assert.Equal(before, ReferenceBundles.Snapshot(bundles));
    }

    private static string DogfoodVault()
    {
        var vault = Repository.DogfoodVault;
        Skip.If(vault is null || !Directory.Exists(vault), "The dogfood vault 'okf/' is not present.");
        return vault!;
    }

    private static string ReferenceVault()
    {
        var bundles = ReferenceBundles.Root;
        Skip.IfNot(Directory.Exists(bundles), $"Reference bundles directory '{bundles}' is not present.");
        return Path.GetDirectoryName(bundles)!;
    }
}
