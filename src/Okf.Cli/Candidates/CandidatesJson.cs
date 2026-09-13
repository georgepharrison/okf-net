using System.Text.Json;
using Okf.Core;

namespace Okf.Cli.Candidates;

/// <summary>
/// The candidate contract (work item #77): a stable array of the concepts with provably absent
/// verification history, one record each, engine-agnostic — nothing in a record names a lint
/// rule, a verdict vocabulary, a severity, or anything else about how okf-net decided.
/// </summary>
/// <remarks>
/// <para>
/// A bare array, for the same reason <see cref="InboxJson" /> is one: every consumer that reads
/// element 0 as a record keeps working when a field is added, and a run-metadata envelope would
/// break all of them. The counts live on stderr under <c>--verbose</c>, and the completeness of
/// the array is carried by the exit code rather than by a field — a consumer that needs to know
/// whether the list is the whole truth is already reading <c>$?</c>.
/// </para>
/// <para>
/// Field names and their order match <see cref="SearchJson" /> and <see cref="InboxJson" />: the
/// identity block is byte-identical to the inbox's, and the lifecycle block is the inbox's with
/// the two verification fields it needs and this one cannot have removed. A consumer that parses
/// one of the three writers parses all three, which is the only reason the order is worth pinning.
/// </para>
/// <para>
/// <para>
/// WHY THE WRITER SITS HERE AND NOT IN <c>Okf.Core</c>
/// ---------------------------------------------------
/// The reuse target is the deferred MCP tool (#83), whose contract is that its payload is
/// "produced by the same writer the command uses" — the byte-parity claim <see cref="McpToolset" />
/// already makes by handing <see cref="SearchJson" /> to both <c>okf search --json</c> and
/// <c>okf_search</c>. That parity is a command-to-tool claim, and both surfaces live in this
/// assembly, so the reuse this ticket owes is a single <c>internal</c> writer with one caller and
/// no second implementation of the semantics (#71's Out-of-Scope: the writer is "placed for later
/// reuse so the semantics are not duplicated"). Moving it into <c>Okf.Core</c> would make this the
/// only one of the three reporting writers there — <see cref="SearchJson" /> and
/// <see cref="InboxJson" /> are siblings for the same reason — and would buy no consumer that
/// #83 does not already reach from here.
/// </para>
/// <para>
/// Quarantined concepts are deliberately absent. They are not candidates, and mixing two kinds of
/// record into one array would make every consumer branch on a discriminator to find the list it
/// asked for. They are named on standard error instead, in both formats.
/// </para>
/// </remarks>
internal static class CandidatesJson
{
    /// <summary>Renders a scan as the candidate array.</summary>
    /// <param name="result">The scan result.</param>
    /// <param name="baseDirectory">Paths are reported relative to this directory when they sit under it.</param>
    /// <returns>The JSON array.</returns>
    public static string Write(OkfCandidateResult result, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);

        return JsonOutput.Write(writer =>
        {
            writer.WriteStartArray();
            foreach (OkfCandidate candidate in result.Candidates)
            {
                WriteCandidate(writer, candidate, baseDirectory);
            }

            writer.WriteEndArray();
        });
    }

    private static void WriteCandidate(Utf8JsonWriter writer, OkfCandidate candidate, string baseDirectory)
    {
        OkfConcept concept = candidate.Concept;
        writer.WriteStartObject();
        WriteIdentity(writer, concept, baseDirectory);
        WriteContent(writer, concept);
        WriteLifecycle(writer, candidate, concept);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Where the concept is, four ways: the id a caller cites, the bundle-relative path, the
    /// display path, and the absolute path. The last is the one an automated verifier opens, and
    /// it is spelled out rather than left to be reconstructed from the other three (#71 story 5).
    /// </summary>
    private static void WriteIdentity(Utf8JsonWriter writer, OkfConcept concept, string baseDirectory)
    {
        writer.WriteString("id", concept.Id);
        writer.WriteString("path", concept.Path);
        writer.WriteString("displayPath", DiagnosticWriter.Display(concept.AbsolutePath, baseDirectory));
        writer.WriteString("absolutePath", concept.AbsolutePath);
        writer.WriteString("bundle", concept.Bundle.Root);
        writer.WriteString("bundleName", concept.Bundle.Name);
        writer.WriteString("title", concept.Title);
    }

    /// <summary>What the concept is, so a caller can brief a reviewer without re-parsing it (#71 story 6).</summary>
    private static void WriteContent(Utf8JsonWriter writer, OkfConcept concept)
    {
        JsonOutput.WriteStringOrNull(writer, "type", concept.Type);
        JsonOutput.WriteStringOrNull(writer, "description", concept.Description);
        writer.WriteStartArray("tags");
        foreach (string tag in concept.Tags)
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// The state a caller uses to order its work. The tier is reported as derived even where it
    /// disagrees with the verdict — a quarantined concept can read <c>machine-confirmed</c> to
    /// §5.3 — and <c>status</c> and <c>generated</c> are reported as written, so skipping drafts or
    /// preferring recent material stays the caller's policy and never becomes this command's
    /// opinion (#71 stories 7, 8 and 9).
    /// </summary>
    /// <remarks>
    /// There are no verification fields. Every candidate has zero verification events by
    /// construction — that is what makes it a candidate — so a <c>verifiedBy</c> or an event count
    /// here would be a field that is always null or always zero: one a consumer has to read, then
    /// learn to disbelieve. <c>verifiedBy</c> and <c>verifiedAt</c> are the inbox's, where history
    /// exists to be reported.
    /// </remarks>
    private static void WriteLifecycle(Utf8JsonWriter writer, OkfCandidate candidate, OkfConcept concept)
    {
        writer.WriteString("trustTier", concept.TrustTier.ToSpecString());
        writer.WriteBoolean("stale", concept.Stale);
        JsonOutput.WriteStringOrNull(writer, "status", OkfVerificationStamp.Status(concept));
        JsonOutput.WriteStringOrNull(writer, "generatedBy", OkfVerificationStamp.GeneratedBy(concept));
        JsonOutput.WriteStringOrNull(writer, "generatedAt", OkfVerificationStamp.GeneratedAt(concept));
        JsonOutput.WriteStringOrNull(writer, "staleAfter", candidate.StaleAfter);
    }
}
