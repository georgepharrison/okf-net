using System.Diagnostics.CodeAnalysis;

namespace Okf.Core;

/// <summary>
/// The reserved <c>OKF####</c> number ranges (decisions.md Q2). The range a rule falls
/// in is fixed by its category and never reused.
/// </summary>
public enum OkfRuleCategory
{
    /// <summary>OKF00xx — OKF v0.2 §11 conformance. The only category that errors by default.</summary>
    Conformance = 0,

    /// <summary>OKF01xx — provenance: <c>sources</c>, citations, source recency (§5.1).</summary>
    Provenance,

    /// <summary>OKF02xx — trust and lifecycle: <c>generated</c>, <c>verified</c>, <c>stale_after</c> (§5.2–§5.5).</summary>
    Trust,

    /// <summary>OKF03xx — hygiene: everything that only degrades a bundle's usability.</summary>
    Hygiene,
}

/// <summary>
/// One lint rule: a stable <c>OKF####</c> identifier, the category range it belongs to,
/// and the severity it carries when nothing overrides it (PRD CLI-15).
/// </summary>
public sealed class OkfRule
{
    /// <summary>Initializes a rule.</summary>
    /// <param name="id">The stable <c>OKF####</c> identifier.</param>
    /// <param name="nickname">The kebab-case documentation nickname (never a configuration key).</param>
    /// <param name="category">The category whose number range the identifier falls in.</param>
    /// <param name="defaultSeverity">The severity applied when no configuration layer overrides it.</param>
    /// <param name="title">A one-line description of what the rule checks.</param>
    public OkfRule(string id, string nickname, OkfRuleCategory category, OkfSeverity defaultSeverity, string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(nickname);
        ArgumentException.ThrowIfNullOrEmpty(title);
        Id = id;
        Nickname = nickname;
        Category = category;
        DefaultSeverity = defaultSeverity;
        Title = title;
    }

    /// <summary>The stable identifier, e.g. <c>OKF0002</c>. This is the configuration key.</summary>
    public string Id { get; }

    /// <summary>
    /// The kebab-case nickname used in prose and documentation, e.g.
    /// <c>missing-type</c>. Deliberately not accepted as a configuration key
    /// (decisions.md Q2).
    /// </summary>
    public string Nickname { get; }

    /// <summary>The category whose reserved number range the identifier falls in.</summary>
    public OkfRuleCategory Category { get; }

    /// <summary>The severity applied when no configuration layer overrides the rule.</summary>
    public OkfSeverity DefaultSeverity { get; }

    /// <summary>A one-line description of what the rule checks.</summary>
    public string Title { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Id} ({Nickname})";
}

/// <summary>
/// The catalog of lint rules okf-net ships. Only OKF v0.2 §11 conformance rules default
/// to <see cref="OkfSeverity.Error" />: defaults block only what the spec says, and
/// every additional block is consumer configuration (decisions.md §7).
/// </summary>
public static class OkfRules
{
    /// <summary>A non-reserved <c>.md</c> file has no parseable frontmatter block (§11.1).</summary>
    public const string UnparseableFrontmatter = "OKF0001";

    /// <summary>A concept's frontmatter has no non-empty <c>type</c> (§11.2).</summary>
    public const string MissingType = "OKF0002";

    /// <summary>An <c>index.md</c> does not follow the §8 structure (§11.3).</summary>
    public const string InvalidIndexStructure = "OKF0003";

    /// <summary>A <c>log.md</c> does not follow the §9 structure (§11.3).</summary>
    public const string InvalidLogStructure = "OKF0004";

    /// <summary>A body footnote label matches no <c>sources[].id</c> (§5.1).</summary>
    public const string UncitedFootnote = "OKF0101";

    /// <summary>A <c>sources[].id</c> is never cited by a body footnote (§5.1).</summary>
    public const string UnusedSourceId = "OKF0102";

    /// <summary>A <c>sources[].last_modified</c> is newer than <c>generated.at</c> (PRD CORE-8).</summary>
    public const string SourceDrift = "OKF0103";

    /// <summary>A <c>verified[].by</c> equals <c>generated.by</c> (decisions.md §7).</summary>
    public const string SelfVerification = "OKF0201";

    /// <summary>The concept is stale: <c>today &gt;= stale_after</c> (§5.5).</summary>
    public const string StaleConcept = "OKF0202";

    /// <summary>The concept has no <c>description</c> (§4.1).</summary>
    public const string MissingDescription = "OKF0301";

    /// <summary>A bundle-internal markdown link resolves to no file (§6.1).</summary>
    public const string BrokenInternalLink = "OKF0302";

    /// <summary>Two concepts in a bundle are near-duplicates by title or filename.</summary>
    public const string NearDuplicateConcept = "OKF0303";

    /// <summary>The concept has no <c>tags</c> (§4.1).</summary>
    public const string MissingTags = "OKF0304";

    /// <summary>A tag is absent from the bundle's configured tag registry (beyond-spec extension).</summary>
    public const string UnregisteredTag = "OKF0305";

    /// <summary>
    /// A generated <c>index.md</c> no longer matches what <c>okf index</c> would emit for
    /// the tree (PRD CLI-9's generated-drift rule).
    /// </summary>
    public const string GeneratedIndexDrift = "OKF0306";

