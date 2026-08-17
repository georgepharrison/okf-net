using System.Text.Json;

namespace Okf.Cli.Mcp;

/// <summary>
/// A failure that travels back to the client as a JSON-RPC error object rather than as a
/// crash. Bad parameters are the client's bug to fix, so they are reported with a code and
/// a message it can act on (PRD MCP-2).
/// </summary>
internal sealed class McpProtocolException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The error message.</param>
    public McpProtocolException(int code, string message)
        : base(message) => Code = code;

    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; }

    /// <summary>Builds an <c>Invalid params</c> failure.</summary>
    /// <param name="message">What is wrong with the parameters.</param>
    /// <returns>The exception to throw.</returns>
    public static McpProtocolException InvalidParams(string message) =>
        new(McpServer.InvalidParams, message);
}

/// <summary>
/// The MCP stdio server: a hand-rolled JSON-RPC 2.0 loop over newline-delimited JSON
/// (PRD MCP-1). It owns nothing but the protocol — every answer comes from
/// <see cref="McpToolset" />, which is itself a thin adapter over <c>Okf.Core</c>
/// (decisions.md §4).
/// </summary>
/// <remarks>
/// <para><b>Why hand-rolled.</b> The surface is four methods — <c>initialize</c>,
/// <c>tools/list</c>, <c>tools/call</c>, <c>ping</c> — and the same reasoning that kept
/// <c>System.CommandLine</c> out of the CLI applies: the exit-code contract (PRD CLI-14) and
/// the NativeAOT publish (CLI-17) are ours to keep, and a framework's process lifetime would
/// have to be overridden to keep them. The evaluation of the official SDK, and the evidence
/// behind this call, is recorded in decisions.md.</para>
/// <para><b>Synchronous by construction.</b> One line is read, handled, written and flushed
/// before the next is read. Nothing is in flight when stdin reaches EOF, so a client that
/// writes its requests and closes the pipe — a shell here-doc, a CI harness — always gets
/// every response it asked for.</para>
/// <para><b>stdout belongs to the protocol.</b> Nothing but JSON-RPC is ever written to it.
/// Notes go to stderr, and only under <c>--verbose</c>.</para>
/// </remarks>
internal sealed class McpServer
{
    /// <summary>Invalid JSON was received.</summary>
    public const int ParseError = -32700;

    /// <summary>The payload is not a valid JSON-RPC request object.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>The method does not exist.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>The parameters are missing, malformed, or name something that cannot be served.</summary>
    public const int InvalidParams = -32602;

    /// <summary>The server failed while handling an otherwise valid request.</summary>
    public const int InternalError = -32603;

    /// <summary>The protocol revision okf-net speaks, and the one it offers by default.</summary>
    public const string LatestProtocolVersion = "2025-06-18";

    /// <summary>The JSON-RPC version every message carries.</summary>
    private const string JsonRpcVersion = "2.0";

    /// <summary>
    /// The protocol revisions okf-net answers on. A client asking for one of these gets it
    /// back verbatim; a client asking for anything else gets
    /// <see cref="LatestProtocolVersion" /> and decides for itself whether to continue,
    /// which is what the spec's version negotiation asks a server to do.
    /// </summary>
    private static readonly string[] SupportedProtocolVersions =
        [LatestProtocolVersion, "2025-03-26", "2024-11-05"];

    private readonly McpToolset _tools;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    /// <summary>Initializes a server.</summary>
    /// <param name="tools">The tools the server exposes.</param>
    /// <param name="input">The request stream (stdin).</param>
    /// <param name="output">The response stream (stdout), which carries nothing else.</param>
    public McpServer(McpToolset tools, TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        _tools = tools;
        _input = input;
        _output = output;
    }

