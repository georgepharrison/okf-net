using System.Text.Json;
using Okf.Core;

namespace Okf.Cli.Candidates;

/// <summary>
/// The candidate contract (work item #75): a stable array of the concepts with provably absent
/// verification history, one record each, engine-agnostic — nothing in a record names a lint
/// rule, a severity, or anything else about how okf-net decided.
/// </summary>
/// <remarks>
/// A bare array, for the same reason <see cref="InboxJson" /> is one: every consumer that reads
/// element 0 as a record keeps working when a field is added, and a run-metadata envelope would
/// break all of them. The counts live on stderr under <c>--verbose</c>, and the completeness of
/// the array is carried by the exit code rather than by a field — a consumer that needs to know
/// whether the list is the whole truth is already reading <c>$?</c>.
///
/// Quarantined concepts are deliberately absent. They are not candidates, and mixing two kinds of
/// record into one array would make every consumer branch on a discriminator to find the list it
/// asked for. They are named on standard error instead, in both formats.
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
        OkfVerificationStamp stamp = OkfVerificationStamp.Read(concept);

        writer.WriteStartObject();
        WriteIdentity(writer, concept, baseDirectory);
        WriteContent(writer, concept);
        WriteVerdict(writer, concept, stamp);
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
    /// The verdict and the state a caller uses to order its work. The tier is reported as derived
    /// even where it disagrees with the verdict — a quarantined concept can read
    /// <c>machine-confirmed</c> to §5.3 — and <c>status</c> and <c>generated</c> are reported as
    /// written, so skipping drafts or preferring recent material stays the caller's policy and
    /// never becomes this command's opinion (#71 stories 7, 8 and 9).
    /// </summary>
    private static void WriteVerdict(Utf8JsonWriter writer, OkfConcept concept, OkfVerificationStamp stamp)
    {
        writer.WriteString("trustTier", concept.TrustTier.ToSpecString());
        writer.WriteBoolean("stale", concept.Stale);
        JsonOutput.WriteStringOrNull(writer, "status", OkfVerificationStamp.Status(concept));
        JsonOutput.WriteStringOrNull(writer, "generatedBy", OkfVerificationStamp.GeneratedBy(concept));
        JsonOutput.WriteStringOrNull(writer, "generatedAt", OkfVerificationStamp.GeneratedAt(concept));
        writer.WriteNumber("verificationEvents", stamp.Events.Count);
        writer.WriteNumber("unreadableVerificationEvents", stamp.MalformedEventCount);
        writer.WriteString("reason", stamp.WhyCandidate);
    }
}
