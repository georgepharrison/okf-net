using System.Globalization;

namespace Okf.Core;

/// <summary>
/// A moment a concept's frontmatter records — <c>generated.at</c>, <c>verified[].at</c>,
/// <c>sources[].last_modified</c>, <c>stale_after</c> — parsed once and compared at the
/// precision it was actually written with.
/// </summary>
/// <remarks>
/// <para>The read half of the pair. <see cref="OkfTimestamp" /> is the canonical form
/// okf-net <em>writes</em>, one spelling by choice; this type reads what is already on
/// disk, in whichever spelling the format allowed, which is why it is not named for a
/// form. A value here may carry no time at all.</para>
/// <para>The spec lets the same field be a bare date (<c>2026-08-14</c>) or a full
/// instant (<c>2026-08-14T20:41:50-05:00</c>), often on the two sides of one comparison:
/// a source records the day it changed, a generator records the second it ran. So
/// precision is carried rather than discarded — two instants compare as instants, and
/// anything coarser compares by date. Two writes on the same day are therefore ordered
/// when both recorded a time, which the linter's <c>text[..10]</c> truncation cannot see;
/// a bare date against a same-day instant reads as simultaneous rather than as drift,
/// because a bare date says only "that day" and picking a time for it would be picking
/// the answer.</para>
/// <para>The date compared is the one <em>written</em>, not the UTC one, and that is why
/// the two comparisons can disagree: <c>2026-08-15T01:00:00+05:00</c> is a later written
/// date than <c>2026-08-14T23:00:00Z</c> and an earlier instant. Ordering follows the
/// instants, because <see cref="Compare" /> has to be a strict order — something picks a
/// maximum with it. A test that must not be less sensitive than
/// <c>OkfLinter</c>'s date-only one (<c>OKF0203</c>) takes the union instead: see
/// <see cref="IsAfterAtEitherPrecision" />.</para>
/// </remarks>
public readonly struct OkfLifecycleInstant
{
    private OkfLifecycleInstant(DateOnly date, DateTimeOffset instant, bool hasTime)
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
    public static bool TryParse(string? text, out OkfLifecycleInstant timestamp)
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
            timestamp = new OkfLifecycleInstant(date, instant, hasTime: true);
            return true;
        }

        timestamp = new OkfLifecycleInstant(date, Midnight(date), hasTime: false);
        return true;
    }

    /// <summary>Parses a timestamp value, yielding <see langword="null" /> when it is not one.</summary>
    /// <param name="text">The frontmatter text, or <see langword="null" />.</param>
    /// <returns>The parsed timestamp, or <see langword="null" />.</returns>
    public static OkfLifecycleInstant? Parse(string? text) =>
        TryParse(text, out var timestamp) ? timestamp : null;

    /// <summary>
    /// Orders two timestamps at the coarser of their two precisions (see the type's
    /// remarks).
    /// </summary>
    /// <param name="left">The left timestamp.</param>
    /// <param name="right">The right timestamp.</param>
    /// <returns>Negative, zero, or positive as <paramref name="left" /> sorts before, with, or after <paramref name="right" />.</returns>
    public static int Compare(OkfLifecycleInstant left, OkfLifecycleInstant right) =>
        left.HasTime && right.HasTime
            ? left.Instant.CompareTo(right.Instant)
            : left.Date.CompareTo(right.Date);

    /// <summary>Whether <paramref name="left" /> is strictly later than <paramref name="right" />.</summary>
    /// <param name="left">The timestamp under test.</param>
    /// <param name="right">The timestamp to beat.</param>
    /// <returns><see langword="true" /> when strictly later. Equal timestamps are not later.</returns>
    public static bool IsAfter(OkfLifecycleInstant left, OkfLifecycleInstant right) => Compare(left, right) > 0;

    /// <summary>Whether <paramref name="left" /> is strictly later than <paramref name="right" />.</summary>
    /// <param name="left">The timestamp under test, or <see langword="null" /> when there is none.</param>
    /// <param name="right">The timestamp to beat, or <see langword="null" /> when there is none.</param>
    /// <returns><see langword="true" /> when both are present and the first is strictly later.</returns>
    public static bool IsAfter(OkfLifecycleInstant? left, OkfLifecycleInstant? right) =>
        left is { } a && right is { } b && IsAfter(a, b);

    /// <summary>
    /// Whether <paramref name="left" /> is later than <paramref name="right" /> at
    /// <em>either</em> precision: the instants say so, or the dates as written do.
    /// </summary>
    /// <param name="left">The timestamp under test.</param>
    /// <param name="right">The timestamp to beat.</param>
    /// <returns><see langword="true" /> when either comparison makes it later.</returns>
    /// <remarks>
    /// For a signal that must never be quieter than the linter's — source drift against
    /// <c>OKF0203</c>, which compares the written dates and nothing else. Where the two
    /// sides carry different UTC offsets the date and the instant can disagree, and
    /// <see cref="IsAfter(OkfLifecycleInstant, OkfLifecycleInstant)" /> alone would then hide a row the
    /// linter reports on the same file. Equal stays equal: this is a wider "after", not a
    /// weaker one.
    /// </remarks>
    public static bool IsAfterAtEitherPrecision(OkfLifecycleInstant left, OkfLifecycleInstant right) =>
        IsAfter(left, right) || left.Date > right.Date;

    /// <summary>Whole days from this timestamp's date to a later date.</summary>
    /// <param name="today">The date to measure to.</param>
    /// <returns>The day count; negative when the timestamp is in the future.</returns>
    public int DaysUntil(DateOnly today) => today.DayNumber - Date.DayNumber;

    private static DateTimeOffset Midnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