    /// <summary>
    /// Reads requests until stdin closes, answering each one. A protocol error is answered,
    /// never thrown: the loop survives anything a client can send.
    /// </summary>
    /// <returns>The process exit code — always success, because closing stdin is how an MCP client says goodbye.</returns>
    public int Run()
    {
        while (_input.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            Handle(line);
        }

        return CliApplication.ExitSuccess;
    }

    private void Handle(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            WriteError(null, ParseError, $"Parse error: {exception.Message}");
            return;
        }

        using (document)
        {
            if (Request(document.RootElement) is { } request)
            {
                Answer(request);
            }
        }
    }

    private sealed record IncomingRequest(JsonElement Root, bool IsRequest, JsonElement? Identifier);

    private IncomingRequest? Request(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            // JSON-RPC batching was removed from MCP in the 2025-06-18 revision, and a
            // batch has no single id to answer under, so it is refused as a whole.
            WriteError(null, InvalidRequest, "Batch requests are not supported; send one JSON-RPC message per line.");
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            WriteError(null, InvalidRequest, "A JSON-RPC message must be an object.");
            return null;
        }

        // A message with no id (or a null id) is a notification: the spec forbids a
        // response to one, including an error response, so an invalid notification is
        // dropped rather than answered.
        bool isRequest = root.TryGetProperty("id", out JsonElement id) && id.ValueKind != JsonValueKind.Null;
        return new IncomingRequest(root, isRequest, isRequest ? id : null);
    }

    private void Answer(IncomingRequest request)
    {
        try
        {
            Respond(request);
        }
        catch (McpProtocolException exception)
        {
            WriteError(request.Identifier, exception.Code, exception.Message);
        }
#pragma warning disable CA1031 // deliberate: see the comment below — the stdio loop's
        // contract is that it survives whatever arrives on stdin, so this is the one
        // place a failure of any type is turned back into a response instead of killing
        // the loop.
        catch (Exception exception)
        {
            // A bundle that moved or turned unreadable under us is the environment's
            // failure, not the client's, and it must not take the server down. Nor may
            // anything else: the loop's contract is that it survives whatever arrives on
            // stdin, and a client reaches code that throws types this method cannot
            // enumerate — `System.Text.Json` alone throws `InvalidOperationException`
            // when a string carrying an unpaired `\uD800` escape is read, which is legal
            // to parse and possible in any field. Dying there would lose every request
            // still queued behind it, so an unexpected failure is reported as one and
            // the next line is read.
            WriteError(request.Identifier, InternalError, exception.Message);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Validates one parsed message and answers it. Everything that can throw lives here
    /// rather than in <see cref="Handle" />, which owns the one place a failure is turned
    /// back into a response.
    /// </summary>
    private void Respond(IncomingRequest request)
    {
        if (!HasJsonRpcVersion(request.Root))
        {
            RefuseIfRequest(request, $"Every message must carry \"jsonrpc\": \"{JsonRpcVersion}\".");
            return;
        }

        if (!request.Root.TryGetProperty("method", out JsonElement method) || method.ValueKind != JsonValueKind.String)
        {
            RefuseIfRequest(request, "A JSON-RPC message must carry a string \"method\".");
            return;
        }

        if (!request.IsRequest)
        {
            // Nothing okf-net exposes changes on a notification: it has no subscriptions
            // and no write tools (MCP-4). `notifications/initialized` and the rest are
            // therefore accepted and ignored, silently, as the spec requires.
            return;
        }

        JsonElement? parameters = request.Root.TryGetProperty("params", out JsonElement value)
            ? value
            : (JsonElement?)null;
        Dispatch(method.GetString()!, parameters, request.Identifier);
    }

    private static bool HasJsonRpcVersion(JsonElement root) =>
        root.TryGetProperty("jsonrpc", out JsonElement version)
        && version.ValueKind == JsonValueKind.String
        && string.Equals(version.GetString(), JsonRpcVersion, StringComparison.Ordinal);

    private void RefuseIfRequest(IncomingRequest request, string message)
    {
        if (request.IsRequest)
        {
            WriteError(request.Identifier, InvalidRequest, message);
        }
    }

    private void Dispatch(string method, JsonElement? parameters, JsonElement? id)
    {
        switch (method)
        {
            case "initialize":
                WriteResult(id, Initialize(parameters));
                break;

            case "ping":
                WriteResult(id, "{}");
                break;

            case "tools/list":
                WriteResult(id, McpToolset.List());
                break;

            case "tools/call":
                WriteResult(id, _tools.Call(parameters));
                break;

            default:
                throw new McpProtocolException(
                    MethodNotFound,
                    $"Unknown method '{method}'. okf serves initialize, tools/list, tools/call, and ping.");
        }
    }

    /// <summary>
    /// The <c>initialize</c> result: the negotiated protocol revision, the one capability
    /// okf-net has (tools), who it is, and the doctrine a client should read before calling
    /// anything.
    /// </summary>
    private static string Initialize(JsonElement? parameters)
    {
        return JsonOutput.WriteWire(writer => WriteInitialize(writer, NegotiatedVersion(parameters)));
    }

    private static string NegotiatedVersion(JsonElement? parameters)
    {
        string? requested = parameters is { ValueKind: JsonValueKind.Object } value
            && value.TryGetProperty("protocolVersion", out JsonElement version)
            && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        return requested is not null && Array.IndexOf(SupportedProtocolVersions, requested) >= 0
            ? requested
            : LatestProtocolVersion;
    }

    private static void WriteInitialize(Utf8JsonWriter writer, string negotiated)
    {
        writer.WriteStartObject();
        writer.WriteString("protocolVersion", negotiated);
        writer.WriteStartObject("capabilities");
        writer.WriteStartObject("tools");

        // The tool list is fixed at startup, so there is never a change to notify.
        writer.WriteBoolean("listChanged", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartObject("serverInfo");
        writer.WriteString("name", "okf");
        writer.WriteString("version", CliApplication.Version);
        writer.WriteEndObject();
        writer.WriteString("instructions", McpToolset.Instructions);
        writer.WriteEndObject();
    }

    private void WriteResult(JsonElement? id, string resultJson)
    {
        Send(JsonOutput.WriteWire(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", JsonRpcVersion);
            WriteId(writer, id);
            writer.WritePropertyName("result");
            writer.WriteRawValue(resultJson);
            writer.WriteEndObject();
        }));
    }

    private void WriteError(JsonElement? id, int code, string message)
    {
        Send(JsonOutput.WriteWire(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", JsonRpcVersion);
            WriteId(writer, id);
            writer.WriteStartObject("error");
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }));
    }

    private static void WriteId(Utf8JsonWriter writer, JsonElement? id)
    {
        writer.WritePropertyName("id");
        if (id is not { } value)
        {
            writer.WriteNullValue();
            return;
        }

        // Echoed verbatim, whatever the client used: JSON-RPC ids may be strings or numbers,
        // and a client is entitled to get its own back. Rendered into a buffer first because
        // an id is client-supplied and need not be writable — a string carrying an unpaired
        // `\uD800` escape parses but throws on the way back out — and this method sits on the
        // path that reports failures. Failing here would either kill the loop or leave a
        // half-written envelope on the wire, so an id that cannot be echoed becomes null.
        try
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter scratch = new Utf8JsonWriter(buffer, JsonOutput.WireOptions))
            {
                value.WriteTo(scratch);
            }

            writer.WriteRawValue(buffer.ToArray());
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            writer.WriteNullValue();
        }
    }

    /// <summary>
    /// Writes one message and flushes it. The framing is one JSON object per line, so the
    /// newline is part of the protocol and is written explicitly rather than left to the
    /// writer's platform line ending.
    /// </summary>
    private void Send(string message)
    {
        _output.Write(message);
        _output.Write('\n');
        _output.Flush();
    }
}
