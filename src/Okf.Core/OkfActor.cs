namespace Okf.Core;

/// <summary>
/// The OKF v0.2 §7 actor convention, which every <c>generated.by</c> and
/// <c>verified[].by</c> value follows: <c>&lt;producer&gt;/&lt;version&gt;</c> for an
/// agent or tool, <c>human:&lt;id&gt;</c> for a person, <c>process:&lt;id&gt;</c> for an
/// automated process.
/// </summary>
/// <remarks>
/// The prefix is the whole trust signal: §5.3 derives <c>human-reviewed</c> from nothing
/// but a <c>human:</c> prefix on a verification event, so what may carry that prefix is a
/// judgement okf-net has to make in exactly one place.
/// </remarks>
public static class OkfActor
{
    /// <summary>The prefix marking a person (§7).</summary>
    public const string HumanPrefix = OkfDocument.HumanActorPrefix;

    /// <summary>The prefix marking an automated process (§7).</summary>
    public const string ProcessPrefix = "process:";

    /// <summary>Whether an actor names a person.</summary>
    /// <param name="actor">The actor value, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the value carries the <c>human:</c> prefix.</returns>
    public static bool IsHuman(string? actor) =>
        actor is not null && actor.StartsWith(HumanPrefix, StringComparison.Ordinal);

    /// <summary>Whether an actor is written in one of §7's three forms.</summary>
    /// <param name="actor">The actor value, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the value is a well-formed actor.</returns>
    public static bool IsValid(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Trim().Length != actor.Length)
        {
            return false;
        }

        // An actor is written into a double-quoted YAML scalar, so a quote, a backslash, or
        // a control character in one would not be an odd name — it would be a way to write
        // arbitrary frontmatter through the `--by` flag.
        if (actor.Any(character => character is '"' or '\\' || char.IsControl(character)))
        {
            return false;
        }

        if (actor.StartsWith(HumanPrefix, StringComparison.Ordinal))
        {
            return actor.Length > HumanPrefix.Length;
        }

        if (actor.StartsWith(ProcessPrefix, StringComparison.Ordinal))
        {
            return actor.Length > ProcessPrefix.Length;
        }

        // `<producer>/<version>`: a colon anywhere else would be a mistyped prefix
        // (`human/ringo`, `Human:ringo`), and letting it through as a producer name is how
        // a person silently stops counting as one.
        var separator = actor.IndexOf('/', StringComparison.Ordinal);
        return separator > 0
            && separator < actor.Length - 1
            && !actor.Contains(':', StringComparison.Ordinal);
    }

    /// <summary>
    /// Renders a configured identity as a <c>human:</c> actor, leaving an already-prefixed
    /// value alone so <c>verify.actor</c> may be written either way.
    /// </summary>
    /// <param name="id">The identity, with or without the prefix.</param>
    /// <returns>The <c>human:</c> actor.</returns>
    public static string ToHuman(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var trimmed = id.Trim();
        return IsHuman(trimmed) ? trimmed : HumanPrefix + trimmed;
    }
}
