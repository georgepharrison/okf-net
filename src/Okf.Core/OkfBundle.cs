using System.Diagnostics.CodeAnalysis;

namespace Okf.Core;

/// <summary>
/// A bundle root: the directory tree of markdown files a command operates on (spec §3).
/// Nothing about the directory marks it as a bundle — any directory a consumer points at
/// is one, which is what makes foreign bundles work (PRD CORE-13).
/// </summary>
public sealed class OkfBundle
{
    /// <summary>The reserved filename for a directory listing (spec §3.1, §8).</summary>
    public const string IndexFileName = "index.md";

    /// <summary>The reserved filename for an update history (spec §3.1, §9).</summary>
    public const string LogFileName = "log.md";

    /// <summary>
    /// The filename that carries a subdirectory's own description, used as the blurb on
    /// the entry that links that subdirectory from its parent's <c>index.md</c>
    /// (decisions.md Q1). It is an ordinary concept — not a spec §3.1 reserved name — so
    /// it is validated and listed like any other.
    /// </summary>
    public const string AboutFileName = "about.md";

    private static readonly string[] Conventional = [AboutFileName];

    /// <summary>Initializes a bundle.</summary>
    /// <param name="root">The bundle root directory.</param>
    public OkfBundle(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Name = Path.GetFileName(Root);
    }

    /// <summary>The absolute, normalized bundle root directory.</summary>
    public string Root { get; }

    /// <summary>The bundle's directory name.</summary>
    public string Name { get; }

