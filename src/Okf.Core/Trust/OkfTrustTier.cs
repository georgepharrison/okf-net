namespace Okf.Core.Trust;

/// <summary>
/// A concept's trust tier, derived from <c>verified</c> (OKF v0.2 §5.3), lowest to
/// highest.
/// </summary>
public enum OkfTrustTier
{
    /// <summary>No <c>verified</c> events.</summary>
    Unverified = 0,

    /// <summary>Verified by non-<c>human:</c> actors only.</summary>
    MachineConfirmed,

    /// <summary>Verified by at least one <c>human:&lt;id&gt;</c> actor.</summary>
    HumanReviewed,
}

/// <summary>Conversions for <see cref="OkfTrustTier" />.</summary>
public static class OkfTrustTierExtensions
{
    /// <summary>
    /// Renders the tier using the spec's wording (<c>unverified</c>,
    /// <c>machine-confirmed</c>, <c>human-reviewed</c>) — the strings the reference
    /// implementation returns and the ones the CLI, MCP server, and docs must use.
    /// </summary>
    /// <param name="tier">The tier to render.</param>
    /// <returns>The spec's name for the tier.</returns>
    public static string ToSpecString(this OkfTrustTier tier) => tier switch
    {
        OkfTrustTier.Unverified => "unverified",
        OkfTrustTier.MachineConfirmed => "machine-confirmed",
        OkfTrustTier.HumanReviewed => "human-reviewed",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
