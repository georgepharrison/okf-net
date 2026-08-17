using Okf.Core;

namespace Okf.Cli.Search;

/// <summary>
/// The search result contract (decisions.md Q7): a stable sorted array of result records,
/// engine-agnostic — nothing in a record names BM25, tokens, or fields.
/// </summary>
/// <remarks>
/// One writer, two surfaces. PRD CLI-11 makes this array <c>okf search --json</c>'s output
/// and MCP-2/MCP-3 make it <c>okf_search</c>'s, and "identical query, identical working
/// directory, identical results" is a claim about bytes. Rendering it twice would let the
/// two drift apart one field at a time, so the CLI and the MCP tool call this.
/// </remarks>
internal static class SearchJson
{
    /// <summary>
    /// Renders an outcome as the result array. Always indented: both surfaces print it that
    /// way, and a switch here would be the seam byte-parity is supposed to close.
    /// </summary>
    /// <param name="outcome">The search outcome.</param>
    /// <returns>The JSON array.</returns>
    public static string Write(OkfSearchOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return JsonOutput.Write(writer =>
        {
            // A bare array, not an envelope: PRD CLI-11 and MCP-2 read the same records,
            // and every field a result needs to be judged travels on the result itself.
            writer.WriteStartArray();
            foreach (OkfSearchResult result in outcome.Results)
            {
                writer.WriteStartObject();
                writer.WriteString("id", result.Id);
                writer.WriteString("path", result.Path);
                writer.WriteString("absolutePath", result.AbsolutePath);
                writer.WriteString("bundle", result.Bundle);
                writer.WriteString("bundleName", result.BundleName);
                writer.WriteString("title", result.Title);
                JsonOutput.WriteStringOrNull(writer, "type", result.Type);
                JsonOutput.WriteStringOrNull(writer, "description", result.Description);
                writer.WriteStartArray("tags");
                foreach (string tag in result.Tags)
                {
                    writer.WriteStringValue(tag);
                }

                writer.WriteEndArray();
                writer.WriteNumber("score", result.Score);
                writer.WriteString("snippet", result.Snippet);
                writer.WriteString("trustTier", result.TrustTier.ToSpecString());
                writer.WriteBoolean("stale", result.Stale);

                // Per result rather than in an envelope, so the contract stays one array:
                // an OR fallback is something an agent must be able to see.
                writer.WriteString("matchMode", MatchMode(outcome.MatchMode));
                writer.WriteStartArray("matchedTerms");
                foreach (string term in result.MatchedTerms)
                {
                    writer.WriteStringValue(term);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    /// <summary>The wire spelling of a match mode: <c>all</c>, <c>any</c>, or <c>filter</c>.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Its wire spelling.</returns>
    public static string MatchMode(OkfSearchMatchMode mode) => mode switch
    {
        OkfSearchMatchMode.All => "all",
        OkfSearchMatchMode.Any => "any",
        OkfSearchMatchMode.Filter => "filter",
        _ => "unknown",
    };
}
