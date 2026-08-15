using System.Globalization;

namespace Okf.Core;

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
public static class OkfTimestamp
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

/// <summary>
/// An OKF lifecycle timestamp — <c>generated.at</c>, <c>verified[].at</c>,
/// <c>sources[].last_modified</c>, <c>stale_after</c> — parsed once and compared at the
/// precision it was actually written with.
/// </summary>
/// <remarks>
/// <para>The spec lets the same field be a bare date (<c>2026-08-14</c>) or a full
/// instant (<c>2026-08-14T20:41:50-05:00</c>), often on the two sides of one comparison:
/// a source records the day it changed, a generator records the second it ran. Coercing
/// a bare date to midnight and comparing instants would then answer "the source did not
/// change after the concept was written" for a source that changed later the same
/// day — a false negative on exactly the drift signal decisions.md §5 calls the
/// compensating control for cited-live sources.</para>
/// <para>So precision is carried, not discarded: two instants compare as instants, and
/// anything else compares by date. That makes every comparison here at least as
/// sensitive as <c>OkfLinter</c>'s date-only test (<c>OKF0203</c>), never less.</para>
/// </remarks>
public readonly struct OkfTimestamp
{
    private OkfTimestamp(DateOnly date, DateTimeOffset instant, bool hasTime)
    {
        Date = date;
        Instant = instant;
        HasTime = hasTime;
    }

    /// <summary>
    /// The calendar date exactly as written — the value's first ten characters, not its
    /// UTC date. An instant written <c>2026-08-14T20:41:50-05:00</c> is the 14th to its
    /// author and the 15th in UTC, and the author's day is the one the document means.
    /// </summary>
    public DateOnly Date { get; }

    /// <summary>
    /// The value as an instant. For a bare date this is midnight UTC, which is only ever
    /// read when <see cref="HasTime" /> is <see langword="true" /> on both sides of a
    /// comparison.
    /// </summary>
    public DateTimeOffset Instant { get; }

    /// <summary>Whether the value carried a time of day, and therefore compares as an instant.</summary>
    public bool HasTime { get; }

    /// <summary>Parses a timestamp value.</summary>
    /// <param name="text">The frontmatter text, or <see langword="null" />.</param>
    /// <param name="timestamp">The parsed timestamp.</param>
    /// <returns>
    /// <see langword="true" /> when the value opens with an ISO <c>YYYY-MM-DD</c> date.
    /// A trailing time that does not parse is dropped rather than failing the whole
    /// value, so a malformed time degrades to date precision instead of to silence.
    /// </returns>
    public static bool TryParse(string? text, out OkfTimestamp timestamp)
    {
        timestamp = default;

        var trimmed = text?.Trim();
        if (trimmed is null || trimmed.Length < 10)
        {
            return false;
        }

        if (!DateOnly.TryParseExact(
                trimmed[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return false;
        }

        // A value with no offset is read as UTC rather than as this machine's local time:
        // okf output must not depend on the reader's timezone (PRD ACC-7).
        if (trimmed.Length > 10
            && DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant))
        {
            timestamp = new OkfTimestamp(date, instant, hasTime: true);
            return true;
        }

        timestamp = new OkfTimestamp(date, Midnight(date), hasTime: false);
        return true;
    }

    /// <summary>Parses a timestamp value, yielding <see langword="null" /> when it is not one.</summary>
    /// <param name="text">The frontmatter text, or <see langword="null" />.</param>
    /// <returns>The parsed timestamp, or <see langword="null" />.</returns>
    public static OkfTimestamp? Parse(string? text) =>
        TryParse(text, out var timestamp) ? timestamp : null;

    /// <summary>
    /// Orders two timestamps at the coarser of their two precisions (see the type's
    /// remarks).
    /// </summary>
    /// <param name="left">The left timestamp.</param>
    /// <param name="right">The right timestamp.</param>
    /// <returns>Negative, zero, or positive as <paramref name="left" /> sorts before, with, or after <paramref name="right" />.</returns>
    public static int Compare(OkfTimestamp left, OkfTimestamp right) =>
        left.HasTime && right.HasTime
            ? left.Instant.CompareTo(right.Instant)
            : left.Date.CompareTo(right.Date);

    /// <summary>Whether <paramref name="left" /> is strictly later than <paramref name="right" />.</summary>
    /// <param name="left">The timestamp under test.</param>
    /// <param name="right">The timestamp to beat.</param>
    /// <returns><see langword="true" /> when strictly later. Equal timestamps are not later.</returns>
    public static bool IsAfter(OkfTimestamp left, OkfTimestamp right) => Compare(left, right) > 0;

    /// <summary>Whether <paramref name="left" /> is strictly later than <paramref name="right" />.</summary>
    /// <param name="left">The timestamp under test, or <see langword="null" /> when there is none.</param>
    /// <param name="right">The timestamp to beat, or <see langword="null" /> when there is none.</param>
    /// <returns><see langword="true" /> when both are present and the first is strictly later.</returns>
    public static bool IsAfter(OkfTimestamp? left, OkfTimestamp? right) =>
        left is { } a && right is { } b && IsAfter(a, b);

    /// <summary>Whole days from this timestamp's date to a later date.</summary>
    /// <param name="today">The date to measure to.</param>
    /// <returns>The day count; negative when the timestamp is in the future.</returns>
    public int DaysUntil(DateOnly today) => today.DayNumber - Date.DayNumber;

    private static DateTimeOffset Midnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
