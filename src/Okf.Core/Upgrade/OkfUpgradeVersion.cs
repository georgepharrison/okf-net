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
        VersionParts leftVersion = VersionParts.Parse(left);
        VersionParts rightVersion = VersionParts.Parse(right);

        int coreOrder = CompareCore(leftVersion.Core, rightVersion.Core);
        return coreOrder != 0
            ? coreOrder
            : ComparePrereleasePresence(leftVersion.Prerelease, rightVersion.Prerelease);
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
        string core = VersionParts.Parse(version).Core;
        return Number(core, 0) == 0 && Number(core, 1) == 0 && Number(core, 2) == 0;
    }

    /// <summary>Whether <paramref name="available" /> is worth downloading over <paramref name="current" />.</summary>
    /// <param name="current">The running binary's version.</param>
    /// <param name="available">The version the release manifest names.</param>
    /// <returns><see langword="true" /> when an upgrade is available.</returns>
    public static bool IsUpgradeAvailable(string? current, string? available) =>
        available is { Length: > 0 }
        && (IsDevelopmentBuild(current) || Compare(available, current) > 0);

    /// <summary>Compares the three numeric core components.</summary>
    private static int CompareCore(string leftCore, string rightCore)
    {
        for (int index = 0; index < 3; index++)
        {
            int order = Number(leftCore, index).CompareTo(Number(rightCore, index));
            if (order != 0)
            {
                return order;
            }
        }

        return 0;
    }

    private static int ComparePrereleasePresence(string? leftPre, string? rightPre)
    {
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

    /// <summary>Splits a version into its numeric core and its prerelease, dropping build metadata.</summary>
    private static VersionParts Split(string? version)
    {
        string text = (version ?? string.Empty).Trim();
        if (text.StartsWith('v'))
        {
            text = text[1..];
        }

        // §10: build metadata is ignored in precedence, so it is dropped before anything
        // else looks at the string. `1.0.0+abc` and `1.0.0` are the same release.
        int metadata = text.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            text = text[..metadata];
        }

        int dash = text.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? new VersionParts(text, null) : new VersionParts(text[..dash], text[(dash + 1)..]);
    }

    /// <summary>Reads one dot-separated component of the numeric core; a missing or unparseable one is zero.</summary>
    private static long Number(string core, int index)
    {
        string[] parts = core.Split('.');
        if (index >= parts.Length)
        {
            return 0;
        }

        return long.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0;
    }

    /// <summary>Compares two prerelease strings identifier by identifier (§11.4).</summary>
    private static int ComparePrerelease(string left, string right)
    {
        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');

        for (int index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            int order = ComparePrereleaseIdentifier(leftParts[index], rightParts[index]);
            if (order != 0)
            {
                return order;
            }
        }

        // §11.4.4: all preceding identifiers equal, so more fields wins — `rc.1.1` follows
        // `rc.1`.
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        bool leftNumeric = IsNumeric(left);
        bool rightNumeric = IsNumeric(right);

        if (leftNumeric && rightNumeric)
        {
            return CompareNumericIdentifiers(left, right);
        }

        // §11.4.3: numeric identifiers always have lower precedence than alphanumeric.
        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        int text = string.CompareOrdinal(left, right);
        return text == 0 ? 0 : Math.Sign(text);
    }

    private static int CompareNumericIdentifiers(string left, string right)
    {
        // Numerically, not lexically: rc.10 follows rc.9, and a string compare would put it
        // before.
        return long.Parse(left, CultureInfo.InvariantCulture)
            .CompareTo(long.Parse(right, CultureInfo.InvariantCulture));
    }

    private static bool IsNumeric(string identifier) =>
        identifier.Length > 0 && identifier.All(char.IsAsciiDigit)
        && long.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private readonly record struct VersionParts(string Core, string? Prerelease)
    {
        public static VersionParts Parse(string? version) => Split(version);
    }
}
