using System.Text;

namespace Okf.Core.Index;

/// <summary>One bullet in a generated <c>index.md</c>: <c>* [Title](link) - description</c> (§8).</summary>
public sealed class OkfIndexEntry
{
    /// <summary>The heading text used for the subdirectory section of every index.</summary>
    public const string SubdirectoriesSection = "Subdirectories";

    /// <summary>The heading text used for concepts whose frontmatter carries no <c>type</c>.</summary>
    public const string OtherSection = "Other";

    /// <summary>Initializes an entry.</summary>
    /// <param name="section">The <c>#</c> heading the entry is grouped under.</param>
    /// <param name="title">The display title.</param>
    /// <param name="link">The link target, relative to the index's own directory.</param>
    /// <param name="description">The blurb, or <see langword="null" /> for an entry with none.</param>
    public OkfIndexEntry(string section, string title, string link, string? description)
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(link);
        Section = section;
        Title = title;
        Link = link;
        Description = description;
    }

    /// <summary>The <c>#</c> heading the entry is grouped under — a concept's <c>type</c>, or <see cref="SubdirectoriesSection" />.</summary>
    public string Section { get; }

    /// <summary>The display title: frontmatter <c>title</c>, else the filename stem (§4.1).</summary>
    public string Title { get; }

    /// <summary>The link target, relative to the index's own directory, with <c>/</c> separators.</summary>
    public string Link { get; }

    /// <summary>The blurb, or <see langword="null" /> when the entry carries none.</summary>
    public string? Description { get; }

    /// <summary>Whether the entry links a subdirectory rather than a concept.</summary>
    public bool IsSubdirectory =>
        string.Equals(Section, SubdirectoriesSection, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() =>
        $"* [{Title}]({Link})" + (Description is { Length: > 0 } text ? $" - {text}" : string.Empty);
}

/// <summary>How a directory's on-disk <c>index.md</c> stands against the generated one.</summary>
public enum OkfIndexStatus
{
    /// <summary>No <c>index.md</c> is on disk; generating writes a new file.</summary>
    Created = 0,

    /// <summary>The file on disk is byte-identical to the generated one.</summary>
    Unchanged,

    /// <summary>
    /// The file on disk carries okf-net's generated marker but no longer matches the
    /// tree. This is drift: a generated file was hand-edited, or the tree moved under it.
    /// </summary>
    Drifted,

    /// <summary>
    /// The file on disk differs and carries no generated marker, so okf-net did not write
    /// it. Never drift — Google's reference bundles hand-style their indexes and must not
    /// be condemned for it (PRD ACC-1).
    /// </summary>
    Foreign,

    /// <summary>
    /// A marked generated <c>index.md</c> sits in a directory that no longer has anything
    /// to index. Drift, but never fixed by writing: <c>okf index</c> reports it and leaves
    /// the file for a human to delete.
    /// </summary>
    Orphaned,
}

