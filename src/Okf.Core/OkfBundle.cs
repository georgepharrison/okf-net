using System.Collections.Frozen;
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

    /// <summary>
    /// The only names the walk refuses to read: version-control and editor state
    /// directories, and the files an operating system drops into a directory nobody asked
    /// it to. Matched on the whole name, case-insensitively, for files and directories
    /// alike — a git worktree spells <c>.git</c> as a file.
    /// </summary>
    /// <remarks>
    /// A leading dot is not itself a reason to skip anything. Spec §11 conforms every
    /// non-reserved <c>.md</c> file in the tree, so a walk that skipped
    /// <c>.hidden.md</c> would report a bundle conformant without having read it. This
    /// list is the whole of the deviation from that, and it is kept short and literal for
    /// the same reason the bundler's junk list is: a name that does not say it is tool
    /// state belongs to the producer who wrote it. <c>.gitignore</c> and
    /// <c>.editorconfig</c> are therefore content, and ship.
    /// </remarks>
    public static readonly IReadOnlySet<string> IgnoredMetadataNames = FrozenSet.ToFrozenSet(
        [".DS_Store", ".git", ".hg", ".idea", ".obsidian", ".svn", ".vscode", "Thumbs.db", "desktop.ini"],
        StringComparer.OrdinalIgnoreCase);

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
    /// bundle-relative path) — including a dot-prefixed one, and one inside a
    /// dot-directory, because spec §11 conforms every non-reserved <c>.md</c> file in the
    /// tree. Only <see cref="IgnoredMetadataNames" /> is skipped. Symlinked subdirectories
    /// are skipped too — following them either walks out of the bundle root, which nothing
    /// may do, or walks a cycle back into it, which never terminates on its own.
    /// </summary>
    /// <returns>The absolute paths of the bundle's markdown files.</returns>
    public IReadOnlyList<string> MarkdownFiles() => Walk("*.md");

    /// <summary>
    /// Every file in the tree, not only the markdown, under exactly the rules
    /// <see cref="MarkdownFiles" /> walks by: nothing named in
    /// <see cref="IgnoredMetadataNames" />, and no symlink that leaves the bundle. A
    /// bundle's content is more than its concepts — spec §6.3's <c>references/</c>
    /// convention explicitly covers code, and Google's own bundles carry <c>.py</c>
    /// attesters and a <c>viz.html</c> — so the bundler ships what the walk sees rather
    /// than what the linter reads.
    /// </summary>
    /// <returns>The absolute paths of the bundle's files, in ordinal path order.</returns>
    public IReadOnlyList<string> ContentFiles() => Walk("*");

    /// <summary>
    /// Walks the bundle for a pattern and orders the result by BUNDLE-relative path — the
    /// <c>/</c>-separated form — rather than by the absolute one.
    /// </summary>
    /// <remarks>
    /// On a POSIX filesystem the two orders are identical: every path shares the
    /// <c>Root + '/'</c> prefix, so comparing the whole string compares the tail. On
    /// Windows they are not identical, because the separator is <c>\</c> (0x5C), which
    /// sorts ABOVE the letters, where <c>/</c> (0x2F) sorts below them — so
    /// <c>a/b.md</c> and <c>aZ.md</c> come out in opposite orders on the two platforms.
    /// That order is not internal: <c>okf inbox</c> emits its items in exactly this order
    /// (<see cref="OkfInboxScanner.Scan" /> appends as it walks and never re-sorts), and it decides
    /// which of two near-duplicate concepts <see cref="OkfLinter" /> keeps as the original
    /// and which gets OKF0303 — so on Windows the walk would name the other file. Sorting
    /// on the bundle-relative path fixes both to the spec's separator instead of the host's.
    ///
    /// Two orders a reader might expect to find here are decided elsewhere, and each has to
    /// be fixed where it is decided. <c>okf index --json</c>'s entry order is the index
    /// PLAN's sort, which rebuilds its own list and re-sorts (see
    /// <c>OkfIndexGenerator.Plan</c>). <c>okf lint</c>'s diagnostic order is
    /// <see cref="OkfDiagnostic.CompareTo" />, which totally re-sorts by path and so
    /// discards this order entirely. Neither inherits anything from the walk.
    /// (The bundler is unaffected either way — it re-sorts its entries by their
    /// <c>/</c>-paths — but it should not have to be the only thing that is.)
    /// </remarks>
    /// <param name="pattern">The search pattern to collect.</param>
    /// <returns>The absolute paths, ordered by their bundle-relative form.</returns>
    private IReadOnlyList<string> Walk(string pattern)
    {
        var files = new List<string>();
        Collect(Root, pattern, files);

        var ordered = new List<(string Relative, string Absolute)>(files.Count);
        foreach (var file in files)
        {
            ordered.Add((RelativePath(file), file));
        }

        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Relative, right.Relative));
        return ordered.ConvertAll(static entry => entry.Absolute);
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
    /// <remarks>
    /// A backslash is refused outright, and that is the one rule here that is about
    /// portability rather than containment. Bundle-relative paths are <c>/</c>-separated by
    /// spec, and <see cref="RelativePath" /> is the only thing that mints them — so a
    /// backslash never arrives from okf-net, only from a caller. Left alone it would mean
    /// two different things: a literal character in a filename on Linux and macOS, a
    /// directory separator on Windows. One string, two resolutions, and the containment
    /// argument would then have to be made twice. <see cref="OkfConceptReader.Normalize" /> and
    /// the MCP server's directory normalizer already refuse it for the same reason; this
    /// is the containment primitive itself refusing it, so a caller that reaches
    /// <c>TryResolve</c> directly cannot be the one that gets it wrong.
    /// </remarks>
    public bool TryResolve(string? relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;

        if (relativePath is not null
            && (Path.IsPathRooted(relativePath)
                || relativePath.Contains('\\', StringComparison.Ordinal)
                || relativePath.Contains('\0', StringComparison.Ordinal)))
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

    private void Collect(string directory, string pattern, List<string> files)
    {
        foreach (var file in Directory.EnumerateFiles(directory, pattern))
        {
            if (IgnoredMetadataNames.Contains(Path.GetFileName(file)))
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
            if (!IgnoredMetadataNames.Contains(child.Name) && child.LinkTarget is null)
            {
                Collect(child.FullName, pattern, files);
            }
        }
    }
}
