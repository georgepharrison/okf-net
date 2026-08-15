using System.Text;
using System.Text.Json;
using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// The inbox contract (PRD CLI-12): a stable sorted array of the concepts needing human
/// attention, one record each, engine-agnostic — nothing in a record names a lint rule, a
/// severity, or anything else about how okf-net decided.
/// </summary>
/// <remarks>
/// A bare array, for the same reason <see cref="SearchJson" /> is one: every consumer that
/// reads element 0 as a record keeps working when a field is added, and a run-metadata
/// envelope would break all of them. The counts live on stderr under <c>--verbose</c>.
/// </remarks>
internal static class InboxJson
{
    /// <summary>Renders a scan as the item array.</summary>
    /// <param name="result">The scan result.</param>
    /// <param name="baseDirectory">Paths are reported relative to this directory when they sit under it.</param>
    /// <returns>The JSON array.</returns>
    public static string Write(OkfInboxResult result, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var buffer = new MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartArray();
            foreach (var item in result.Items)
            {
                var concept = item.Concept;
                writer.WriteStartObject();
                writer.WriteString("id", concept.Id);
                writer.WriteString("path", concept.Path);
                writer.WriteString("displayPath", DiagnosticWriter.Display(concept.AbsolutePath, baseDirectory));
                writer.WriteString("absolutePath", concept.AbsolutePath);
                writer.WriteString("bundle", concept.Bundle.Root);
                writer.WriteString("bundleName", concept.Bundle.Name);
                writer.WriteString("title", concept.Title);
                WriteStringOrNull(writer, "type", concept.Type);

                writer.WriteStartArray("reasons");
                foreach (var reason in item.Reasons)
                {
                    writer.WriteStringValue(reason.ToWireString());
                }

                writer.WriteEndArray();

                writer.WriteString("trustTier", concept.TrustTier.ToSpecString());
                writer.WriteBoolean("stale", concept.Stale);
                WriteStringOrNull(writer, "status", item.Status);
                WriteStringOrNull(writer, "generatedBy", item.GeneratedBy);
                WriteStringOrNull(writer, "generatedAt", item.GeneratedAt);
                WriteStringOrNull(writer, "verifiedBy", item.VerifiedBy);
                WriteStringOrNull(writer, "verifiedAt", item.VerifiedAt);
                WriteStringOrNull(writer, "staleAfter", item.StaleAfter);
                WriteNumberOrNull(writer, "ageDays", item.AgeDays);
                WriteNumberOrNull(writer, "staleDays", item.StaleDays);

                writer.WriteStartArray("driftedSources");
                foreach (var source in item.DriftedSources)
                {
                    writer.WriteStartObject();
                    WriteStringOrNull(writer, "id", source.Id);
                    WriteStringOrNull(writer, "resource", source.Resource);
                    writer.WriteString("lastModified", source.LastModified);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

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

    private static void WriteNumberOrNull(Utf8JsonWriter writer, string name, int? value)
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
}
