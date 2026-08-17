using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Okf.Cli;

/// <summary>
/// How okf renders JSON: one writer setup for everything a person or a tool reads
/// (<c>--json</c>, the MCP payloads), and one for what travels on the MCP wire.
/// </summary>
/// <remarks>
/// Both shapes use the relaxed encoder because okf's own text carries backticks and
/// section signs, which the default encoder escapes to <c>\u00XX</c> — valid JSON that a
/// CI annotation or a model reads as noise. Nothing okf writes is embedded in HTML, which
/// is the one thing that encoder guards against.
/// </remarks>
internal static class JsonOutput
{
    /// <summary>
    /// The read shape: indented, because a <c>--json</c> payload is read in a terminal and
    /// in a diff as often as it is piped through <c>jq</c>.
    /// </summary>
    private static readonly JsonWriterOptions ReadOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The wire shape: never indented. The MCP stdio framing is one JSON object per line,
    /// so a raw newline inside a message splits it in two. A payload's own newlines are
    /// safe because a payload travels as a JSON string, where they are escaped.
    /// </summary>
    internal static readonly JsonWriterOptions WireOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Renders what <paramref name="body" /> writes, in the read shape.</summary>
    /// <param name="body">Writes the document; the writer is flushed and disposed here.</param>
    /// <returns>The JSON text, with no trailing newline.</returns>
    public static string Write(Action<Utf8JsonWriter> body) => Render(body, ReadOptions);

    /// <summary>Renders what <paramref name="body" /> writes, in the wire shape.</summary>
    /// <param name="body">Writes the document; the writer is flushed and disposed here.</param>
    /// <returns>The JSON text, on one line, with no trailing newline.</returns>
    public static string WriteWire(Action<Utf8JsonWriter> body) => Render(body, WireOptions);

    /// <summary>Writes a string property, or JSON <c>null</c> when there is no value.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value, or <see langword="null" />.</param>
    public static void WriteStringOrNull(Utf8JsonWriter writer, string name, string? value)
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

    /// <summary>Writes a number property, or JSON <c>null</c> when there is no value.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value, or <see langword="null" />.</param>
    public static void WriteNumberOrNull(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static string Render(Action<Utf8JsonWriter> body, JsonWriterOptions options)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            body(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
