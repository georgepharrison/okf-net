using System.Text.Json;

namespace Okf.Cli.Tests.Mcp;

/// <summary>
/// The three tools <c>okf mcp</c> exposes (PRD MCP-2, MCP-4, MCP-5): what they answer, and
/// what they refuse.
/// </summary>
public class McpToolTests
{
    [Fact]
    public void SearchReturnsByteForByteWhatOkfSearchJsonPrints()
    {
        using var vault = McpProtocolTests.Vault();
        var environment = McpProtocolTests.Environment(vault);

        var cli = CliHarness.Run(environment, "search", "widget pricing", vault.Root, "--json", "--limit", "3");
        var run = McpHarness.Session(
            environment,
            vault.Root,
            McpHarness.Call(1, "okf_search", """{"query":"widget pricing","limit":3}"""));

        var (text, isError) = run.Content(0);

        Assert.False(isError);

        // PRD MCP-3's parity claim is about bytes, not about "the same fields": one query,
        // one working directory, one array. decisions.md Q7 makes that array the contract.
        Assert.Equal(cli.Output.TrimEnd('\r', '\n'), text);

        // Terms are AND-ed and there is no stemming, so only the playbook carries both
        // `widget` and `pricing`; the catalog has `widgets` and no `pricing` at all.
        using var results = JsonDocument.Parse(text);
        Assert.Equal(1, results.RootElement.GetArrayLength());
        Assert.Equal("pricing.md", results.RootElement[0].GetProperty("path").GetString());
        Assert.Equal("all", results.RootElement[0].GetProperty("matchMode").GetString());
    }

