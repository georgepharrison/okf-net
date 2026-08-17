using System.Globalization;

namespace Okf.Core.Upgrade;

/// <summary>
/// Semantic-version precedence, as semver 2.0.0 §11 defines it — the one question
/// <c>okf upgrade</c> has to answer before it downloads anything.
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from a package, for AD-9's reason and one more: the only
/// versions compared here are the ones this repository produces (a bare <c>X.Y.Z</c> from a
/// stable tag, <c>X.Y.Z-rc.N</c> from a prerelease tag, <c>0.0.0-dev+&lt;sha&gt;</c> from an
/// unstamped build — AD-41), so the input space is small and entirely ours.
/// <see cref="Version" /> cannot do it: it has no notion of a prerelease, so it reads
/// <c>1.0.0-rc.1</c> as unparseable and would make a release candidate compare equal to the
/// release it precedes.
/// </remarks>
public static class OkfUpgradeVersion
{
    /// <summary>
    /// Compares two versions by semver precedence.
    /// </summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns>Negative, zero or positive as <paramref name="left" /> precedes, equals or follows <paramref name="right" />.</returns>
    public static int Compare(string? left, string? right)
    {
        var (leftCore, leftPre) = Split(left);
        var (rightCore, rightPre) = Split(right);

        for (var index = 0; index < 3; index++)
        {
            var order = Number(leftCore, index).CompareTo(Number(rightCore, index));
            if (order != 0)
            {
                return order;
            }
        }

        // §11.3: a version WITH a prerelease has lower precedence than the same core
        // without one. This is the whole reason the comparison exists — 1.1.0-rc.1 must not
        // read as an upgrade over 1.1.0.
        if (leftPre is null && rightPre is null)
        {
            return 0;
        }

        if (leftPre is null)
        {
            return 1;
        }

        if (rightPre is null)
        {
            return -1;
        }

        return ComparePrerelease(leftPre, rightPre);
    }

    /// <summary>
    /// Whether a version is an unstamped local build — <c>0.0.0-dev</c> and anything else
    /// on the <c>0.0.0</c> core (AD-41).
    /// </summary>
    /// <param name="version">The version to judge.</param>
    /// <returns><see langword="true" /> when nothing stamped this build.</returns>
    /// <remarks>
    /// Such a build is always upgradable and says so, rather than being compared: it was
    /// built from a working tree that may hold anything, so "is 0.0.0-dev older than
    /// 1.1.0-rc.1" is a question about version strings and not about code.
    /// </remarks>
    public static bool IsDevelopmentBuild(string? version)
    {
        var (core, _) = Split(version);
        return Number(core, 0) == 0 && Number(core, 1) == 0 && Number(core, 2) == 0;
    }

    /// <summary>Whether <paramref name="available" /> is worth downloading over <paramref name="current" />.</summary>
    /// <param name="current">The running binary's version.</param>
    /// <param name="available">The version the release manifest names.</param>
    /// <returns><see langword="true" /> when an upgrade is available.</returns>
    public static bool IsUpgradeAvailable(string? current, string? available) =>
        available is { Length: > 0 }
        && (IsDevelopmentBuild(current) || Compare(available, current) > 0);

    /// <summary>Splits a version into its numeric core and its prerelease, dropping build metadata.</summary>
    private static (string Core, string? Prerelease) Split(string? version)
    {
        var text = (version ?? string.Empty).Trim();
        if (text.StartsWith('v'))
        {
            text = text[1..];
        }

        // §10: build metadata is ignored in precedence, so it is dropped before anything
        // else looks at the string. `1.0.0+abc` and `1.0.0` are the same release.
        var metadata = text.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            text = text[..metadata];
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? (text, null) : (text[..dash], text[(dash + 1)..]);
    }

    /// <summary>Reads one dot-separated component of the numeric core; a missing or unparseable one is zero.</summary>
    private static long Number(string core, int index)
    {
        var parts = core.Split('.');
        if (index >= parts.Length)
        {
            return 0;
        }

        return long.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    /// <summary>Compares two prerelease strings identifier by identifier (§11.4).</summary>
    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');

        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var leftNumeric = IsNumeric(leftParts[index]);
            var rightNumeric = IsNumeric(rightParts[index]);

            if (leftNumeric && rightNumeric)
            {
                // Numerically, not lexically: rc.10 follows rc.9, and a string compare
                // would put it before.
                var order = long.Parse(leftParts[index], CultureInfo.InvariantCulture)
                    .CompareTo(long.Parse(rightParts[index], CultureInfo.InvariantCulture));
                if (order != 0)
                {
                    return order;
                }

                continue;
            }

            // §11.4.3: numeric identifiers always have lower precedence than alphanumeric.
            if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }

            var text = string.CompareOrdinal(leftParts[index], rightParts[index]);
            if (text != 0)
            {
                return Math.Sign(text);
            }
        }

        // §11.4.4: all preceding identifiers equal, so more fields wins — `rc.1.1` follows
        // `rc.1`.
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static bool IsNumeric(string identifier) =>
        identifier.Length > 0 && identifier.All(char.IsAsciiDigit)
        && long.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
