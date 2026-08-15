using System.Text.Json;

namespace Okf.Cli.Tests;

/// <summary>
/// The JSON-RPC 2.0 surface of <c>okf mcp</c> (PRD MCP-1): the handshake, the method set,
/// and what happens when a client sends something malformed.
/// </summary>
/// <remarks>
/// The expected wire shapes are not derived from okf-net's own code. They were taken from
/// the official ModelContextProtocol C# SDK (v2.2.0), driven over stdio during the
/// build-versus-buy evaluation recorded in decisions.md: <c>initialize</c> answers with
/// <c>{protocolVersion, capabilities:{tools:{…}}, serverInfo:{name, version}}</c>,
/// <c>ping</c> answers with <c>{}</c>, and every response carries <c>jsonrpc</c>, the
/// request's <c>id</c>, and a <c>result</c>. Those transcripts are the oracle these
/// assertions encode.
/// </remarks>
public class McpProtocolTests
{
    [Fact]
    public void InitializeAnswersWithTheNegotiatedRevisionTheToolCapabilityAndTheDoctrine()
    {
        using var vault = Vault();

        var run = Mcp.Session(Environment(vault), vault.Root, Mcp.Initialize());

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Single(run.Responses);

        using var message = run.Response(0);
        Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, message.RootElement.GetProperty("id").GetInt32());

