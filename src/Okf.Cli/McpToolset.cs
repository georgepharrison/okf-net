using System.Globalization;
using System.Text;
using System.Text.Json;
using Okf.Core;

namespace Okf.Cli;

/// <summary>One tool as <c>tools/list</c> reports it.</summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Description">What it does, and where it sits in the doctrine.</param>
/// <param name="InputSchema">Its JSON Schema, as raw JSON.</param>
internal sealed record McpTool(string Name, string Description, string InputSchema);

/// <summary>What one tool call produced.</summary>
/// <param name="Text">The text content block.</param>
/// <param name="IsError">Whether the call failed in a way the caller can recover from.</param>
internal sealed record McpToolResult(string Text, bool IsError)
{
    /// <summary>A successful call.</summary>
    /// <param name="text">The payload.</param>
    /// <returns>The result.</returns>
    public static McpToolResult Ok(string text) => new(text, IsError: false);

    /// <summary>
    /// A call that ran but could not answer — an unknown concept, a bundle that is not in
    /// scope. Reported as an error <em>result</em> rather than a JSON-RPC error because the
    /// model is the one who has to recover from it (search again, list the directory), and
    /// a protocol error would be hidden from it.
    /// </summary>
    /// <param name="message">What went wrong and what to try instead.</param>
    /// <returns>The result.</returns>
    public static McpToolResult Failed(string message) => new(message, IsError: true);
}

/// <summary>
/// The three read-only tools <c>okf mcp</c> exposes (PRD MCP-2, MCP-4), and their
/// implementations over <c>Okf.Core</c>. Nothing here decides anything: scope comes from
/// <see cref="OkfDiscovery" />, ranking from <see cref="OkfSearchEngine" />, listings from
/// <see cref="OkfIndexGenerator" />, and concept reads from
/// <see cref="OkfConceptReader" /> — the same code the CLI calls (decisions.md §4).
/// </summary>
internal sealed class McpToolset
{
    /// <summary>The doctrine, handed to the client in the <c>initialize</c> result.</summary>
    public const string Instructions = """
        okf serves OKF v0.2 knowledge bundles: directories of markdown concepts with YAML
        frontmatter, readable without okf and never requiring you to run bundle-supplied code.

        Work in three moves, in this order:

        1. Orient by disclosure. `okf_list` with no arguments names the bundles in scope; with
           a bundle it returns that directory's listing — the bundle's own index.md when it
           ships one, a synthesized listing when it does not. Walk down a level at a time
           rather than guessing at paths.
        2. Retrieve by search. `okf_search` ranks concepts and answers links-first: path,
           title, type, tags, trust tier, stale flag, score, and a short snippet. It never
           returns a body, so it points into disclosure rather than replacing it.
        3. Open only what you picked. `okf_read` returns one concept's frontmatter and body,
           with its trust tier and staleness computed. Read the ones you chose, not the page
           of results.

        Judge before you trust: every concept carries a trust tier (unverified,
        machine-confirmed, human-reviewed) and a stale flag. Prefer human-reviewed and fresh;
        say so when you rely on something unverified or stale.

        Everything here is read-only. Nothing is served from outside a bundle root — the raw/
        capture drop zone is never readable through okf, and neither is any path that leaves
        the bundle. Stamping (`okf verify`) and index generation (`okf index`) are CLI
        operations, deliberately not tools.
        """;