/// <summary>
/// One <c>index.md</c> okf-net would generate, together with what is on disk in its place
/// (PRD CORE-9, CORE-10).
/// </summary>
public sealed class OkfIndex
{
    /// <summary>Initializes an index.</summary>
    /// <param name="path">The absolute path of the <c>index.md</c>.</param>
    /// <param name="isBundleRoot">Whether the index sits at the bundle root.</param>
    /// <param name="entries">The entries, in render order.</param>
    /// <param name="content">The rendered file text; empty for an orphan.</param>
    /// <param name="existingContent">The file's current text, or <see langword="null" /> when absent.</param>
    /// <param name="status">How the on-disk file stands against the generated one.</param>
    public OkfIndex(
        string path,
        bool isBundleRoot,
        IReadOnlyList<OkfIndexEntry> entries,
        string content,
        string? existingContent,
        OkfIndexStatus status)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(content);
        Path = path;
        IsBundleRoot = isBundleRoot;
        Entries = entries;
        Content = content;
        ExistingContent = existingContent;
        Status = status;
    }

    /// <summary>The absolute path of the <c>index.md</c>.</summary>
    public string Path { get; }

    /// <summary>The directory the index describes.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>
    /// Whether the index sits at the bundle root — the only index that carries
    /// frontmatter, and only <c>okf_version</c> (§8, §12).
    /// </summary>
    public bool IsBundleRoot { get; }

    /// <summary>The entries, in render order.</summary>
    public IReadOnlyList<OkfIndexEntry> Entries { get; }

    /// <summary>The rendered file text; empty when the directory has nothing to index.</summary>
    public string Content { get; }

    /// <summary>The file's current text, or <see langword="null" /> when no file is on disk.</summary>
    public string? ExistingContent { get; }

    /// <summary>How the on-disk file stands against the generated one.</summary>
    public OkfIndexStatus Status { get; }

    /// <summary>
    /// Whether this counts as drift: a generated file that no longer matches the tree.
    /// A hand-written index okf-net never wrote is deliberately excluded (PRD ACC-1).
    /// </summary>
    public bool IsDrift => Status is OkfIndexStatus.Drifted or OkfIndexStatus.Orphaned;

    /// <summary>Whether generating would change the file on disk.</summary>
    public bool WouldWrite => Status is OkfIndexStatus.Created or OkfIndexStatus.Drifted or OkfIndexStatus.Foreign;

    /// <inheritdoc />
    public override string ToString() => $"{Path} ({Status})";
}

/// <summary>Every <c>index.md</c> okf-net would generate for one bundle.</summary>
public sealed class OkfIndexPlan
{
    /// <summary>Initializes a plan.</summary>
    /// <param name="bundle">The bundle the plan covers.</param>
    /// <param name="indexes">The indexes, ordered by path.</param>
    public OkfIndexPlan(OkfBundle bundle, IReadOnlyList<OkfIndex> indexes)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(indexes);
        Bundle = bundle;
        Indexes = indexes;
    }

    /// <summary>The bundle the plan covers.</summary>
    public OkfBundle Bundle { get; }

    /// <summary>The indexes, ordered by path (ordinal), deepest paths sorting with their parents.</summary>
    public IReadOnlyList<OkfIndex> Indexes { get; }

    /// <summary>The indexes that drifted — the mechanical basis for <c>OKF0306</c>.</summary>
    public IEnumerable<OkfIndex> Drift => Indexes.Where(index => index.IsDrift);

    /// <summary>Whether any index drifted.</summary>
    public bool HasDrift => Indexes.Any(index => index.IsDrift);

    /// <summary>
    /// The index for one directory, or <see langword="null" /> when that directory has
    /// nothing to list. This is the read side of on-the-fly synthesis (PRD CORE-10): the
    /// caller renders <see cref="OkfIndex.ExistingContent" /> when a bundle ships an index
    /// and <see cref="OkfIndex.Content" /> when it does not, without writing either.
    /// </summary>
    /// <param name="directory">An absolute directory path inside the bundle.</param>
    /// <returns>The index for that directory, or <see langword="null" />.</returns>
    public OkfIndex? For(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string wanted = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));
        return Indexes.FirstOrDefault(
            index => string.Equals(index.Directory, wanted, StringComparison.Ordinal));
    }
}

/// <summary>
/// Where index generation reads the bundle from. The defaults read the filesystem; the
/// linter supplies its own caches so surfacing <c>OKF0306</c> costs no extra file reads
/// or YAML parses on top of the lint walk it already performs.
/// </summary>
public sealed class OkfIndexOptions
{
    /// <summary>
    /// The bundle's markdown files, absolute and ordinal-sorted. When
    /// <see langword="null" />, <see cref="OkfBundle.MarkdownFiles" /> supplies them.
    /// </summary>
    public IReadOnlyList<string>? Files { get; set; }

