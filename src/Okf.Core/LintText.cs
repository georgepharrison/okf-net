using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Okf.Core;

/// <summary>Where a path-valued target lands relative to the bundle it was written in.</summary>
internal enum LinkTarget
{
    /// <summary>
    /// Not a path at all: an absolute URL, another URI scheme, a protocol-relative
    /// reference, an in-page anchor, or an empty target. Nothing on disk to check.
    /// </summary>
    NotAPath = 0,

    /// <summary>A path that resolves inside the bundle root.</summary>
    Inside,

    /// <summary>A path that resolves, but outside the bundle root (§6.2).</summary>
    Outside,
}

/// <summary>How a <c>sources[].resource</c> value reads (§5.1, §6.2).</summary>
internal enum SourceResource
{
    /// <summary>A URL or a §5.1 population/scope descriptor: nothing on disk to check.</summary>
    NotAPath = 0,

    /// <summary>A path that names a file or directory inside the bundle.</summary>
    Resolved,

    /// <summary>A path that names nothing inside the bundle.</summary>
    Unresolved,
}

/// <summary>
/// Text-shaped helpers shared by the lint rules: the §8 index-entry form and the §6.1/§6.2
/// link resolution both rules and, later, index generation need.
/// </summary>
internal static partial class LintText
{
    /// <summary>
    /// Whether a bullet's text is an index entry — <c>[Title](link)</c> with an optional
    /// <c> - description</c> tail (§8).
    /// </summary>
    /// <param name="text">The bullet text, without its list marker.</param>
    /// <returns><see langword="true" /> when the entry is well formed.</returns>
    public static bool IsIndexEntry(string text) => IndexEntryRegex().IsMatch(text.Trim());

    /// <summary>
    /// Resolves a markdown link to a filesystem path inside the bundle (§6.1, §6.2).
    /// Absolute URLs, other URI schemes, in-page anchors, and paths that escape the bundle
    /// root are not bundle-internal and resolve to nothing, so the broken-link rule stays
    /// silent on them.
    /// </summary>
    /// <param name="target">The link destination as written.</param>
    /// <param name="bundleRoot">The bundle root, which <c>/</c>-rooted links are relative to.</param>
    /// <param name="documentDirectory">The linking document's directory, which relative links are relative to.</param>
    /// <param name="resolved">The absolute path the link points at.</param>
    /// <returns><see langword="true" /> when the link is bundle-internal and worth checking.</returns>
    public static bool TryResolveLink(
        string target,
        string bundleRoot,
        string documentDirectory,
        [NotNullWhen(true)] out string? resolved)
    {
        var landed = Resolve(target, bundleRoot, documentDirectory, out var candidate);
        resolved = landed == LinkTarget.Inside ? candidate : null;
        return resolved is not null;
    }

    /// <summary>
    /// Resolves a path-valued target and says where it landed (§6.1, §6.2). Unlike
    /// <see cref="TryResolveLink" /> this distinguishes "not a path" from "a path that
    /// leaves the bundle", which is the difference between saying nothing about a link and
    /// reporting that it points out of the bundle.
    /// </summary>
    /// <param name="target">The destination as written.</param>
    /// <param name="bundleRoot">The bundle root, which <c>/</c>-rooted targets are relative to.</param>
    /// <param name="documentDirectory">The document's directory, which relative targets are relative to.</param>
    /// <param name="resolved">The absolute path, for both <see cref="LinkTarget.Inside" /> and <see cref="LinkTarget.Outside" />.</param>
    /// <returns>Where the target landed.</returns>
    public static LinkTarget Resolve(
        string target,
        string bundleRoot,
        string documentDirectory,
        out string? resolved)
    {
        resolved = null;

        var path = target.Trim();
        var fragment = path.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
        {
            path = path[..fragment];
        }

        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            path = path[..query];
        }