    [Fact]
    public void SearchResultsCarryTrustAndStalenessAndNeverABody()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_search", """{"query":"widget"}"""));

        var (text, _) = run.Content(0);
        using var results = JsonDocument.Parse(text);

        Assert.All(results.RootElement.EnumerateArray(), result =>
        {
            Assert.Contains(
                result.GetProperty("trustTier").GetString(),
                (string[])["unverified", "machine-confirmed", "human-reviewed"]);
            Assert.True(result.TryGetProperty("stale", out _));
            Assert.False(result.TryGetProperty("body", out _));
        });

        var catalog = results.RootElement.EnumerateArray()
            .First(result => result.GetProperty("path").GetString() == "widgets.md");

        // The fixture's catalog carries a `human:` verification and no stale_after; its
        // pricing playbook expired in 2020.
        Assert.Equal("human-reviewed", catalog.GetProperty("trustTier").GetString());
        Assert.False(catalog.GetProperty("stale").GetBoolean());

        var pricing = results.RootElement.EnumerateArray()
            .First(result => result.GetProperty("path").GetString() == "pricing.md");
        Assert.True(pricing.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void SearchTakesFiltersAsArraysAsBareStringsAndInline()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_search", """{"query":"widget","type":["Reference"]}"""),
            McpHarness.Call(2, "okf_search", """{"query":"widget","type":"Reference"}"""),
            McpHarness.Call(3, "okf_search", """{"query":"widget type:Reference"}"""),
            McpHarness.Call(4, "okf_search", """{"query":"widget","tag":["catalog","widgets"]}"""));

        var array = run.Content(0).Text;
        Assert.Equal(array, run.Content(1).Text);
        Assert.Equal(array, run.Content(2).Text);

        using var typed = JsonDocument.Parse(array);
        Assert.All(
            typed.RootElement.EnumerateArray(),
            result => Assert.Equal("Reference", result.GetProperty("type").GetString()));

        // Repeated tags are AND-ed, so only the concept carrying both survives.
        using var tagged = JsonDocument.Parse(run.Content(3).Text);
        Assert.Equal(1, tagged.RootElement.GetArrayLength());
        Assert.Equal("widgets.md", tagged.RootElement[0].GetProperty("path").GetString());
    }

    [Fact]
    public void SearchWithNothingToMatchOnIsInvalidParams()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_search", "{}"),
            McpHarness.Call(2, "okf_search", """{"query":"   "}"""));

        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(0));
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(1));
        Assert.Contains("okf_list", run.ErrorMessage(0), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"query":"widget","limit":"three"}""")]
    [InlineData("""{"query":"widget","limit":0}""")]
    [InlineData("""{"query":42}""")]
    [InlineData("""{"query":"widget","tag":[7]}""")]
    public void SearchWithAMalformedArgumentIsInvalidParams(string arguments)
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(McpProtocolTests.Environment(vault), vault.Root, McpHarness.Call(1, "okf_search", arguments));

        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(0));
    }

    [Fact]
    public void ReadReturnsFrontmatterBodyTrustTierAndStaleness()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_read", """{"path":"widgets.md"}"""));

        var (text, isError) = run.Content(0);
        Assert.False(isError);

        using var concept = JsonDocument.Parse(text);
        var root = concept.RootElement;

        Assert.Equal("widgets", root.GetProperty("id").GetString());
        Assert.Equal("widgets.md", root.GetProperty("path").GetString());
        Assert.Equal("searchable", root.GetProperty("bundleName").GetString());
        Assert.Equal("Widget catalog", root.GetProperty("title").GetString());
        Assert.Equal("Reference", root.GetProperty("type").GetString());
        Assert.Equal("human-reviewed", root.GetProperty("trustTier").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
        Assert.Contains("A widget is the unit this fixture bundle sells", root.GetProperty("body").GetString()!, StringComparison.Ordinal);

        // Frontmatter travels as parsed: source order, nested mappings, scalars unretyped.
        var frontmatter = root.GetProperty("frontmatter");
        Assert.Equal(
            ["type", "title", "description", "tags", "generated", "verified"],
            frontmatter.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            "okf-net/tests",
            frontmatter.GetProperty("generated").GetProperty("by").GetString());
        Assert.Equal(
            "human:tests@example.invalid",
            frontmatter.GetProperty("verified")[0].GetProperty("by").GetString());
    }

    [Fact]
    public void ReadAcceptsAConceptIdAsWellAsAPath()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_read", """{"path":"notes/gadgets"}"""),
            McpHarness.Call(2, "okf_read", """{"path":"notes/gadgets.md"}"""));

        // A search result's `id` and its `path` differ only by the suffix, and both are
        // things an agent will copy back into a read.
        Assert.Equal(run.Content(0).Text, run.Content(1).Text);
        Assert.False(run.Content(0).IsError);
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("notes/../../escape.md")]
    [InlineData("/etc/passwd")]
    [InlineData("~/secrets.md")]
    [InlineData("notes\\gadgets.md")]
    public void ReadRefusesEveryPathThatLeavesTheBundleRoot(string path)
    {
        using var vault = McpProtocolTests.Vault();
        File.WriteAllText(Path.Combine(vault.Root, "escape.md"), "---\ntype: Secret\n---\n\nOutside every bundle.\n");

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_read", $$"""{"path":"{{path.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}"""));

        // PRD MCP-5: containment is a hard refusal, and nothing of the file leaks into the
        // message that refuses it.
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(0));
        Assert.DoesNotContain("Outside every bundle", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingAConceptThatIsNotThereIsAnErrorResultTheModelCanRecoverFrom()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_read", """{"path":"notes/sprockets.md"}"""));

        var (text, isError) = run.Content(0);

        // Not a JSON-RPC error: the model is the one who has to try something else, and a
        // protocol error would never reach it.
        Assert.True(isError);
        Assert.Contains("okf_search", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnparseableConceptIsReportedRatherThanTreatedAsMissing()
    {
        using var vault = McpProtocolTests.Vault();
        vault.Write(
            Path.Combine("bundles", "searchable", "broken.md"),
            "---\ntitle: Broken\n  bad: [unclosed\n---\n\nBody.\n");

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_read", """{"path":"broken.md"}"""));

        var failure = run.Content(0);
        Assert.True(failure.IsError);
        Assert.Contains("frontmatter that does not parse", failure.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadWithoutAPathIsInvalidParams()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(McpProtocolTests.Environment(vault), vault.Root, McpHarness.Call(1, "okf_read", "{}"));

        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(0));
    }

    [Fact]
    public void ReadNamesTheBundleWhenTwoBundlesHoldTheSamePath()
    {
        using var vault = McpProtocolTests.Vault();
        vault.CopyFixture("searchable", Path.Combine("bundles", "second"));

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_read", """{"path":"widgets.md"}"""),
            McpHarness.Call(2, "okf_read", """{"path":"widgets.md","bundle":"second"}"""),
            McpHarness.Call(3, "okf_read", """{"path":"widgets.md","bundle":"nowhere"}"""));

        var ambiguous = run.Content(0);
        Assert.True(ambiguous.IsError);
        Assert.Contains("more than one bundle", ambiguous.Text, StringComparison.Ordinal);

        var chosen = run.Content(1);
        Assert.False(chosen.IsError);
        using var concept = JsonDocument.Parse(chosen.Text);
        Assert.Equal("second", concept.RootElement.GetProperty("bundleName").GetString());

        var missing = run.Content(2);
        Assert.True(missing.IsError);
        Assert.Contains("searchable", missing.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ListWithNoArgumentsNamesTheBundlesInScope()
    {
        using var vault = McpProtocolTests.Vault();
        vault.CopyFixture("conformant", Path.Combine("bundles", "conformant"));

        var run = McpHarness.Session(McpProtocolTests.Environment(vault), vault.Root, McpHarness.Call(1, "okf_list"));

        var (text, isError) = run.Content(0);
        Assert.False(isError);

        using var scope = JsonDocument.Parse(text);
        Assert.Contains(vault.Root, scope.RootElement.GetProperty("scope").GetString()!, StringComparison.Ordinal);

        var bundles = scope.RootElement.GetProperty("bundles").EnumerateArray().ToList();
        Assert.Equal(
            ["conformant", "searchable"],
            bundles.Select(bundle => bundle.GetProperty("name").GetString()));

        // The searchable fixture holds four concepts; index.md and log.md are reserved files,
        // not concepts (spec §3.1), so they are not counted.
        var searchable = bundles.First(bundle => bundle.GetProperty("name").GetString() == "searchable");
        Assert.Equal(3, searchable.GetProperty("concepts").GetInt32());
    }

    [Fact]
    public void ListUsesTheIndexABundleShipsAndSynthesizesOneWhenItDoesNot()
    {
        using var vault = McpProtocolTests.Vault();
        var bare = vault.CreateDirectory(Path.Combine("bundles", "bare"));
        File.WriteAllText(
            Path.Combine(bare, "alpha.md"),
            "---\ntype: Reference\ntitle: Alpha\ndescription: The first letter.\n---\n\nAlpha.\n");

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_list", """{"bundle":"searchable"}"""),
            McpHarness.Call(2, "okf_list", """{"bundle":"bare"}"""),
            McpHarness.Call(3, "okf_list", """{"bundle":"searchable","path":"notes"}"""));

        using var shipped = JsonDocument.Parse(run.Content(0).Text);
        Assert.Equal("index.md", shipped.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            File.ReadAllText(Path.Combine(vault.Root, "bundles", "searchable", "index.md")),
            shipped.RootElement.GetProperty("listing").GetString());

        // PRD CORE-10/MCP-2: a bundle with no index.md still lists, synthesized in memory,
        // and nothing is written to it.
        using var synthesized = JsonDocument.Parse(run.Content(1).Text);
        Assert.Equal("synthesized", synthesized.RootElement.GetProperty("source").GetString());
        Assert.Contains(
            "* [Alpha](alpha.md) - The first letter.",
            synthesized.RootElement.GetProperty("listing").GetString()!,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(bare, "index.md")));

        using var subdirectory = JsonDocument.Parse(run.Content(2).Text);
        Assert.Equal("notes", subdirectory.RootElement.GetProperty("path").GetString());
        Assert.Contains(
            subdirectory.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("link").GetString() == "gadgets.md");
    }

    [Fact]
    public void ListEntriesCarryTheirSectionLinkAndBlurbAndMarkSubdirectories()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_list", """{"bundle":"searchable"}"""));

        using var listing = JsonDocument.Parse(run.Content(0).Text);
        var entries = listing.RootElement.GetProperty("entries").EnumerateArray().ToList();

        var catalog = entries.First(entry => entry.GetProperty("link").GetString() == "widgets.md");
        Assert.Equal("Reference", catalog.GetProperty("section").GetString());
        Assert.Equal("Widget catalog", catalog.GetProperty("title").GetString());
        Assert.Equal(
            "Every widget the fixture bundle knows about.",
            catalog.GetProperty("description").GetString());
        Assert.False(catalog.GetProperty("subdirectory").GetBoolean());

        var notes = entries.First(entry => entry.GetProperty("subdirectory").GetBoolean());
        Assert.Equal("notes/index.md", notes.GetProperty("link").GetString());
    }

    [Fact]
    public void ListWalksIntoABundleNamedInThePathWhenSeveralAreInScope()
    {
        using var vault = McpProtocolTests.Vault();
        vault.CopyFixture("conformant", Path.Combine("bundles", "conformant"));

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_list", """{"path":"searchable/notes"}"""),
            McpHarness.Call(2, "okf_list", """{"path":"notes"}"""));

        using var walked = JsonDocument.Parse(run.Content(0).Text);
        Assert.Equal("searchable", walked.RootElement.GetProperty("bundleName").GetString());
        Assert.Equal("notes", walked.RootElement.GetProperty("path").GetString());

        // A path that names no bundle, with several in scope, is a question the server
        // refuses to answer by guessing.
        var ambiguous = run.Content(1);
        Assert.True(ambiguous.IsError);
        Assert.Contains("Pass \"bundle\"", ambiguous.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../..")]
    [InlineData("/etc")]
    [InlineData("notes/../../..")]
    public void ListRefusesEveryDirectoryThatLeavesTheBundleRoot(string path)
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_list", $$"""{"bundle":"searchable","path":"{{path}}"}"""));

        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(0));
    }

    [Fact]
    public void ListingADirectoryThatIsNotThereIsAnErrorResult()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_list", """{"bundle":"searchable","path":"archive"}"""));

        Assert.True(run.Content(0).IsError);
        Assert.Contains("archive", run.Content(0).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownToolIsInvalidParams()
    {
        using var vault = McpProtocolTests.Vault();

        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_write", """{"path":"widgets.md"}"""),
            McpHarness.Request(2, "tools/call", """{"arguments":{}}"""),
            McpHarness.Request(3, "tools/call", """{"name":"okf_list","arguments":"not an object"}"""),
            McpHarness.Request(4, "tools/call", """{"name":42,"arguments":{}}"""));

        // MCP-4: there is no write tool, and asking for one is a parameter error naming the
        // tools there are.
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(0));
        Assert.Contains("okf_list, okf_search, and okf_read", run.ErrorMessage(0), StringComparison.Ordinal);
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(1));
        Assert.Contains("string \"name\"", run.ErrorMessage(1), StringComparison.Ordinal);
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(2));
        Assert.Contains("\"arguments\" must be an object", run.ErrorMessage(2), StringComparison.Ordinal);
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(3));
    }

    [Fact]
    public void TheServerReadsNothingOutsideABundleRoot()
    {
        using var project = new TempTree();
        project.CopyFixture("searchable", Path.Combine("okf", "bundles", "searchable"));

        // The raw/ drop zone is a sibling of bundles/, outside every bundle root (Q3), so no
        // tool can reach it: not by search, not by read, not by listing.
        project.Write(Path.Combine("okf", "raw", "captured.md"), "---\ntype: Reference\n---\n\nPhlogiston raw capture.\n");

        var run = McpHarness.Session(
            CliHarness.Environment(project.Root, project.Root),
            null,
            McpHarness.Call(1, "okf_search", """{"query":"phlogiston"}"""),
            McpHarness.Call(2, "okf_read", """{"path":"../raw/captured.md"}"""),
            McpHarness.Call(3, "okf_list", """{"path":"../raw"}"""));

        Assert.DoesNotContain("raw capture", run.Content(0).Text, StringComparison.Ordinal);
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(1));
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(2));
    }

    [SkippableFact]
    public void NoToolFollowsASymlinkOutOfABundleRoot()
    {
        using var project = new TempTree();
        project.CopyFixture("searchable", Path.Combine("okf", "bundles", "searchable"));
        project.Write(Path.Combine("okf", "raw", "captured.md"), "---\ntype: Reference\n---\n\nPhlogiston raw capture.\n");

        var bundle = Path.Combine(project.Root, "okf", "bundles", "searchable");
        try
        {
            // A bundle okf did not produce can carry links, and a `..` that the path grammar
            // refuses is spelled here in the filesystem instead: `borrowed.md` names a file
            // outside every bundle, and `escape/` is a whole directory of them.
            File.CreateSymbolicLink(
                Path.Combine(bundle, "borrowed.md"),
                Path.Combine(project.Root, "okf", "raw", "captured.md"));
            Directory.CreateSymbolicLink(
                Path.Combine(bundle, "escape"),
                Path.Combine(project.Root, "okf", "raw"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SkipException($"This platform will not create symlinks: {exception.Message}");
        }

        var run = McpHarness.Session(
            CliHarness.Environment(project.Root, project.Root),
            null,
            McpHarness.Call(1, "okf_search", """{"query":"phlogiston"}"""),
            McpHarness.Call(2, "okf_read", """{"path":"borrowed.md"}"""),
            McpHarness.Call(3, "okf_read", """{"path":"escape/captured.md"}"""),
            McpHarness.Call(4, "okf_list", """{"bundle":"searchable","path":"escape"}"""),
            McpHarness.Call(5, "okf_list", """{"bundle":"searchable"}"""));

        // PRD MCP-5, and the promise the tool descriptions make to the model: the raw/ drop
        // zone is not readable through okf, by any route.
        Assert.DoesNotContain("raw capture", run.Output, StringComparison.Ordinal);
        Assert.True(run.Content(1).IsError);
        Assert.True(run.Content(2).IsError);
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(3));

        // Nor is the linked file listed as bundle content, which would advertise a read that
        // is going to be refused.
        Assert.DoesNotContain("borrowed.md", run.Content(4).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void APathCarryingANulIsInvalidParamsAndTheServerKeepsGoing()
    {
        using var vault = McpProtocolTests.Vault();

        // `Path.GetFullPath` throws on a NUL rather than answering. It reaches the server as
        // an ordinary JSON escape, so it is a path the client got wrong (-32602), not a
        // reason for every request queued behind it to be lost.
        var run = McpHarness.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            McpHarness.Call(1, "okf_read", """{"path":"wid\u0000gets.md"}"""),
            McpHarness.Call(2, "okf_list", """{"bundle":"searchable","path":"no\u0000tes"}"""),
            McpHarness.Request(3, "ping"));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(0));
        Assert.Equal(McpServer.InvalidParams, run.ErrorCode(1));

        using var pong = run.Response(2);
        Assert.Equal(3, pong.RootElement.GetProperty("id").GetInt32());
    }
}
