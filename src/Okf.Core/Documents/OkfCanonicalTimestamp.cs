using System.Globalization;

namespace Okf.Core.Documents;

/// <summary>
/// The canonical form okf-net writes instants in: <b>RFC 3339 UTC with a <c>Z</c> suffix
/// and second precision</b>, e.g. <c>2026-08-15T14:00:00Z</c>.
/// </summary>
/// <remarks>
/// <para>OKF v0.2 does not fix a form — §5.2's <c>generated.at</c> is an ISO 8601 instant
/// and every ISO 8601 spelling parses. That freedom produced three spellings inside one
/// repository (dogfood friction #19-9 and #20-12): Google's reference bundles stamp
/// Z-suffixed UTC, the dogfood bundle stamped whatever local offset `date -Iseconds`
/// handed the author, and the capture manifest stamped a third. Nothing was wrong and
/// nothing sorted.</para>
/// <para>UTC with <c>Z</c> is the pick because it matches the reference bundles, because
/// a Z-suffixed second-precision stamp sorts lexicographically in the same order it sorts
/// chronologically — which every diff, index, and log listing quietly relies on — and
/// because a local offset records where the author was sitting, which is not information
/// the format asked for. Second precision because a knowledge concept is not a
/// high-frequency event and subsecond digits are noise in a reviewed diff.</para>
/// <para>Reading stays tolerant: nothing here rejects another spelling, and existing
/// stamps are not rewritten. This is what okf-net <em>writes</em>.</para>
/// </remarks>
public static class OkfCanonicalTimestamp
{
    /// <summary>The canonical format string: RFC 3339 UTC, second precision, <c>Z</c> suffix.</summary>
    public const string CanonicalFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>Renders an instant in the canonical form.</summary>
    /// <param name="instant">The instant, in any offset.</param>
    /// <returns>The instant as UTC, e.g. <c>2026-08-15T14:00:00Z</c>.</returns>
    public static string ToCanonical(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString(CanonicalFormat, CultureInfo.InvariantCulture);

    /// <summary>Whether a stamp is already in the canonical form.</summary>
    /// <param name="text">The stamp to test.</param>
    /// <returns><see langword="true" /> when it is exactly RFC 3339 UTC at second precision.</returns>
    public static bool IsCanonical(string? text) =>
        text is not null
        && DateTimeOffset.TryParseExact(
            text,
            CanonicalFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
}
