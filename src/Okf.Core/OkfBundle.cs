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

    /// <inheritdoc />
    public override string ToString() => Root;

    private static void Collect(string directory, List<string> files)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.md"))
        {
            if (!Path.GetFileName(file).StartsWith('.'))
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
