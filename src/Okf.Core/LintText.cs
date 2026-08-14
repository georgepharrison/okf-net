using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Okf.Core;

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
            return false;
        }

        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
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
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(bundleRoot);
        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(combined, root, StringComparison.Ordinal))
        {
            return false;
        }

        resolved = combined;
        return true;
    }

    [GeneratedRegex(@"^\[(?<title>[^\]]*)\]\((?<dest>[^)]*)\)\s*(?:[-–—:]\s*(?<description>.*))?$")]
    private static partial Regex IndexEntryRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex SchemeRegex();
}