        if (path.Length == 0 || SchemeRegex().IsMatch(path) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return LinkTarget.NotAPath;
        }

        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return LinkTarget.NotAPath;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return LinkTarget.NotAPath;
        }

        var native = path.Replace('/', Path.DirectorySeparatorChar);
        string combined;
        try
        {
            combined = path.StartsWith('/')
                ? Path.GetFullPath(Path.Combine(bundleRoot, native.TrimStart(Path.DirectorySeparatorChar)))
                : Path.GetFullPath(Path.Combine(documentDirectory, native));
        }
        catch (ArgumentException)
        {
            return LinkTarget.NotAPath;
        }

        resolved = combined;

        var root = Path.TrimEndingDirectorySeparator(bundleRoot);
        return combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(combined, root, StringComparison.Ordinal)
            ? LinkTarget.Inside
            : LinkTarget.Outside;
    }

    /// <summary>
    /// Classifies a <c>sources[].resource</c> value (§5.1, §6.2). §5.1 allows the field to
    /// be a population or scope descriptor rather than a path — <c>all queries in BigQuery
    /// project X</c> — and §6.2 allows an absolute URL, so only values that unambiguously
    /// read as a filesystem path are ever checked against the tree. The bar for
    /// "unambiguously a path" is deliberately high (see
    /// <see cref="IsCheckablePath(string)" />): a false <see cref="SourceResource.Unresolved" />
    /// on a foreign bundle costs more than a missed typo.
    /// </summary>
    /// <param name="resource">The resource value as written.</param>
    /// <param name="bundleRoot">The bundle root the concept lives in.</param>
    /// <param name="documentDirectory">The concept's directory, which relative paths are relative to.</param>
    /// <returns>How the value reads, and whether it points at something that exists.</returns>
    public static SourceResource ClassifyResource(string resource, string bundleRoot, string documentDirectory)
    {
        var value = resource.Trim();
        if (!IsCheckablePath(value))
        {
            return SourceResource.NotAPath;
        }

        // A relative `sources[].resource` is resolved from the concept, as §6.2 says, and
        // then — only if that fails — from the bundle root. Producers write both: three of
        // Google's four reference bundles carry root-relative source paths with no leading
        // slash (`policies/margin-standard.md` cited from `metrics/`), and a provenance
        // pointer that names a real file in the bundle is doing its job whichever base the
        // author had in mind. A typo, or a path that leaves the bundle, still fails both.
        if (Exists(Resolve(value, bundleRoot, documentDirectory, out var relative), relative))
        {
            return SourceResource.Resolved;
        }

        return !value.StartsWith('/')
            && Exists(Resolve("/" + value, bundleRoot, documentDirectory, out var rooted), rooted)
            ? SourceResource.Resolved
            : SourceResource.Unresolved;
    }

    /// <summary>
    /// Whether a value reads as a filesystem path rather than as a URL or a §5.1 scope
    /// descriptor: no whitespace, no URI scheme, and either explicit path syntax
    /// (<c>/</c>, <c>./</c>, <c>../</c>), a <c>.md</c> suffix, or a directory segment
    /// followed by a name carrying an extension. A dotted identifier
    /// (<c>project.dataset.table</c>) and a bare descriptor path (<c>dashboards/exec-revenue</c>,
    /// which SPEC §5.1 itself uses) therefore both read as descriptors, not paths.
    /// </summary>
    /// <param name="value">The trimmed value.</param>
    /// <returns><see langword="true" /> when the value is worth resolving.</returns>
    private static bool IsCheckablePath(string value)
    {
        if (value.Length == 0
            || value.Any(char.IsWhiteSpace)
            || SchemeRegex().IsMatch(value)
            || value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.StartsWith('/')
            || value.StartsWith("./", StringComparison.Ordinal)
            || value.StartsWith("../", StringComparison.Ordinal)
            || value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var slash = value.LastIndexOf('/');
        return slash >= 0 && value.AsSpan(slash + 1).Contains('.');
    }

    private static bool Exists(LinkTarget target, string? resolved) =>
        target == LinkTarget.Inside && (File.Exists(resolved) || Directory.Exists(resolved));

    [GeneratedRegex(@"^\[(?<title>[^\]]*)\]\((?<dest>[^)]*)\)\s*(?:[-–—:]\s*(?<description>.*))?$")]
    private static partial Regex IndexEntryRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex SchemeRegex();
}
