using System.Text.Json;

namespace Okf.Cli.Tests;

/// <summary>
/// What the MCP tools accept as arguments and what they refuse. AD-30 fixes the served
/// scope at launch and no tool argument may widen it, so the path and bundle arguments are
/// a closed vocabulary: a directory is bundle-relative or it is refused, and a bundle is
/// named out of the set already in scope.
/// </summary>
public class McpToolArgumentTests
{
    /// <summary>
    /// Every spelling that would leave the bundle is refused. Each row isolates one clause
    /// of the guard, so a guard that lost that clause stops refusing that row.
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData("topics/..")]
    [InlineData("../elsewhere")]
    [InlineData("/etc")]
    [InlineData("~")]
    [InlineData("~/elsewhere")]
    [InlineData("topics\\\\windows")]
    [InlineData("topics\\u0000null")]
    public void ListRefusesAPathThatLeavesTheBundle(string path)
    {
        using var vault = McpProtocolTests.Vault();

        var run = Mcp.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            Mcp.Call(1, "okf_list", $$"""{"path":"{{path}}"}"""));

        Assert.Contains(
            "is not a bundle-relative directory",
            run.ErrorMessage(0),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The spellings that are merely untidy are normalized rather than refused: an empty
    /// path, a lone <c>.</c>, a doubled separator and a <c>./</c> prefix all name the same
    /// directory the plain form does.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("./")]
    public void ListNormalizesAnUntidyPathToTheBundleRoot(string path)
    {
        using var vault = McpProtocolTests.Vault();
        var environment = McpProtocolTests.Environment(vault);

        var untidy = Mcp.Session(environment, vault.Root, Mcp.Call(1, "okf_list", $$"""
            {"bundle":"searchable","path":"{{path}}"}
            """));
        var plain = Mcp.Session(environment, vault.Root, Mcp.Call(1, "okf_list", """
            {"bundle":"searchable"}
            """));

        var (untidyText, untidyFailed) = untidy.Content(0);
        var (plainText, _) = plain.Content(0);
        Assert.False(untidyFailed);
        Assert.Equal(plainText, untidyText);
    }

    /// <summary>
    /// A listing names the bundle it came from, where its entries came from, and the
    /// entries themselves — <c>index.md</c> when the bundle has one on disk, and
    /// <c>synthesized</c> when the listing was built rather than read (PRD MCP-2).
    /// </summary>
    [Fact]
    public void ListNamesTheBundleAndWhereTheListingCameFrom()
    {
        using var vault = McpProtocolTests.Vault();

        var run = Mcp.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            Mcp.Call(1, "okf_list", """{"bundle":"searchable"}"""));

        var (text, isError) = run.Content(0);
        Assert.False(isError);

        using var listing = JsonDocument.Parse(text);
        Assert.True(Path.IsPathRooted(listing.RootElement.GetProperty("bundle").GetString()));
        Assert.Contains(
            listing.RootElement.GetProperty("source").GetString(),
            (string[])["index.md", "synthesized", "empty"]);
        Assert.NotEmpty(listing.RootElement.GetProperty("entries").EnumerateArray());
    }

    /// <summary>
    /// The orienting call — no arguments at all — reports the vault and one entry per
    /// bundle, each with its root and how many concepts it holds. It is the only answer a
    /// client has before it knows any name, so every field is load-bearing.
    /// </summary>
    [Fact]
    public void ListWithoutArgumentsReportsTheScopeWithConceptCounts()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory(Path.Combine("okf", "bundles", "notes"));
        tree.Write(Path.Combine("okf", "bundles", "notes", "index.md"), "# notes\n");
        tree.Write(Path.Combine("okf", "bundles", "notes", "log.md"), "# log\n");
        foreach (var name in (string[])["one", "two", "three", "four"])
        {
            tree.Write(
                Path.Combine("okf", "bundles", "notes", $"{name}.md"),
                $"---\nid: {name}\ntype: concept\ntitle: {name}\n---\n\nBody.\n");
        }

        var run = Mcp.Session(
            Cli.Environment(tree.Root, tree.Root),
            Path.Combine(tree.Root, "okf"),
            Mcp.Call(1, "okf_list"));

        var (text, isError) = run.Content(0);
        Assert.False(isError);

        using var scope = JsonDocument.Parse(text);
        Assert.Equal(Path.Combine(tree.Root, "okf"), scope.RootElement.GetProperty("vault").GetString());
        var listed = Assert.Single(scope.RootElement.GetProperty("bundles").EnumerateArray().ToList());
        Assert.Equal(bundle, listed.GetProperty("path").GetString());

        // Four concepts beside two reserved files, deliberately unequal counts: index.md
        // and log.md are the bundle's own bookkeeping (§8), and a count that included them
        // would overstate every bundle in scope.
        Assert.Equal(4, listed.GetProperty("concepts").GetInt32());
    }

    /// <summary>
    /// A bundle name that is not in scope is refused with the names that are — and when
    /// two bundles in scope share a name, the refusal says so rather than pretending the
    /// name is unknown, because the client's next move is different in each case.
    /// </summary>
    [Fact]
    public void AnUnknownBundleNameIsRefusedWithTheNamesThatAreInScope()
    {
        using var vault = McpProtocolTests.Vault();

        var run = Mcp.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            Mcp.Call(1, "okf_list", """{"bundle":"nowhere"}"""));

        var (text, isError) = run.Content(0);
        Assert.True(isError);
        Assert.Contains("No bundle named 'nowhere' is in scope.", text, StringComparison.Ordinal);
        Assert.Contains("In scope: searchable.", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two vaults can hold a bundle of the same directory name. There the name no longer
    /// picks one out, so it is refused as ambiguous and every bundle is labelled by its
    /// absolute root instead (AD-30: the argument stays a name picked out of a listing).
    /// </summary>
    [Fact]
    public void ADuplicatedBundleNameIsRefusedAsAmbiguousAndLabelledByRoot()
    {
        using var tree = new TempTree();
        var first = tree.CopyFixture("searchable", Path.Combine("one", "bundles", "notes"));
        var second = tree.CopyFixture("searchable", Path.Combine("two", "bundles", "notes"));
        var environment = Cli.Environment(tree.Root, tree.Root);

        Assert.Equal(
            CliApplication.ExitSuccess,
            Cli.Run(environment, "register", Path.Combine(tree.Root, "one")).ExitCode);
        Assert.Equal(
            CliApplication.ExitSuccess,
            Cli.Run(environment, "register", Path.Combine(tree.Root, "two")).ExitCode);

        var run = Mcp.Run(
            environment,
            ["--scope", "registered"],
            Mcp.Call(1, "okf_list", """{"bundle":"notes"}"""));

        var (text, isError) = run.Content(0);
        Assert.True(isError);
        Assert.Contains(
            "More than one bundle in scope is named 'notes'. Name it by its label.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(first, text, StringComparison.Ordinal);
        Assert.Contains(second, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A concept that is not there is reported by naming where it was looked for: the one
    /// bundle by name when there is one, the count when there are several.
    /// </summary>
    [Fact]
    public void AMissingConceptNamesTheSingleBundleItWasLookedForIn()
    {
        using var vault = McpProtocolTests.Vault();

        var run = Mcp.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            Mcp.Call(1, "okf_read", """{"path":"nowhere.md"}"""));

        var (text, isError) = run.Content(0);
        Assert.True(isError);
        Assert.Contains("in bundle 'searchable'", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// With more than one bundle in scope the same refusal counts them instead, because no
    /// single bundle name would be the truth.
    /// </summary>
    [Fact]
    public void AMissingConceptCountsTheBundlesWhenThereAreSeveral()
    {
        using var tree = new TempTree();
        tree.CopyFixture("searchable", Path.Combine("okf", "bundles", "notes"));
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));

        var run = Mcp.Session(
            Cli.Environment(tree.Root, tree.Root),
            Path.Combine(tree.Root, "okf"),
            Mcp.Call(1, "okf_read", """{"path":"nowhere.md"}"""));

        var (text, isError) = run.Content(0);
        Assert.True(isError);
        Assert.Contains("in any of the 2 bundles in scope", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A filter may be one string or an array of them, and anything else is a protocol
    /// error rather than a silently ignored argument — a client whose filter was dropped
    /// would read the unfiltered results as filtered ones.
    /// </summary>
    [Theory]
    [InlineData("""{"query":"widget","type":5}""")]
    [InlineData("""{"query":"widget","type":["concept",5]}""")]
    [InlineData("""{"query":"widget","tag":{"name":"pricing"}}""")]
    public void AFilterThatIsNeitherAStringNorAnArrayOfStringsIsAProtocolError(string arguments)
    {
        using var vault = McpProtocolTests.Vault();

        var run = Mcp.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            Mcp.Call(1, "okf_search", arguments));

        Assert.Contains(
            "must be a string or an array of strings",
            run.ErrorMessage(0),
            StringComparison.Ordinal);
    }

    /// <summary>A scalar filter means the same as the one-element array (decisions.md Q7).</summary>
    [Fact]
    public void AScalarFilterMeansTheOneElementArray()
    {
        using var vault = McpProtocolTests.Vault();
        var environment = McpProtocolTests.Environment(vault);

        var scalar = Mcp.Session(environment, vault.Root, Mcp.Call(1, "okf_search", """
            {"query":"widget","type":"concept"}
            """));
        var array = Mcp.Session(environment, vault.Root, Mcp.Call(1, "okf_search", """
            {"query":"widget","type":["concept"]}
            """));

        var (scalarText, scalarFailed) = scalar.Content(0);
        var (arrayText, _) = array.Content(0);
        Assert.False(scalarFailed);
        Assert.Equal(arrayText, scalarText);
    }

    /// <summary>
    /// A concept read back carries the fields a client needs to cite it — its absolute
    /// path, its bundle, its description and its tags — beside the body. The description
    /// and the tags are pinned to the fixture's own frontmatter rather than merely asserted
    /// to be present: a `tags` array that came back empty reads as a concept that carries
    /// no tags, which is a different citation. (The search-result path being a legal
    /// `okf_read` argument is asserted separately in <see cref="McpAcceptanceTests" />.)
    /// </summary>
    [Fact]
    public void AReadConceptCarriesItsPathBundleDescriptionAndTags()
    {
        using var vault = McpProtocolTests.Vault();

        var run = Mcp.Session(
            McpProtocolTests.Environment(vault),
            vault.Root,
            Mcp.Call(1, "okf_read", """{"path":"widgets.md"}"""));

        var (text, isError) = run.Content(0);
        Assert.False(isError);

        using var concept = JsonDocument.Parse(text);
        Assert.True(Path.IsPathRooted(concept.RootElement.GetProperty("absolutePath").GetString()));
        Assert.True(Path.IsPathRooted(concept.RootElement.GetProperty("bundle").GetString()));
        Assert.Equal(
            "Every widget the fixture bundle knows about.",
            concept.RootElement.GetProperty("description").GetString());
        Assert.Equal(
            (string?[])["catalog", "widgets"],
            concept.RootElement.GetProperty("tags").EnumerateArray()
                .Select(tag => tag.GetString()).ToArray());
    }
}