    /// <summary>A <c>sources[]</c> entry carries no <c>resource</c> (§5.1 requires one).</summary>
    public const string MissingSourceResource = "OKF0307";

    /// <summary>A <c>sources[].resource</c> path names nothing inside the bundle (§6.2).</summary>
    public const string UnresolvableSourceResource = "OKF0308";

    /// <summary>A markdown link resolves outside the bundle root (§6.2).</summary>
    public const string LinkLeavesBundle = "OKF0309";

    private static readonly OkfRule[] Catalog =
    [
        new(
            UnparseableFrontmatter,
            "unparseable-frontmatter",
            OkfRuleCategory.Conformance,
            OkfSeverity.Error,
            "A non-reserved .md file must contain a parseable YAML frontmatter block (§11.1)."),
        new(
            MissingType,
            "missing-type",
            OkfRuleCategory.Conformance,
            OkfSeverity.Error,
            "Every concept's frontmatter must contain a non-empty type (§11.2)."),
        new(
            InvalidIndexStructure,
            "invalid-index-structure",
            OkfRuleCategory.Conformance,
            OkfSeverity.Error,
            "An index.md must follow the §8 structure (§11.3)."),
        new(
            InvalidLogStructure,
            "invalid-log-structure",
            OkfRuleCategory.Conformance,
            OkfSeverity.Error,
            "A log.md must follow the §9 structure (§11.3)."),
        new(
            UncitedFootnote,
            "uncited-footnote",
            OkfRuleCategory.Provenance,
            OkfSeverity.Warning,
            "A body footnote label should join to a sources[].id (§5.1)."),
        new(
            UnusedSourceId,
            "unused-source-id",
            OkfRuleCategory.Provenance,
            OkfSeverity.Warning,
            "A sources[].id should be cited by at least one body footnote (§5.1)."),
        new(
            SourceDrift,
            "source-drift",
            OkfRuleCategory.Provenance,
            OkfSeverity.Warning,
            "A source changed after the concept was generated (sources[].last_modified > generated.at)."),
        new(
            SelfVerification,
            "self-verification",
            OkfRuleCategory.Trust,
            OkfSeverity.Warning,
            "A concept's generating actor must not also be one of its verifiers (decisions.md §7)."),
        new(
            StaleConcept,
            "stale-concept",
            OkfRuleCategory.Trust,
            OkfSeverity.Warning,
            "The concept has reached its stale_after date (§5.5)."),
        new(
            MissingDescription,
            "missing-description",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Warning,
            "A concept should carry a description; index entries degrade without one (§4.1)."),
        new(
            BrokenInternalLink,
            "broken-internal-link",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Info,
            "A bundle-internal markdown link resolves to no file (§6.1: consumers must tolerate)."),
        new(
            NearDuplicateConcept,
            "near-duplicate-concept",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Warning,
            "Two concepts in the bundle carry the same title or filename up to case and punctuation."),
        new(
            MissingTags,
            "missing-tags",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Hidden,
            "A concept carries no tags (opt-in: raise the severity to enable)."),
        new(
            UnregisteredTag,
            "unregistered-tag",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Hidden,
            "A tag is absent from the configured tag registry (opt-in: configure lint.tagRegistry)."),
        new(
            GeneratedIndexDrift,
            "generated-index-drift",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Warning,
            "A generated index.md differs from what `okf index` would emit (run `okf index`)."),
        new(
            MissingSourceResource,
            "missing-source-resource",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Warning,
            "A sources[] entry carries no resource; §5.1 requires one within an entry."),
        new(
            UnresolvableSourceResource,
            "unresolvable-source-resource",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Info,
            "A sources[].resource written as a path names nothing inside the bundle (§6.2)."),
        new(
            LinkLeavesBundle,
            "link-leaves-bundle",
            OkfRuleCategory.Hygiene,
            OkfSeverity.Info,
            "A markdown link resolves outside the bundle root; a bundle should be complete on its own (§6.2)."),
    ];

    private static readonly Dictionary<string, OkfRule> ById =
        Catalog.ToDictionary(rule => rule.Id, StringComparer.Ordinal);

    /// <summary>Every rule okf-net ships, in identifier order.</summary>
    public static IReadOnlyList<OkfRule> All => Catalog;

    /// <summary>Whether an identifier names a shipped rule.</summary>
    /// <param name="id">The identifier to test, e.g. <c>OKF0302</c>.</param>
    /// <returns><see langword="true" /> when the rule exists.</returns>
    public static bool IsKnown(string id) => id is not null && ById.ContainsKey(id);

    /// <summary>Looks a rule up by identifier.</summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="rule">The rule when found.</param>
    /// <returns><see langword="true" /> when the rule exists.</returns>
    public static bool TryGet(string id, [NotNullWhen(true)] out OkfRule? rule)
    {
        if (id is null)
        {
            rule = null;
            return false;
        }

        return ById.TryGetValue(id, out rule);
    }

    /// <summary>Looks a rule up by identifier.</summary>
    /// <param name="id">The identifier to look up.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentException">No such rule is shipped.</exception>
    public static OkfRule Get(string id) =>
        TryGet(id, out var rule) ? rule : throw new ArgumentException($"Unknown diagnostic id '{id}'.", nameof(id));
}