        var result = message.RootElement.GetProperty("result");
        Assert.Equal(Mcp.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("okf", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(CliApplication.Version, result.GetProperty("serverInfo").GetProperty("version").GetString());

        // The doctrine travels with the handshake, so a client learns the three moves before
        // it calls anything.
        var instructions = result.GetProperty("instructions").GetString()!;
        Assert.Contains("okf_list", instructions, StringComparison.Ordinal);
        Assert.Contains("okf_search", instructions, StringComparison.Ordinal);
        Assert.Contains("okf_read", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderSupportedRevisionIsEchoedBackAndAnUnknownOneIsAnsweredWithTheLatest()
    {
        using var vault = Vault();

        var run = Mcp.Session(
            Environment(vault),
            vault.Root,
            Mcp.Initialize(1, "2024-11-05"),
            Mcp.Initialize(2, "1999-01-01"));

        using var supported = run.Result(0);
        using var unknown = run.Result(1);

        // The spec's negotiation: answer with the requested revision when it is spoken, and
        // with one the server does speak when it is not.
        Assert.Equal("2024-11-05", supported.RootElement.GetProperty("protocolVersion").GetString());
        Assert.Equal(Mcp.ProtocolVersion, unknown.RootElement.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void NotificationsAreNeverAnsweredAndDoNotStopTheServer()
    {
        using var vault = Vault();

        var run = Mcp.Session(
            Environment(vault),
            vault.Root,
            Mcp.Initialize(),
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":7}}""",

            // A notification naming a method the server does not have is still a
            // notification: JSON-RPC forbids answering one, even to complain.
            """{"jsonrpc":"2.0","method":"notifications/nonsense"}""",
            Mcp.Request(2, "ping"));

        Assert.Equal(2, run.Responses.Length);
        using var pong = run.Response(1);
        Assert.Equal(2, pong.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void PingAnswersWithAnEmptyResult()
    {
        using var vault = Vault();

        var run = Mcp.Session(Environment(vault), vault.Root, Mcp.Request(9, "ping"));

        using var result = run.Result(0);
        Assert.Equal(JsonValueKind.Object, result.RootElement.ValueKind);
        Assert.Empty(result.RootElement.EnumerateObject());
    }

    [Fact]
    public void AnUnknownMethodIsMethodNotFound()
    {
        using var vault = Vault();

        var run = Mcp.Session(Environment(vault), vault.Root, Mcp.Request(3, "resources/list"));

        Assert.Equal(McpServer.MethodNotFound, run.ErrorCode(0));
        Assert.Contains("resources/list", run.ErrorMessage(0), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedJsonIsAParseErrorAndTheServerKeepsGoing()
    {
        using var vault = Vault();

        var run = Mcp.Session(
            Environment(vault),
            vault.Root,
            "{not json at all",
            Mcp.Request(4, "ping"));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(2, run.Responses.Length);
        Assert.Equal(McpServer.ParseError, run.ErrorCode(0));

        using var failure = run.Response(0);
        Assert.Equal(JsonValueKind.Null, failure.RootElement.GetProperty("id").ValueKind);

        // The point of the test: a protocol error is answered, not fatal.
        using var pong = run.Result(1);
        Assert.Empty(pong.RootElement.EnumerateObject());
    }

    [Theory]
    [InlineData("""["a batch", "of messages"]""")]
    [InlineData("\"a bare string\"")]
    [InlineData("""{"jsonrpc":"1.0","id":5,"method":"ping"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":5}""")]
    [InlineData("""{"jsonrpc":"2.0","id":5,"method":42}""")]
    public void AMessageThatIsNotAJsonRpcRequestIsRejectedWithoutStoppingTheServer(string message)
    {
        using var vault = Vault();

        var run = Mcp.Session(Environment(vault), vault.Root, message, Mcp.Request(6, "ping"));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(2, run.Responses.Length);
        Assert.Equal(McpServer.InvalidRequest, run.ErrorCode(0));

        using var pong = run.Response(1);
        Assert.Equal(6, pong.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void EveryResponseIsExactlyOneLine()
    {
        using var vault = Vault();

        var run = Mcp.Session(
            Environment(vault),
            vault.Root,
            Mcp.Initialize(),
            Mcp.Request(2, "tools/list"),
            Mcp.Call(3, "okf_search", """{"query":"widget"}"""),
            Mcp.Call(4, "okf_read", """{"path":"widgets.md"}"""),
            Mcp.Call(5, "okf_list"));

        // The stdio framing is one JSON object per line. A payload full of markdown — bodies,
        // snippets, listings — must therefore travel with its newlines escaped inside a JSON
        // string, never as raw bytes in the message.
        Assert.Equal(5, run.Responses.Length);
        Assert.Equal(5, run.Output.Count(character => character == '\n'));
        Assert.All(run.Responses, response => JsonDocument.Parse(response).Dispose());

        // Each payload really does carry markdown, so the escaping is doing work rather than
        // the test having picked five newline-free answers.
        Assert.Contains("\\n", run.Responses[3], StringComparison.Ordinal);
    }

    [Fact]
    public void IdsComeBackVerbatimAndInRequestOrder()
    {
        using var vault = Vault();

        var run = Mcp.Session(
            Environment(vault),
            vault.Root,
            Mcp.Initialize(),
            """{"jsonrpc":"2.0","id":"a-string-id","method":"ping"}""",
            Mcp.Request(1000, "ping"));

        using var first = run.Response(1);
        using var second = run.Response(2);
        Assert.Equal("a-string-id", first.RootElement.GetProperty("id").GetString());
        Assert.Equal(1000, second.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void ClosingStdinIsACleanShutdown()
    {
        using var vault = Vault();

        // No requests at all: the client connected and went away, which is how an MCP client
        // says goodbye over stdio (PRD §3: 0 clean shutdown).
        var run = Mcp.Session(Environment(vault), vault.Root);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void ToolsListNamesTheThreeReadOnlyToolsWithSchemasAndTheDoctrine()
    {
        using var vault = Vault();

        var run = Mcp.Session(Environment(vault), vault.Root, Mcp.Request(2, "tools/list"));

        using var result = run.Result(0);
        var tools = result.RootElement.GetProperty("tools").EnumerateArray().ToList();

        Assert.Equal(
            ["okf_list", "okf_search", "okf_read"],
            tools.Select(tool => tool.GetProperty("name").GetString()));

        Assert.All(tools, tool =>
        {
            var schema = tool.GetProperty("inputSchema");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, schema.GetProperty("properties").ValueKind);
            Assert.NotEmpty(tool.GetProperty("description").GetString()!);
        });

        // Each description teaches where its tool sits in the sequence, so an agent that
        // reads only the tool list still learns to orient, then search, then open.
        var descriptions = tools.ToDictionary(
            tool => tool.GetProperty("name").GetString()!,
            tool => tool.GetProperty("description").GetString()!,
            StringComparer.Ordinal);

        Assert.Contains("Orient here first", descriptions["okf_list"], StringComparison.Ordinal);
        Assert.Contains("okf_read", descriptions["okf_list"], StringComparison.Ordinal);
        Assert.Contains("never include a concept's body", descriptions["okf_search"], StringComparison.Ordinal);
        Assert.Contains("okf_read", descriptions["okf_search"], StringComparison.Ordinal);
        Assert.Contains("after you have picked it", descriptions["okf_read"], StringComparison.Ordinal);
        Assert.Contains("confined to a bundle root", descriptions["okf_read"], StringComparison.Ordinal);
    }

    [Fact]
    public void McpHelpIsSuccessAndDescribesTheStdioContract()
    {
        using var vault = Vault();

        var run = Mcp.Run(Environment(vault), ["--help"]);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf mcp [path]", run.Output, StringComparison.Ordinal);
        Assert.Contains("stdio", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownOptionIsAUsageFailure()
    {
        using var vault = Vault();

        var run = Mcp.Run(Environment(vault), ["--port", "8080"]);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Empty(run.Output);
        Assert.Contains("--port", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnresolvableVaultIsAStartupFailureRatherThanAServerThatFailsEveryCall()
    {
        using var empty = new TempTree();

        var run = Mcp.Session(
            Cli.Environment(empty.Root, empty.Root),
            null,
            Mcp.Initialize());

        // PRD §3's CLI-surface table: 2 is startup failure. Nothing may reach stdout, because
        // stdout is the protocol channel and a client would try to parse it.
        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Empty(run.Output);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void VerboseReportsResolutionOnStderrAndLeavesStdoutToTheProtocol()
    {
        using var vault = Vault();

        var run = Mcp.Run(Environment(vault), ["--verbose", vault.Root], Mcp.Initialize());

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf: resolved", run.Error, StringComparison.Ordinal);
        Assert.Contains("searchable", run.Error, StringComparison.Ordinal);
        Assert.All(run.Responses, response => JsonDocument.Parse(response).Dispose());
    }

    /// <summary>A vault holding the searchable fixture bundle.</summary>
    internal static TempTree Vault()
    {
        var tree = new TempTree();
        tree.CopyFixture("searchable", Path.Combine("bundles", "searchable"));
        return tree;
    }

    /// <summary>An environment whose working directory and home are the temporary vault.</summary>
    internal static Okf.Core.OkfEnvironment Environment(TempTree vault) =>
        Cli.Environment(vault.Root, vault.Root);
}