    /// <summary>
    /// How a payload is written: the text a tool hands back, which the client shows to a
    /// model. Indented, because it is read.
    /// </summary>
    private static readonly JsonWriterOptions PayloadOptions = new()
    {
        Indented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// How a protocol structure is written — the <c>tools/list</c> result, the content
    /// envelope. These are embedded verbatim in a response, and the stdio framing is one
    /// JSON object per line, so they must never be indented: a raw newline inside a message
    /// splits it in two on the wire. A payload's newlines are safe because a payload travels
    /// as a JSON string, where they are escaped.
    /// </summary>
    private static readonly JsonWriterOptions EnvelopeOptions = new()
    {
        Indented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly McpTool[] Tools =
    [
        new McpTool(
            "okf_list",
            """
            Orient here first. With no arguments, names every knowledge bundle in scope. With a
            bundle (and optionally a directory inside it), returns that directory's listing:
            the bundle's own index.md when it ships one, and an equivalent listing synthesized
            on the fly when it does not — so progressive disclosure works on bundles okf did
            not produce. Listings carry titles, links and blurbs, never bodies: walk down a
            level with okf_list, then open a concept with okf_read. Use this before searching
            when you do not yet know what a bundle contains.
            """,
            """
            {
              "type": "object",
              "properties": {
                "bundle": {
                  "type": "string",
                  "description": "The bundle's name, as reported by okf_list with no arguments. Optional when exactly one bundle is in scope."
                },
                "path": {
                  "type": "string",
                  "description": "A bundle-relative directory, e.g. 'policies'. Omit for the bundle root. Never absolute, never containing '..'."
                }
              },
              "additionalProperties": false
            }
            """),
        new McpTool(
            "okf_search",
            """
            Retrieve by search when you know roughly what you want but not where it lives.
            Ranking is deterministic and lexical: the same bundles and the same query always
            give the same order. Results are links-first — bundle-relative path, concept id,
            title, type, tags, an opaque higher-is-better score, the trust tier, the stale
            flag, and a short snippet — and never include a concept's body, so a hit is
            something to judge and then open with okf_read, not something to quote. Query
            terms are AND-ed; when nothing matches all of them the search falls back to
            matching any, and every result says which in its matchMode field. Filters may be
            passed as the type/tag arguments or written inline in the query as
            'type:Metric tag:cost-optimization'; repeated type filters are OR-ed, repeated tag
            filters are AND-ed, and both are compared case-insensitively on the whole value.
            """,
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "The search terms. Tokenized, not substring-matched, and case-insensitive. May carry inline 'type:' and 'tag:' filters."
                },
                "type": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Only concepts whose frontmatter `type` is one of these (OR). A bare string is accepted for one filter."
                },
                "tag": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Only concepts carrying all of these tags (AND). A bare string is accepted for one filter."
                },
                "limit": {
                  "type": "integer",
                  "minimum": 1,
                  "description": "How many results to return at most. Defaults to 10."
                }
              },
              "additionalProperties": false
            }
            """),
        new McpTool(
            "okf_read",
            """
            Open one concept, after you have picked it out of okf_list or okf_search. Takes a
            bundle-relative path — a search result's `path` or its `id`, whose '.md' suffix is
            optional — and returns the concept's frontmatter, its markdown body, and the two
            judgements to weigh before trusting it: the derived trust tier (unverified,
            machine-confirmed, human-reviewed) and whether it is stale. Frontmatter keys are
            returned in source order, including keys okf does not model, and scalars are
            carried as written rather than retyped. Paths are confined to a bundle root:
            absolute paths and '..' traversal are refused, and nothing outside a bundle is
            ever served. Open what you chose, not the whole result page.
            """,
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "The concept's bundle-relative path or id, e.g. 'policies/revenue-recognition.md' or 'policies/revenue-recognition'."
                },
                "bundle": {
                  "type": "string",
                  "description": "The bundle's name. Optional; needed only when the same path exists in more than one bundle in scope."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """),
    ];

    private readonly OkfEnvironment environment;
    private readonly string? path;

    /// <summary>Initializes the toolset.</summary>
    /// <param name="environment">The environment vaults and configuration resolve against.</param>
    /// <param name="path">The explicit target path <c>okf mcp</c> was given, if any.</param>
    public McpToolset(OkfEnvironment environment, string? path)
    {
        ArgumentNullException.ThrowIfNull(environment);
        this.environment = environment;
        this.path = path;
    }

    /// <summary>The date staleness is judged against; overridable so tests are deterministic.</summary>
    public DateOnly? Today { get; set; }

    /// <summary>Resolves the working set exactly as every other command does (PRD MCP-3).</summary>
    /// <returns>The resolved working set.</returns>
    /// <exception cref="McpProtocolException">The vault could not be resolved.</exception>
    public OkfWorkingSet Resolve()
    {
        try
        {
            // Resolved per call rather than cached: the server is long-lived, and a bundle
            // added to the vault while it runs must be visible to the next call, exactly as
            // it would be to the next `okf search` invocation.
            return OkfDiscovery.Resolve(this.path, this.environment);
        }
        catch (OkfDiscoveryException exception)
        {
            throw new McpProtocolException(McpServer.InternalError, exception.Message);
        }
    }

    /// <summary>Renders the <c>tools/list</c> result.</summary>
    /// <returns>The result JSON.</returns>
    public string List()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, EnvelopeOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("tools");
            foreach (var tool in Tools)
            {
                writer.WriteStartObject();
                writer.WriteString("name", tool.Name);
                writer.WriteString("description", Collapse(tool.Description));
                writer.WritePropertyName("inputSchema");

                // Re-emitted through the writer rather than copied verbatim: the schemas are
                // written indented to stay readable in source, and a raw copy would carry
                // those newlines into the message and split it across lines on the wire.
                using var schema = JsonDocument.Parse(tool.InputSchema);
                schema.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Runs one <c>tools/call</c> request.</summary>
    /// <param name="parameters">The request's <c>params</c>.</param>
    /// <returns>The result JSON.</returns>
    /// <exception cref="McpProtocolException">The parameters are missing or malformed.</exception>
    public string Call(JsonElement? parameters)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } value)
        {
            throw McpProtocolException.InvalidParams("tools/call requires a params object with a tool name.");
        }

        if (!value.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
        {
            throw McpProtocolException.InvalidParams("tools/call requires a string \"name\".");
        }

        var arguments = value.TryGetProperty("arguments", out var given) ? given : (JsonElement?)null;
        if (arguments is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Null })
        {
            throw McpProtocolException.InvalidParams("\"arguments\" must be an object.");
        }

        var result = name.GetString() switch
        {
            "okf_list" => ListTool(arguments),
            "okf_search" => SearchTool(arguments),
            "okf_read" => ReadTool(arguments),
            var unknown => throw McpProtocolException.InvalidParams(
                $"Unknown tool '{unknown}'. okf exposes okf_list, okf_search, and okf_read."),
        };

        return Content(result);
    }

    private McpToolResult SearchTool(JsonElement? arguments)
    {
        var query = OkfSearchQuery.Parse(String(arguments, "query"));
        foreach (var type in Strings(arguments, "type"))
        {
            query.AddTypeFilter(type);
        }

        foreach (var tag in Strings(arguments, "tag"))
        {
            query.AddTagFilter(tag);
        }

        var limit = Integer(arguments, "limit") ?? 10;
        if (limit <= 0)
        {
            throw McpProtocolException.InvalidParams($"\"limit\" expects a positive whole number; got {limit}.");
        }

        if (query.IsEmpty)
        {
            // The same refusal `okf search` makes with no query (PRD CLI-11): returning the
            // whole vault would be a listing, and okf_list is the tool for that.
            throw McpProtocolException.InvalidParams(
                "No query. Give at least one search term, or a type/tag filter. To browse rather than search, call okf_list.");
        }

        var outcome = OkfSearchEngine.Search(
            Resolve().Bundles,
            query,
            new OkfSearchOptions { Limit = limit, Today = Now });

        // Byte-for-byte the array `okf search --json` prints (decisions.md Q7): one contract,
        // one writer, so the CLI and the MCP tool cannot drift apart (PRD MCP-3).
        return McpToolResult.Ok(SearchJson.Write(outcome));
    }

    private McpToolResult ReadTool(JsonElement? arguments)
    {
        var requested = String(arguments, "path")
            ?? throw McpProtocolException.InvalidParams("okf_read requires a \"path\": a concept's bundle-relative path or id.");

        var relative = OkfConceptReader.Normalize(requested)
            ?? throw McpProtocolException.InvalidParams(
                $"'{requested}' is not a bundle-relative path. Paths are relative to a bundle root, "
                + "never absolute, and never contain '..'.");

        var workingSet = Resolve();
        var wanted = String(arguments, "bundle");
        if (wanted is not null)
        {
            return Bundle(workingSet, wanted) is { } named
                ? Read(named, relative)
                : McpToolResult.Failed(NoSuchBundle(workingSet, wanted));
        }

        var hits = new List<OkfConcept>();
        foreach (var bundle in workingSet.Bundles)
        {
            var result = OkfConceptReader.Read(bundle, relative, new OkfConceptOptions { Today = Now });
            if (result.Concept is { } concept)
            {
                hits.Add(concept);
            }
            else if (result.Status == OkfConceptStatus.Unparseable)
            {
                return McpToolResult.Failed(result.Message);
            }
        }

        return hits.Count switch
        {
            1 => McpToolResult.Ok(Concept(hits[0])),
            0 => Missing(workingSet, relative),

            // Two bundles may hold the same relative path; guessing between them would make
            // the answer depend on resolution order, so the caller picks.
            _ => McpToolResult.Failed(
                $"'{relative}' exists in more than one bundle in scope ("
                + string.Join(", ", hits.Select(hit => hit.Bundle.Name))
                + "). Pass \"bundle\" to say which."),
        };
    }

    private McpToolResult ListTool(JsonElement? arguments)
    {
        var workingSet = Resolve();
        var wanted = String(arguments, "bundle");
        var requested = String(arguments, "path");

        if (wanted is null && requested is null)
        {
            // No arguments is the orienting call: what is in scope at all.
            return McpToolResult.Ok(Scope(workingSet));
        }

        var relative = NormalizeDirectory(requested)
            ?? throw McpProtocolException.InvalidParams(
                $"'{requested}' is not a bundle-relative directory. Directories are relative to a bundle root, "
                + "never absolute, and never contain '..'.");

        OkfBundle bundle;
        if (wanted is not null)
        {
            if (Bundle(workingSet, wanted) is not { } named)
            {
                return McpToolResult.Failed(NoSuchBundle(workingSet, wanted));
            }

            bundle = named;
        }
        else if (workingSet.Bundles.Count == 1)
        {
            bundle = workingSet.Bundles[0];
        }
        else if (Bundle(workingSet, relative.Split('/')[0]) is { } prefixed)
        {
            // `okf_list` with a path that starts with a bundle name reads the way a listing
            // looks: the scope listing names the bundles, so a caller naturally walks into
            // one by name.
            bundle = prefixed;
            relative = string.Join('/', relative.Split('/').Skip(1));
        }
        else
        {
            return McpToolResult.Failed(
                $"{workingSet.Bundles.Count} bundles are in scope ("
                + string.Join(", ", workingSet.Bundles.Select(item => item.Name))
                + "). Pass \"bundle\" to say which one to list.");
        }

        if (!bundle.TryResolve(relative, out var directory))
        {
            throw McpProtocolException.InvalidParams(
                $"'{relative}' resolves outside bundle '{bundle.Name}'. okf lists only directories inside a bundle root.");
        }

        if (!Directory.Exists(directory))
        {
            return McpToolResult.Failed(
                $"No directory '{relative}' in bundle '{bundle.Name}'. List the bundle root to see what it holds.");
        }

        return McpToolResult.Ok(Listing(bundle, relative, directory));
    }

    private McpToolResult Read(OkfBundle bundle, string relative)
    {
        var result = OkfConceptReader.Read(bundle, relative, new OkfConceptOptions { Today = Now });
        return result.Concept is { } concept
            ? McpToolResult.Ok(Concept(concept))
            : McpToolResult.Failed(result.Message);
    }

    private static McpToolResult Missing(OkfWorkingSet workingSet, string relative) =>
        McpToolResult.Failed(
            $"No concept '{relative}' in "
            + (workingSet.Bundles.Count == 1
                ? $"bundle '{workingSet.Bundles[0].Name}'"
                : $"any of the {workingSet.Bundles.Count.ToString(CultureInfo.InvariantCulture)} bundles in scope")
            + ". Find it with okf_search, or list its directory with okf_list.");

    private static string NoSuchBundle(OkfWorkingSet workingSet, string wanted) =>
        $"No bundle named '{wanted}' is in scope. In scope: "
        + string.Join(", ", workingSet.Bundles.Select(bundle => bundle.Name))
        + ".";

    private static OkfBundle? Bundle(OkfWorkingSet workingSet, string name) =>
        workingSet.Bundles.FirstOrDefault(
            bundle => string.Equals(bundle.Name, name, StringComparison.Ordinal));

    /// <summary>Renders the bundles in scope — the orienting listing.</summary>
    private static string Scope(OkfWorkingSet workingSet)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, PayloadOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("scope", workingSet.Resolution);
            WriteStringOrNull(writer, "vault", workingSet.VaultRoot);
            writer.WriteStartArray("bundles");
            foreach (var bundle in workingSet.Bundles)
            {
                writer.WriteStartObject();
                writer.WriteString("name", bundle.Name);
                writer.WriteString("path", bundle.Root);
                writer.WriteNumber("concepts", bundle.MarkdownFiles().Count(file => !OkfBundle.IsReservedFile(file)));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Renders one directory's listing (PRD MCP-2, CORE-10). The bundle's own
    /// <c>index.md</c> is the listing when it ships one — including a hand-written foreign
    /// one, which is the whole point — and the synthesized rendering stands in when it does
    /// not. Nothing is written to the bundle either way.
    /// </summary>
    private static string Listing(OkfBundle bundle, string relative, string directory)
    {
        var index = OkfIndexGenerator.Plan(bundle).For(directory);
        var listing = index?.ExistingContent ?? index?.Content ?? string.Empty;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, PayloadOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("bundle", bundle.Root);
            writer.WriteString("bundleName", bundle.Name);
            writer.WriteString("path", relative);
            writer.WriteString(
                "source",
                index is null ? "empty" : index.ExistingContent is null ? "synthesized" : "index.md");

            writer.WriteStartArray("entries");
            foreach (var entry in index?.Entries ?? [])
            {
                writer.WriteStartObject();
                writer.WriteString("section", entry.Section);
                writer.WriteString("title", entry.Title);
                writer.WriteString("link", entry.Link);
                WriteStringOrNull(writer, "description", entry.Description);
                writer.WriteBoolean("subdirectory", entry.IsSubdirectory);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("listing", listing);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Renders one concept: the CORE-12 read, in the search records' vocabulary.</summary>
    private static string Concept(OkfConcept concept)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, PayloadOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("id", concept.Id);
            writer.WriteString("path", concept.Path);
            writer.WriteString("absolutePath", concept.AbsolutePath);
            writer.WriteString("bundle", concept.Bundle.Root);
            writer.WriteString("bundleName", concept.Bundle.Name);
            writer.WriteString("title", concept.Title);
            WriteStringOrNull(writer, "type", concept.Type);
            WriteStringOrNull(writer, "description", concept.Description);
            writer.WriteStartArray("tags");
            foreach (var tag in concept.Tags)
            {
                writer.WriteStringValue(tag);
            }

            writer.WriteEndArray();
            writer.WriteString("trustTier", concept.TrustTier.ToSpecString());
            writer.WriteBoolean("stale", concept.Stale);
            writer.WritePropertyName("frontmatter");
            WriteValue(writer, concept.Frontmatter);
            writer.WriteString("body", concept.Body);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Projects a frontmatter value into JSON. Scalars travel as their source text — a date,
    /// a version pin, a number-shaped string all come back as written — because PRD CORE-2's
    /// no-retyping rule is exactly what makes an unknown producer key survive a round trip,
    /// and a JSON projection that guessed types would undo it one field at a time.
    /// </summary>
    private static void WriteValue(Utf8JsonWriter writer, OkfValue value)
    {
        switch (value)
        {
            case OkfScalar scalar when scalar.IsNull:
                writer.WriteNullValue();
                break;

            case OkfScalar scalar:
                writer.WriteStringValue(scalar.Value);
                break;

            case OkfSequence sequence:
                writer.WriteStartArray();
                foreach (var item in sequence)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;

            case OkfMapping mapping:
                writer.WriteStartObject();
                foreach (var entry in mapping.Entries)
                {
                    // YAML permits a non-scalar key; JSON does not, and no bundle uses one.
                    if (entry.Key is OkfScalar key)
                    {
                        writer.WritePropertyName(key.Value);
                        WriteValue(writer, entry.Value);
                    }
                }

                writer.WriteEndObject();
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>Wraps a tool result in the MCP content envelope.</summary>
    private static string Content(McpToolResult result)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, EnvelopeOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", result.Text);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteBoolean("isError", result.IsError);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private DateOnly Now => Today ?? DateOnly.FromDateTime(DateTime.Now);

    private static void WriteStringOrNull(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    /// <summary>A tool description is prose, and a client renders it as one paragraph.</summary>
    private static string Collapse(string text) =>
        string.Join(' ', text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));

    /// <summary>
    /// Normalizes a bundle-relative directory, or <see langword="null" /> when the argument
    /// is not one. The rules are <see cref="OkfConceptReader.Normalize" />'s, minus the
    /// <c>.md</c> suffix and plus an empty path meaning the bundle root.
    /// </summary>
    private static string? NormalizeDirectory(string? requested)
    {
        var trimmed = requested?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains('\0', StringComparison.Ordinal)
            || trimmed.StartsWith('/')
            || trimmed.StartsWith('~')
            || Path.IsPathRooted(trimmed))
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var segment in trimmed.Split('/'))
        {
            if (segment.Length == 0 || string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return null;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string? String(JsonElement? arguments, string name)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty(name, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : throw McpProtocolException.InvalidParams($"\"{name}\" must be a string.");
    }

    private static int? Integer(JsonElement? arguments, string name)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty(name, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
            ? number
            : throw McpProtocolException.InvalidParams($"\"{name}\" must be a whole number.");
    }

    /// <summary>
    /// Reads a repeatable filter. The schema says array of string; a bare string is accepted
    /// too, because one filter is the common case and a client that spells it as a scalar is
    /// not wrong enough to fail.
    /// </summary>
    private static IReadOnlyList<string> Strings(JsonElement? arguments, string name)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty(name, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return [property.GetString()!];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw McpProtocolException.InvalidParams($"\"{name}\" must be a string or an array of strings.");
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw McpProtocolException.InvalidParams($"\"{name}\" must be a string or an array of strings.");
            }

            values.Add(item.GetString()!);
        }

        return values;
    }
}