    /// <summary>
    /// Supplies a file's text. Returning <see langword="null" /> falls back to reading the
    /// file.
    /// </summary>
    public Func<string, string?>? ReadText { get; set; }

    /// <summary>
    /// Supplies a concept's parsed frontmatter, or <see langword="null" /> when the file
    /// has none that parses. Unset falls back to parsing the file's text.
    /// </summary>
    public Func<string, OkfMapping?>? ReadFrontmatter { get; set; }
}

/// <summary>
/// Deterministic, offline generation of <c>index.md</c> files (PRD CORE-9, CORE-10;
/// decisions.md Q1). No network, no model call, no clock: the same tree always renders
/// the same bytes, so regeneration is idempotent and drift is a mechanical comparison.
/// </summary>
/// <remarks>
/// <para><b>Ordering.</b> Entries are grouped into <c>#</c> sections, one per concept
/// <c>type</c> (§4.1), plus a <see cref="OkfIndexEntry.SubdirectoriesSection" /> section.
/// Concept sections come first, ordered case-insensitively then ordinally by heading text;
/// the subdirectory section is always <em>last</em>, so navigation follows content
/// whatever the bundle's type vocabulary happens to be. Within a section, entries are
/// ordered case-insensitively then ordinally by <em>link</em> — that is, by filename.
/// Filename rather than title because every entry has one: <c>title</c> is optional (§4.1)
/// and may repeat, so ordering by it is neither total nor stable, whereas a directory
/// cannot hold two files of the same name.</para>
/// <para><b>Marker.</b> Every generated file carries
/// <see cref="GeneratedMarker" /> on its own line, first in the file — after the
/// frontmatter on the bundle-root index. It is what lets drift detection tell an
/// index okf-net wrote from one a foreign producer hand-styled: only the former can drift
/// (PRD ACC-1). An HTML comment is used because §8 permits no frontmatter outside the
/// bundle root, and because it survives every markdown renderer invisibly. The position is
/// part of the signal, not decoration; see <see cref="IsGenerated" />.</para>
/// </remarks>
public static class OkfIndexGenerator
{
    /// <summary>
    /// The line every generated index carries, marking the file as okf-net's output.
    /// Removing it opts the file out of drift detection.
    /// </summary>
    public const string GeneratedMarker = "<!-- generated by okf -->";

    /// <summary>The frontmatter the bundle-root index carries, and the only index frontmatter (§8, §12).</summary>
    public const string RootFrontmatter = "okf_version: \"0.2\"";

