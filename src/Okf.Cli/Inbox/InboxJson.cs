using Okf.Core;

namespace Okf.Cli.Inbox;

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

        return JsonOutput.Write(writer =>
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
                JsonOutput.WriteStringOrNull(writer, "type", concept.Type);

                writer.WriteStartArray("reasons");
                foreach (var reason in item.Reasons)
                {
                    writer.WriteStringValue(reason.ToWireString());
                }

                writer.WriteEndArray();

                writer.WriteString("trustTier", concept.TrustTier.ToSpecString());
                writer.WriteBoolean("stale", concept.Stale);
                JsonOutput.WriteStringOrNull(writer, "status", item.Status);
                JsonOutput.WriteStringOrNull(writer, "generatedBy", item.GeneratedBy);
                JsonOutput.WriteStringOrNull(writer, "generatedAt", item.GeneratedAt);
                JsonOutput.WriteStringOrNull(writer, "verifiedBy", item.VerifiedBy);
                JsonOutput.WriteStringOrNull(writer, "verifiedAt", item.VerifiedAt);
                JsonOutput.WriteStringOrNull(writer, "staleAfter", item.StaleAfter);
                JsonOutput.WriteNumberOrNull(writer, "ageDays", item.AgeDays);
                JsonOutput.WriteNumberOrNull(writer, "staleDays", item.StaleDays);

                writer.WriteStartArray("driftedSources");
                foreach (var source in item.DriftedSources)
                {
                    writer.WriteStartObject();
                    JsonOutput.WriteStringOrNull(writer, "id", source.Id);
                    JsonOutput.WriteStringOrNull(writer, "resource", source.Resource);
                    writer.WriteString("lastModified", source.LastModified);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }
}