    /// <summary>Whether a filename is one of the spec's reserved names (§3.1).</summary>
    /// <param name="path">A path or filename.</param>
    /// <returns><see langword="true" /> for <c>index.md</c> and <c>log.md</c>.</returns>
    public static bool IsReservedFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path);
        return string.Equals(name, IndexFileName, StringComparison.Ordinal)
            || string.Equals(name, LogFileName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a filename is a convention-bearing concept name — one whose name is fixed
    /// by okf-net's own conventions rather than chosen to describe the content, and which
    /// is therefore expected to repeat once per subdirectory. Currently just
    /// <see cref="AboutFileName" /> (decisions.md Q1).
    /// </summary>
    /// <param name="path">A path or filename.</param>
    /// <returns><see langword="true" /> when the name is convention-bearing.</returns>
    /// <remarks>
    /// Near-duplicate detection (<c>OKF0303</c>) skips these: a bundle with an
    /// <c>about.md</c> in every subdirectory is following the convention, not repeating
    /// itself, and Q1 makes those files the designated carriers of subdirectory
    /// descriptions. Their identity is their directory, not their name.
    /// </remarks>
    public static bool IsConventionalFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileName(path);
        return Conventional.Contains(name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Every <c>.md</c> file in the tree, in a deterministic order (ordinal by
    /// bundle-relative path). Dot-directories are skipped: <c>.git</c> and friends are
    /// not bundle content. Symlinked subdirectories are skipped too — following them
    /// either walks out of the bundle root, which nothing may do, or walks a cycle back
    /// into it, which never terminates on its own.
    /// </summary>
    /// <returns>The absolute paths of the bundle's markdown files.</returns>
    public IReadOnlyList<string> MarkdownFiles()
    {
        var files = new List<string>();
        Collect(Root, files);
        files.Sort(static (left, right) => string.CompareOrdinal(left, right));
        return files;
    }

    /// <summary>The bundle-relative path of a file inside the bundle, with <c>/</c> separators.</summary>
    /// <param name="path">An absolute path inside the bundle.</param>
    /// <returns>The relative path.</returns>
    public string RelativePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Turns a bundle-relative path into an absolute one, refusing anything that lands
    /// outside the bundle root (PRD MCP-5, CORE-12). The check is on the normalized path,
    /// so <c>a/../..</c> is refused however it is spelled, and on the real one, so a symlink
    /// pointing out of the bundle is refused too.
    /// </summary>
    /// <param name="relativePath">A bundle-relative path, empty for the root itself.</param>
    /// <param name="fullPath">The absolute path, when it is inside the bundle.</param>
    /// <returns><see langword="true" /> when the path is contained.</returns>
    public bool TryResolve(string? relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;

        if (relativePath is not null
            && (Path.IsPathRooted(relativePath) || relativePath.Contains('\0', StringComparison.Ordinal)))
        {
            // An absolute path is never bundle-relative, even when it happens to point
            // inside the bundle: accepting it would make the caller's path grammar depend
            // on where the bundle sits on this machine. A NUL is refused before
            // `Path.GetFullPath` sees it, because it throws on one rather than answering —
            // and a containment check that throws is one a caller can turn into a crash.
            return false;
        }

        var candidate = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(Root, relativePath ?? string.Empty)));

        if (!IsInside(candidate) || !FollowsNoLinkOut(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Root;

    /// <summary>Whether an absolute path is the bundle root or sits under it, textually.</summary>
    private bool IsInside(string path) =>
        string.Equals(path, Root, StringComparison.Ordinal)
        || path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>
    /// Whether walking to a path stays inside the bundle once symlinks are followed.
    /// <see cref="Path.GetFullPath(string)" /> resolves <c>..</c> textually and cannot see a
    /// link, so without this a link planted in a bundle — by the author of a foreign bundle
    /// okf did not produce, or by a capture that went wrong — names any file on the machine,
    /// which is exactly what MCP-5 forbids. The rule is the one <see cref="MarkdownFiles" />
    /// already applies to directories: okf never follows a link out of the bundle root.
    /// </summary>
    private bool FollowsNoLinkOut(string candidate)
    {
        var current = Root;
        foreach (var segment in Path.GetRelativePath(Root, candidate).Split(Path.DirectorySeparatorChar))
        {
            if (segment.Length == 0 || string.Equals(segment, ".", StringComparison.Ordinal))
            {
                continue;
            }

            current = Path.Combine(current, segment);
            switch (Link(current, out var target))
            {
                case LinkKind.None:
                    break;

                // Walking continues from where the link actually lands, so a chain of links
                // inside the bundle stays readable and the first one that leaves is refused.
                case LinkKind.Followed when IsInside(target!):
                    current = target!;
                    break;

                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>What a filesystem entry turned out to be when asked whether it is a symlink.</summary>
    private enum LinkKind
    {
        /// <summary>An ordinary file or directory, or one that is not there at all.</summary>
        None,

        /// <summary>A symlink whose final target was resolved.</summary>
        Followed,

        /// <summary>A symlink that could not be followed: a cycle, a dangling target, an unreadable one.</summary>
        Unfollowable,
    }

    /// <summary>
    /// Classifies one filesystem entry, resolving a symlink to its final target. A link that
    /// cannot be followed is reported as such rather than guessed at, and every caller here
    /// treats that as "not contained".
    /// </summary>
    private static LinkKind Link(string path, out string? target)
    {
        target = null;
        try
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (entry.LinkTarget is null)
            {
                return LinkKind.None;
            }

            if (entry.ResolveLinkTarget(returnFinalTarget: true) is not { } resolved)
            {
                return LinkKind.Unfollowable;
            }

            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved.FullName));
            return LinkKind.Followed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return LinkKind.Unfollowable;
        }
    }

    private void Collect(string directory, List<string> files)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.md"))
        {
            if (Path.GetFileName(file).StartsWith('.'))
            {
                continue;
            }

            // A file symlink is the one way left for the walk to read a file the bundle does
            // not contain: directory links are never descended into, but a link named
            // `notes.md` pointing at `~/.ssh/id_rsa` would otherwise be linted, indexed and
            // searched as bundle content.
            if (Link(file, out var target) is LinkKind.None || (target is not null && IsInside(target)))
            {
                files.Add(file);
            }
        }

        foreach (var child in new DirectoryInfo(directory).EnumerateDirectories())
        {
            // A directory symlink is never descended into. Pointing one at an ancestor
            // makes the walk recur until the OS refuses the path — every level of which
            // re-lints the same files under a longer name — and pointing one outside the
            // bundle would lint files the bundle does not contain. `LinkTarget` is
            // non-null exactly for symlinks and other reparse points.
            if (!child.Name.StartsWith('.') && child.LinkTarget is null)
            {
                Collect(child.FullName, files);
            }
        }
    }
}