    /// <summary>
    /// Whether a file's text carries the generated marker where a generated file carries
    /// it: as the first non-blank line, after a frontmatter block if there is one.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns><see langword="true" /> when okf-net wrote the file.</returns>
    /// <remarks>
    /// The position matters. Scanning the whole file for the marker would claim any
    /// hand-written index that merely quotes it — an index that documents okf, say, or one
    /// with the marker in a fenced block — and claiming it has teeth: the file would be
    /// reported as drift (<c>OKF0306</c>, and a non-zero <c>okf index --check</c>) and
    /// then overwritten as an "update" rather than as the replacement of hand-written
    /// content it is. A foreign index must never be blamed for drift (PRD ACC-1), so only
    /// a file that *declares itself* generated at the top is treated as ours. Reading is
    /// otherwise tolerant: CRLF endings, trailing whitespace, leading blank lines, and an
    /// edited root frontmatter block all still round-trip to <see langword="true" />.
    /// </remarks>
    public static bool IsGenerated(string? text)
    {
        if (text is null)
        {
            return false;
        }

        string[] lines = text.Split('\n');
        return AfterFrontmatter(lines) is { } start
            && FirstNonBlankLine(lines, start) is { } line
            && string.Equals(line, GeneratedMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the file's content starts: past the frontmatter block the bundle-root index
    /// carries (§8, §12), or at the first line when there is none.
    /// <see langword="null" /> when the block never closes, which is not something the
    /// renderer can have produced.
    /// </summary>
    private static int? AfterFrontmatter(string[] lines)
    {
        if (Line(lines, 0) is not OkfDocument.FrontmatterDelimiter)
        {
            return 0;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (Line(lines, i) is OkfDocument.FrontmatterDelimiter)
            {
                return i + 1;
            }
        }

        return null;
    }

    private static string? FirstNonBlankLine(string[] lines, int start)
    {
        for (int i = start; i < lines.Length; i++)
        {
            if (Line(lines, i) is { Length: > 0 } line)
            {
                return line;
            }
        }

        return null;
    }

    private static string Line(string[] lines, int index) =>
        index < lines.Length ? lines[index].Trim() : string.Empty;

    /// <summary>
    /// Works out every <c>index.md</c> the bundle should carry and how each stands against
    /// what is on disk. Reads only; nothing is written.
    /// </summary>
    /// <param name="bundle">The bundle to plan for.</param>
    /// <param name="options">Where to read the bundle from; defaults to the filesystem.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="IOException">A file in the bundle could not be read.</exception>
    public static OkfIndexPlan Plan(OkfBundle bundle, OkfIndexOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        options ??= new OkfIndexOptions();

        BundleTree tree = BundleTree.Of(bundle, options.Files ?? bundle.MarkdownFiles());
        Dictionary<string, List<OkfIndexEntry>> entries = EntriesByDirectory(tree, options);

        List<OkfIndex> indexes = new List<OkfIndex>();
        foreach (string directory in tree.Directories)
        {
            if (PlanDirectory(tree, directory, entries.GetValueOrDefault(directory), options) is { } index)
            {
                indexes.Add(index);
            }
        }

        // Ordered by bundle-relative path, not by absolute path, for the reason
        // OkfBundle.Walk documents: the two agree on POSIX and disagree on Windows, and
        // this order is `okf index --json`'s output order.
        indexes.Sort((left, right) =>
            string.CompareOrdinal(bundle.RelativePath(left.Path), bundle.RelativePath(right.Path)));
        return new OkfIndexPlan(bundle, indexes);
    }

    /// <summary>
    /// The entries every directory's index would list, keyed by directory. A directory
    /// with nothing to list is absent rather than empty.
    /// </summary>
    private static Dictionary<string, List<OkfIndexEntry>> EntriesByDirectory(
        BundleTree tree,
        OkfIndexOptions options)
    {
        Dictionary<string, List<OkfIndexEntry>> entriesByDirectory = new Dictionary<string, List<OkfIndexEntry>>(StringComparer.Ordinal);
        Dictionary<string, List<OkfIndexEntry>> childEntries = new Dictionary<string, List<OkfIndexEntry>>(StringComparer.Ordinal);

        foreach (string directory in tree.DeepestFirst())
        {
            // A subdirectory is listed only when it got an index of its own; listing one
            // that did not would emit a link to a file nothing writes.
            List<OkfIndexEntry> entries = ConceptEntries(tree.ConceptsIn(directory), options);
            entries.AddRange(childEntries.GetValueOrDefault(directory) ?? []);
            if (entries.Count == 0)
            {
                continue;
            }

            entries.Sort(Compare);
            entriesByDirectory[directory] = entries;
            ListUnderParent(childEntries, tree, directory, options);
        }

        return entriesByDirectory;
    }

    private static List<OkfIndexEntry> ConceptEntries(List<string> concepts, OkfIndexOptions options)
    {
        List<OkfIndexEntry> entries = new List<OkfIndexEntry>();
        foreach (string concept in concepts)
        {
            if (Frontmatter(concept, options) is not { } frontmatter)
            {
                // A file whose frontmatter does not parse is not listed. It is already an
                // OKF0001 error; inventing an entry for it would only put a second
                // complaint in a generated file.
                continue;
            }

            entries.Add(new OkfIndexEntry(
                Section(FrontmatterValues.Scalar(frontmatter, "type")),
                Title(FrontmatterValues.Scalar(frontmatter, "title"), concept),
                Link(Path.GetFileName(concept)),
                Blurb(FrontmatterValues.Scalar(frontmatter, "description"))));
        }

        return entries;
    }

    /// <summary>
    /// Records a directory as one bullet of its parent's subdirectory section. The bundle
    /// root is listed under nothing, and neither is a directory with no parent at all.
    /// </summary>
    private static void ListUnderParent(
        Dictionary<string, List<OkfIndexEntry>> childEntries,
        BundleTree tree,
        string directory,
        OkfIndexOptions options)
    {
        if (tree.IsRoot(directory) || Path.GetDirectoryName(directory) is not { } parent)
        {
            return;
        }

        if (!childEntries.TryGetValue(parent, out List<OkfIndexEntry>? siblings))
        {
            childEntries[parent] = siblings = [];
        }

        string name = Path.GetFileName(directory);
        siblings.Add(new OkfIndexEntry(
            OkfIndexEntry.SubdirectoriesSection,
            name,
            $"{Link(name)}/{OkfBundle.IndexFileName}",
            AboutDescription(directory, options)));
    }

    /// <summary>
    /// One directory's index, or <see langword="null" /> when the directory neither has
    /// anything to list nor carries a generated index that has outlived what it listed.
    /// </summary>
    private static OkfIndex? PlanDirectory(
        BundleTree tree,
        string directory,
        List<OkfIndexEntry>? entries,
        OkfIndexOptions options)
    {
        bool isRoot = tree.IsRoot(directory);
        string path = Path.Combine(directory, OkfBundle.IndexFileName);
        string? existing = tree.ExistingIndexIn(directory) is { } indexPath ? Text(indexPath, options) : null;

        if (entries is null)
        {
            // Nothing to index here. A generated file left behind is stale and worth
            // reporting; a hand-written one is none of okf-net's business.
            return existing is not null && IsGenerated(existing)
                ? new OkfIndex(path, isRoot, [], string.Empty, existing, OkfIndexStatus.Orphaned)
                : null;
        }

        string content = Render(isRoot, entries);
        return new OkfIndex(path, isRoot, entries, content, existing, Status(existing, content));
    }

    private static OkfIndexStatus Status(string? existing, string content) => existing switch
    {
        null => OkfIndexStatus.Created,
        _ when string.Equals(existing, content, StringComparison.Ordinal) => OkfIndexStatus.Unchanged,
        _ when IsGenerated(existing) => OkfIndexStatus.Drifted,
        _ => OkfIndexStatus.Foreign,
    };

    /// <summary>
    /// Writes every index in a plan whose on-disk form differs from the generated one.
    /// Orphans are reported by <see cref="Plan" /> but never deleted: removing a file a
    /// human may still want is not a generator's call.
    /// </summary>
    /// <param name="plan">The plan to apply.</param>
    /// <returns>The indexes written, in plan order.</returns>
    /// <exception cref="IOException">An index could not be written.</exception>
    public static IReadOnlyList<OkfIndex> Apply(OkfIndexPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        List<OkfIndex> written = new List<OkfIndex>();
        foreach (OkfIndex index in plan.Indexes.Where(index => index.WouldWrite))
        {
            Directory.CreateDirectory(index.Directory);

            // UTF-8 without a BOM, and the `\n` the renderer produced: the bytes must not
            // depend on the machine that wrote them (PRD ACC-7).
            File.WriteAllText(index.Path, index.Content, FileText.Utf8NoBom);
            written.Add(index);
        }

        return written;
    }

    private static int Compare(OkfIndexEntry left, OkfIndexEntry right)
    {
        // Concept sections first, the subdirectory section last.
        int bySubdirectory = left.IsSubdirectory.CompareTo(right.IsSubdirectory);
        if (bySubdirectory != 0)
        {
            return bySubdirectory;
        }

        int bySection = string.Compare(left.Section, right.Section, StringComparison.OrdinalIgnoreCase);
        if (bySection != 0)
        {
            return bySection;
        }

        // Ordinal tiebreaks keep the order total when two strings differ only in case,
        // which OrdinalIgnoreCase calls equal.
        bySection = string.CompareOrdinal(left.Section, right.Section);
        if (bySection != 0)
        {
            return bySection;
        }

        int byLink = string.Compare(left.Link, right.Link, StringComparison.OrdinalIgnoreCase);
        return byLink != 0 ? byLink : string.CompareOrdinal(left.Link, right.Link);
    }

    private static string Render(bool isBundleRoot, IReadOnlyList<OkfIndexEntry> entries)
    {
        StringBuilder builder = new StringBuilder();
        if (isBundleRoot)
        {
            AppendRootFrontmatter(builder);
        }

        builder.Append(GeneratedMarker).Append("\n\n");

        string? section = null;
        foreach (OkfIndexEntry entry in entries)
        {
            if (!string.Equals(section, entry.Section, StringComparison.Ordinal))
            {
                AppendSectionHeading(builder, entry.Section, section);
                section = entry.Section;
            }

            AppendBullet(builder, entry);
        }

        return builder.ToString();
    }

    /// <summary>
    /// §8/§12: the bundle-root index is the only index permitted frontmatter, and only
    /// <c>okf_version</c>. Written as literal text rather than emitted, so the quoting the
    /// spec shows survives exactly.
    /// </summary>
    private static void AppendRootFrontmatter(StringBuilder builder) =>
        builder.Append(OkfDocument.FrontmatterDelimiter).Append('\n')
            .Append(RootFrontmatter).Append('\n')
            .Append(OkfDocument.FrontmatterDelimiter).Append("\n\n");

    /// <summary>
    /// Opens a <c>#</c> section, separated from the one before it by a blank line. The
    /// first heading of a file needs none: the generated marker already left one.
    /// </summary>
    private static void AppendSectionHeading(StringBuilder builder, string section, string? previousSection)
    {
        if (previousSection is not null)
        {
            builder.Append('\n');
        }

        builder.Append("# ").Append(section).Append("\n\n");
    }

    private static void AppendBullet(StringBuilder builder, OkfIndexEntry entry)
    {
        builder.Append("* [").Append(entry.Title).Append("](").Append(entry.Link).Append(')');
        if (entry.Description is { Length: > 0 } description)
        {
            builder.Append(" - ").Append(description);
        }

        builder.Append('\n');
    }

    private static string? Text(string path, OkfIndexOptions options)
    {
        if (options.ReadText is { } read && read(path) is { } text)
        {
            return text;
        }

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static OkfMapping? Frontmatter(string path, OkfIndexOptions options)
    {
        if (options.ReadFrontmatter is { } read)
        {
            return read(path);
        }

        if (Text(path, options) is not { } text)
        {
            return null;
        }

        try
        {
            return OkfDocument.Parse(text).Frontmatter;
        }
        catch (OkfDocumentException)
        {
            return null;
        }
    }

    private static string? AboutDescription(string directory, OkfIndexOptions options)
    {
        // Q1, resolved: a subdirectory's blurb comes from its own `about.md`, and from
        // nowhere else. No synthesis, no borrowing a child's description — generation
        // stays deterministic and offline (PRD CLI-16).
        string about = Path.Combine(directory, OkfBundle.AboutFileName);
        return Frontmatter(about, options) is { } frontmatter
            ? Blurb(FrontmatterValues.Scalar(frontmatter, "description"))
            : null;
    }

    private static string Section(string? type) => Flatten(type) is { Length: > 0 } text
        ? text
        : OkfIndexEntry.OtherSection;

    private static string Title(string? title, string path) => Flatten(title) is { Length: > 0 } text
        // A `[` or `]` inside a title would close the link label early and leave an entry
        // that does not match the §8 form the linter enforces (OKF0003). Substituting
        // round brackets keeps the entry readable and well formed; no reference bundle
        // has ever needed it.
        ? text.Replace('[', '(').Replace(']', ')')
        : Path.GetFileNameWithoutExtension(path);

    private static string? Blurb(string? description) => Flatten(description) is { Length: > 0 } text ? text : null;

    private static string Link(string name)
    {
        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            if (PercentEncoded(character) is { } escape)
            {
                builder.Append(escape);
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The percent escape a character needs inside a link target, or
    /// <see langword="null" /> when it needs none. A markdown link target ends at the
    /// first <c>)</c>, and a bare space ends it too, so those three characters are the
    /// ones that would break the entry. Everything else is left alone: <c>/</c> must stay
    /// a separator and readability matters more than exhaustive escaping.
    /// </summary>
    private static string? PercentEncoded(char character) => character switch
    {
        ' ' => "%20",
        '(' => "%28",
        ')' => "%29",
        _ => null,
    };

    private static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // A YAML block scalar can carry newlines; an index entry is one line. Runs of
        // whitespace collapse to a single space so the rendered bullet stays a bullet.
        StringBuilder builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// What one walk of a bundle found: every directory an index could belong in, the
    /// concepts to list under each, and the <c>index.md</c> already on disk there.
    /// </summary>
    private sealed class BundleTree
    {
        private readonly Dictionary<string, List<string>> _concepts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _existingIndexes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _directories;
        private readonly string _root;

        private BundleTree(string root)
        {
            _root = root;
            _directories = new HashSet<string>(StringComparer.Ordinal) { root };
        }

        /// <summary>Every directory an index could belong in, the bundle root included.</summary>
        public IEnumerable<string> Directories => _directories;

        /// <summary>Walks a bundle's markdown files.</summary>
        /// <param name="bundle">The bundle being planned for.</param>
        /// <param name="files">Its markdown files, absolute.</param>
        /// <returns>The tree.</returns>
        public static BundleTree Of(OkfBundle bundle, IReadOnlyList<string> files)
        {
            BundleTree tree = new BundleTree(bundle.Root);
            foreach (string file in files)
            {
                string directory = Path.GetDirectoryName(file)!;
                tree.AddDirectoryAndAncestors(directory);

                if (string.Equals(Path.GetFileName(file), OkfBundle.IndexFileName, StringComparison.Ordinal))
                {
                    tree._existingIndexes[directory] = file;
                }
                else if (!OkfBundle.IsReservedFile(file))
                {
                    tree.AddConcept(directory, file);
                }
            }

            return tree;
        }

        public bool IsRoot(string directory) =>
            string.Equals(directory, _root, StringComparison.Ordinal);

        public string? ExistingIndexIn(string directory) => _existingIndexes.GetValueOrDefault(directory);

        public List<string> ConceptsIn(string directory) => _concepts.GetValueOrDefault(directory) ?? [];

        /// <summary>
        /// The directories deepest first, so a directory knows whether its children ended
        /// up indexed before it decides whether it has anything to list.
        /// </summary>
        /// <returns>The directories, in walk order.</returns>
        public List<string> DeepestFirst() => _directories
            .OrderByDescending(directory => directory.Length)
            .ThenBy(directory => directory, StringComparer.Ordinal)
            .ToList();

        private void AddDirectoryAndAncestors(string directory)
        {
            for (string? current = directory;
                 current is not null && current.Length >= _root.Length;
                 current = Path.GetDirectoryName(current))
            {
                if (!_directories.Add(current) || IsRoot(current))
                {
                    break;
                }
            }
        }

        private void AddConcept(string directory, string concept)
        {
            if (!_concepts.TryGetValue(directory, out List<string>? list))
            {
                _concepts[directory] = list = [];
            }

            list.Add(concept);
        }
    }
}
