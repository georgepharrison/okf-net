using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Okf.Core;

/// <summary>The shapes a distribution is written in (spec §3: a bundle MAY be distributed as any of these).</summary>
public enum OkfDistributionFormat
{
    /// <summary>A gzipped tar archive — the default, and the OSS distribution norm.</summary>
    TarGz = 0,

    /// <summary>A zip archive.</summary>
    Zip,

    /// <summary>A plain directory, which is also what a git repository of the distribution would hold.</summary>
    Directory,
}

/// <summary>Everything <see cref="OkfBundler" /> needs beyond the working set.</summary>
public sealed class OkfBundlerOptions
{
    /// <summary>
    /// The bundle names to package. Empty means every bundle in the working set; a name
    /// the working set does not hold is refused rather than ignored.
    /// </summary>
    public IReadOnlyList<string> Bundles { get; set; } = [];

    /// <summary>
    /// When the distribution was packaged. Injected rather than read from the clock inside
    /// the bundler so that a release can pin it and get a reproducible archive — it is the
    /// only clock reading anywhere in the packaging path.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The §7 actor recorded as having packaged the distribution, e.g.
    /// <c>okf/1.0.0-rc.15</c>. The library does not know the CLI's version, so the caller
    /// supplies it.
    /// </summary>
    public string Generator { get; set; } = "okf";
}

/// <summary>One file the distribution will carry.</summary>
public sealed class OkfDistributionEntry
{
    /// <summary>Initializes an entry.</summary>
    /// <param name="path">The distribution-relative path, with <c>/</c> separators.</param>
    /// <param name="sourcePath">The absolute path of the file it was taken from.</param>
    /// <param name="sha256">The SHA-256 of its bytes.</param>
    /// <param name="length">Its length in bytes.</param>
    /// <param name="bundle">The bundle it came from.</param>
    public OkfDistributionEntry(string path, string sourcePath, string sha256, long length, OkfBundle bundle)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        ArgumentNullException.ThrowIfNull(bundle);
        Path = path;
        SourcePath = sourcePath;
        Sha256 = sha256;
        Length = length;
        Bundle = bundle;
    }

    /// <summary>The distribution-relative path, e.g. <c>bundles/okf-net/index.md</c>.</summary>
    public string Path { get; }

    /// <summary>The absolute path of the file it was taken from.</summary>
    public string SourcePath { get; }

    /// <summary>The SHA-256 of its bytes.</summary>
    public string Sha256 { get; }

    /// <summary>Its length in bytes.</summary>
    public long Length { get; }

    /// <summary>The bundle it came from.</summary>
    public OkfBundle Bundle { get; }
}

/// <summary>What a distribution will contain, computed before a byte is written.</summary>
public sealed class OkfDistributionPlan
{
    /// <summary>Initializes a plan.</summary>
    /// <param name="bundles">The bundles being packaged, in order.</param>
    /// <param name="entries">The files, ordered by distribution path.</param>
    /// <param name="manifest">The manifest that will sit at the distribution root.</param>
    public OkfDistributionPlan(
        IReadOnlyList<OkfBundle> bundles,
        IReadOnlyList<OkfDistributionEntry> entries,
        OkfDistributionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(manifest);
        Bundles = bundles;
        Entries = entries;
        Manifest = manifest;
    }

    /// <summary>The bundles being packaged.</summary>
    public IReadOnlyList<OkfBundle> Bundles { get; }

    /// <summary>The files, ordered by distribution path.</summary>
    public IReadOnlyList<OkfDistributionEntry> Entries { get; }

    /// <summary>The manifest that will sit at the distribution root.</summary>
    public OkfDistributionManifest Manifest { get; }

    /// <summary>The links that will dangle for a consumer of this distribution.</summary>
    public IReadOnlyList<OkfExternalLink> ExternalLinks => Manifest.ExternalLinks;

    /// <summary>The total size of the packaged files, before compression.</summary>
    public long TotalBytes => Entries.Sum(entry => entry.Length);
}

/// <summary>
/// The bundler: packages a vault's bundles for <b>consume-only</b> distribution
/// (PRD §5, decisions.md §1). It ships <c>bundles/</c> and nothing else — no custodian
/// directory, no <c>raw/</c> archive, no <c>okf.json</c>, no repo-facing README — so what
/// a consumer receives is readable markdown they never have to execute.
/// </summary>
/// <remarks>
/// <para><b>Deterministic by construction.</b> Entries are sorted by path, every archive
/// timestamp is <see cref="ArchiveTimestamp" /> rather than the file's mtime, ownership is
/// root:root with empty owner names, and every file ships mode 0644. The one clock reading
/// is <see cref="OkfBundlerOptions.GeneratedAt" />, which the caller may pin — so the same
/// vault, packaged by the same version with the same stamp, is byte-identical, which is
/// what a release artifact and a verifiable download both need.</para>
/// <para><b>Cross-bundle links are left dangling and recorded, never vendored.</b> Spec
/// §6.1 makes a broken link legal and obliges consumers to tolerate one; copying the
/// linked concept into this bundle would duplicate a document along with its trust state
/// and its provenance, and the copy would rot silently. The manifest lists every such link
/// so a consumer knows what they did not receive (decisions.md, the bundler milestone).</para>
/// </remarks>
public static class OkfBundler
{
    /// <summary>The directory every packaged bundle sits under, mirroring the vault layout.</summary>
    public const string BundlesPrefix = OkfDiscovery.BundlesDirectoryName + "/";

    /// <summary>
    /// The timestamp every archive entry carries. Not the epoch: MS-DOS timestamps, which
    /// the zip format stores, cannot represent anything before 1980, and one constant that
    /// both formats can hold is worth more than a smaller number.
    /// </summary>
    public static readonly DateTimeOffset ArchiveTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The mode every packaged file ships with: <c>rw-r--r--</c>.</summary>
    private const UnixFileMode FileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>Zip's external attributes for a regular file at <see cref="FileMode" />.</summary>
    private const int ZipFileAttributes = ((0b1000 << 12) | 0b110_100_100) << 16;

    /// <summary>
    /// Name endings that are never knowledge: editor backups, swap files, and merge
    /// leftovers. Deliberately a short list matched by <em>name</em>, never by content — a
    /// file whose name does not say it is junk gets packaged, because a producer who put
    /// it in a bundle root meant it. Tool state and operating-system droppings —
    /// <c>.git</c>, <c>.obsidian</c>, <c>.DS_Store</c>, <c>Thumbs.db</c> — never reach
    /// here: <see cref="OkfBundle.IgnoredMetadataNames" /> keeps the walk itself off them,
    /// so one list serves the bundler, the linter, the index, search and the site.
    /// </summary>
    private static readonly string[] JunkSuffixes = ["~", ".swp", ".swo", ".swn", ".orig", ".rej", ".bak"];

    /// <summary>Plans a distribution: what would be packaged, and what would dangle.</summary>
    /// <param name="workingSet">The resolved vault and bundles.</param>
    /// <param name="options">The selection, the stamp, and the generator.</param>
    /// <returns>The plan, including the manifest.</returns>
    /// <exception cref="OkfDiscoveryException">A requested bundle name is not in the working set.</exception>
    /// <exception cref="IOException">A file could not be read.</exception>
    public static OkfDistributionPlan Plan(OkfWorkingSet workingSet, OkfBundlerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workingSet);
        options ??= new OkfBundlerOptions();

        var bundles = Select(workingSet, options.Bundles);
        var entries = new List<OkfDistributionEntry>();

        foreach (var bundle in bundles)
        {
            foreach (var file in bundle.ContentFiles())
            {
                if (IsJunk(file))
                {
                    continue;
                }

                entries.Add(new OkfDistributionEntry(
                    BundlesPrefix + bundle.Name + "/" + bundle.RelativePath(file),
                    file,
                    OkfCaptureManifest.Sha256Of(file),
                    new FileInfo(file).Length,
                    bundle));
            }
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));

        var manifest = new OkfDistributionManifest(
            options.Generator,
            VaultName(workingSet.VaultRoot),
            OkfCanonicalTimestamp.ToCanonical(options.GeneratedAt),
            [.. bundles.Select(bundle => bundle.Name)],
            DanglingLinks(entries, workingSet.VaultRoot),
            [.. entries.Select(entry => new OkfDistributionFile(entry.Path, entry.Sha256))]);

        return new OkfDistributionPlan(bundles, entries, manifest);
    }

    /// <summary>Writes a planned distribution.</summary>
    /// <param name="plan">The plan to write.</param>
    /// <param name="outputPath">The archive file, or the directory for <see cref="OkfDistributionFormat.Directory" />.</param>
    /// <param name="format">The shape to write.</param>
    /// <exception cref="IOException">The output could not be written, or the target directory holds something else.</exception>
    public static void Write(OkfDistributionPlan plan, string outputPath, OkfDistributionFormat format)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));

        switch (format)
        {
            case OkfDistributionFormat.Directory:
                WriteDirectory(plan, full);
                return;

            case OkfDistributionFormat.Zip:
                WriteArchive(plan, full, WriteZip);
                return;

            default:
                WriteArchive(plan, full, WriteTarGz);
                return;
        }
    }

    /// <summary>
    /// Re-hashes a distribution against its own manifest (the attestation half of the
    /// bundler): every recorded file must be present and hash to what was recorded, and
    /// nothing unrecorded may have joined it.
    /// </summary>
    /// <param name="path">An archive file or a distribution directory.</param>
    /// <returns>What was found.</returns>
    /// <exception cref="IOException">The archive or directory could not be read.</exception>
    public static OkfDistributionVerification Verify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        string? manifestText;
        Dictionary<string, string> digests;
        List<string> foreign;

        if (Directory.Exists(full))
        {
            (manifestText, digests, foreign) = ReadDirectory(full);
        }
        else if (File.Exists(full))
        {
            try
            {
                (manifestText, digests, foreign) = ReadArchive(full);
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
            {
                // A file that is not an archive at all, or one that stops in the middle:
                // someone verified the wrong path, a download landed as an error page, or
                // — the case these hashes exist for — the transfer was cut short. Both are
                // reported, never thrown. `InvalidDataException` is not an `IOException`,
                // so letting it escape would crash the process; `EndOfStreamException` IS
                // one, so letting it escape merely turned a bad download into exit 2 and
                // a stray "Unable to read beyond the end of the stream" that names no file.
                return Unreadable(full, $"'{full}' is not a readable archive: {exception.Message}");
            }
        }
        else
        {
            return Unreadable(full, $"'{full}' is neither an archive nor a directory.");
        }

        if (manifestText is null)
        {
            return Unreadable(
                full,
                $"'{full}' carries no {OkfDistributionManifest.FileName}; it was not written by `okf bundle`.");
        }

        if (OkfDistributionManifest.Parse(manifestText) is not { } manifest)
        {
            return Unreadable(full, $"{OkfDistributionManifest.FileName} does not parse as a distribution manifest.");
        }

        var findings = new List<OkfDistributionFinding>();
        var recorded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in manifest.Files)
        {
            recorded.Add(file.Path);
            if (!digests.TryGetValue(file.Path, out var actual))
            {
                findings.Add(new OkfDistributionFinding(
                    OkfDistributionIssue.Missing,
                    file.Path,
                    "recorded in the manifest and not present."));
                continue;
            }

            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new OkfDistributionFinding(
                    OkfDistributionIssue.Modified,
                    file.Path,
                    $"recorded {Short(file.Sha256)}, found {Short(actual)}."));
            }
        }

        foreach (var extra in digests.Keys.Where(key => !recorded.Contains(key)).Order(StringComparer.Ordinal))
        {
            findings.Add(new OkfDistributionFinding(
                OkfDistributionIssue.Unlisted,
                extra,
                "present and recorded nowhere in the manifest."));
        }

        // Entries that are not files at all. The bundler writes regular files and nothing
        // else, so every one of these joined the archive after it was written — and a
        // symlink is the one an attacker would add, because extracting it plants a path
        // into somebody else's filesystem. They carry no bytes to hash, so they can never
        // match a recorded digest and are always unlisted.
        foreach (var link in foreign.Order(StringComparer.Ordinal))
        {
            findings.Add(new OkfDistributionFinding(
                OkfDistributionIssue.Unlisted,
                link,
                "present as a link or device entry; the bundler writes regular files only."));
        }

        return new OkfDistributionVerification(full, manifest, findings, manifest.Files.Count);
    }

    /// <summary>
    /// The format an output path names, by suffix: <c>.tar.gz</c>/<c>.tgz</c>, <c>.zip</c>,
    /// or the default when it says nothing.
    /// </summary>
    /// <param name="outputPath">The output path.</param>
    /// <returns>The format the name implies.</returns>
    public static OkfDistributionFormat FormatFor(string outputPath)
    {
        ArgumentNullException.ThrowIfNull(outputPath);

        if (outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return OkfDistributionFormat.Zip;
        }

        return OkfDistributionFormat.TarGz;
    }

    /// <summary>The bundles a selection names, or all of them when it names none.</summary>
    private static List<OkfBundle> Select(OkfWorkingSet workingSet, IReadOnlyList<string> names)
    {
        var available = workingSet.Bundles
            .OrderBy(bundle => bundle.Name, StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
        {
            return available;
        }

        var selected = new List<OkfBundle>();
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            var bundle = available.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal));

            if (bundle is null)
            {
                throw new OkfDiscoveryException(
                    $"No bundle named '{name}' in the working set. Available: " +
                    $"{string.Join(", ", available.Select(candidate => candidate.Name))}.");
            }

            selected.Add(bundle);
        }

        return [.. selected.OrderBy(bundle => bundle.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The links that will dangle for a consumer: those leaving their bundle root and
    /// landing on something this distribution does not carry. A link into a bundle that
    /// <em>is</em> packaged still resolves, because the distribution keeps the vault's
    /// <c>bundles/&lt;name&gt;/</c> layout — which is the main reason it keeps it.
    /// </summary>
    private static List<OkfExternalLink> DanglingLinks(List<OkfDistributionEntry> entries, string? vaultRoot)
    {
        var shipped = new HashSet<string>(entries.Select(entry => entry.SourcePath), StringComparer.Ordinal);
        var dangling = new Dictionary<(string From, string To, string? Bundle), OkfExternalLink>();

        foreach (var entry in entries.Where(entry => entry.Path.EndsWith(".md", StringComparison.Ordinal)))
        {
            var directory = Path.GetDirectoryName(entry.SourcePath)!;
            var layout = FileLayout.Of(File.ReadAllText(entry.SourcePath));
            var scan = MarkdownScanner.Scan(layout.Body, layout.BodyFirstLine);

            foreach (var link in scan.Links)
            {
                if (LintText.Resolve(link.Target, entry.Bundle.Root, directory, out var resolved) != LinkTarget.Outside
                    || resolved is null)
                {
                    continue;
                }

                // A link may name a directory (`../other-bundle/`), and a trailing slash
                // survives normalization — so it is trimmed before the target is compared
                // against the packaged paths, or a link to a bundle that *is* shipped
                // would be reported as dangling.
                var target = Path.TrimEndingDirectorySeparator(resolved);
                if (IsShipped(shipped, target))
                {
                    continue;
                }

                var key = (entry.Path, link.Target, BundleNameOf(target, vaultRoot));
                if (!dangling.ContainsKey(key))
                {
                    dangling[key] = new OkfExternalLink(key.Item1, key.Item2, key.Item3, link.Line);
                }
            }
        }

        return [.. dangling.Values
            .OrderBy(link => link.From, StringComparer.Ordinal)
            .ThenBy(link => link.Line)
            .ThenBy(link => link.To, StringComparer.Ordinal)];
    }

    /// <summary>Whether a resolved target is packaged — as a file, or as a directory holding packaged files.</summary>
    private static bool IsShipped(HashSet<string> shipped, string resolved) =>
        shipped.Contains(resolved)
        || shipped.Any(path => path.StartsWith(resolved + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    /// <summary>The vault bundle a path lands in, or null when it lands outside <c>bundles/</c>.</summary>
    private static string? BundleNameOf(string resolved, string? vaultRoot)
    {
        if (vaultRoot is not { Length: > 0 })
        {
            return null;
        }

        var prefix = Path.Combine(vaultRoot, OkfDiscovery.BundlesDirectoryName) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = resolved[prefix.Length..];
        var separator = remainder.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return separator < 0 ? remainder : remainder[..separator];
    }

    /// <summary>
    /// The name recorded for the producing vault: its directory name, or the project
    /// directory's name when the vault is the conventional <c>okf/</c>, which names
    /// nothing. A name, never a path — the producer's filesystem layout is not part of
    /// what was packaged.
    /// </summary>
    private static string? VaultName(string? vaultRoot)
    {
        if (vaultRoot is not { Length: > 0 })
        {
            return null;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(vaultRoot));
        var name = Path.GetFileName(trimmed);
        if (!string.Equals(name, OkfDiscovery.VaultDirectoryName, StringComparison.Ordinal))
        {
            return name;
        }

        return Directory.GetParent(trimmed)?.Name is { Length: > 0 } project ? project : name;
    }

    private static bool IsJunk(string path)
    {
        var name = Path.GetFileName(path);
        return JunkSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The distribution's contents in write order: the files, then the manifest, sorted by path.</summary>
    private static IEnumerable<(string Path, Func<Stream> Open)> Contents(OkfDistributionPlan plan)
    {
        var manifest = Encoding.UTF8.GetBytes(plan.Manifest.ToJson());
        return plan.Entries
            .Select(entry => (entry.Path, Open: (Func<Stream>)(() => File.OpenRead(entry.SourcePath))))
            .Append((OkfDistributionManifest.FileName, () => (Stream)new MemoryStream(manifest)))
            .OrderBy(item => item.Item1, StringComparer.Ordinal);
    }

    private static void WriteArchive(OkfDistributionPlan plan, string output, Action<OkfDistributionPlan, Stream> write)
    {
        var directory = Path.GetDirectoryName(output);
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        // An archive is wholly generated output, so it is replaced rather than defended:
        // the same call that produced it produces it again, byte for byte.
        using var file = File.Create(output);
        write(plan, file);
    }

    private static void WriteTarGz(OkfDistributionPlan plan, Stream output)
    {
        // No directory entries: tar and every extractor create the directories a file's
        // path implies, and an entry that carries no bytes is one more thing to have to
        // make deterministic. The gzip header .NET writes carries no timestamp and no
        // filename, which is what keeps the compressed stream reproducible.
        //
        // GNU rather than PAX, and this is a reproducibility decision rather than a taste
        // one: .NET names every PAX extended-header entry `./PaxHeaders.<process-id>/.`,
        // so a PAX archive embeds the pid of the process that wrote it and two builds of
        // the same vault differ. GNU carries mtime in the header itself, needs no extended
        // headers, and — unlike ustar — has no 100-character limit on a path, which an
        // arbitrary consumer's bundle may well exceed.
        //
        // `AccessTime` and `ChangeTime` are deliberately LEFT UNSET, which writes GNU's
        // atime/ctime fields as NUL bytes — what GNU tar's own writer puts there for a
        // non-incremental entry. They are already deterministic either way, so this is an
        // interoperability fix rather than a reproducibility one: those two fields occupy
        // bytes 345–368, which in *ustar* is the start of the `prefix` field, and CPython's
        // `tarfile` joins `prefix` onto the name for every non-GNU-typed entry without
        // first checking the magic. Pinning them to a real instant therefore made the
        // stdlib module every Python consumer reaches for — including the OKF reference
        // implementation — extract this archive into a directory named after the octal
        // timestamp (`02263523000/bundles/…`). GNU tar and libarchive read it correctly
        // either way; writing NULs makes CPython read it correctly too, and costs nothing.
        using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        using var tar = new TarWriter(gzip, TarEntryFormat.Gnu, leaveOpen: true);

        foreach (var (path, open) in Contents(plan))
        {
            using var content = open();
            tar.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, path)
            {
                DataStream = content,
                ModificationTime = ArchiveTimestamp,
                Mode = FileMode,
                Uid = 0,
                Gid = 0,
                UserName = string.Empty,
                GroupName = string.Empty,
            });
        }
    }

    private static void WriteZip(OkfDistributionPlan plan, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var (path, open) in Contents(plan))
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);

            // A DateTimeOffset with an explicit zero offset, so the DOS timestamp the zip
            // format stores does not depend on the packaging machine's time zone.
            entry.LastWriteTime = ArchiveTimestamp;
            entry.ExternalAttributes = ZipFileAttributes;

            using var content = open();
            using var target = entry.Open();
            content.CopyTo(target);
        }
    }

    private static void WriteDirectory(OkfDistributionPlan plan, string output)
    {
        Clear(output);
        Directory.CreateDirectory(output);

        foreach (var (path, open) in Contents(plan))
        {
            var target = Path.Combine(output, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var content = open();
            using var file = File.Create(target);
            content.CopyTo(file);
        }
    }

    /// <summary>
    /// Empties a directory the bundler is about to write into. A directory it wrote before
    /// is recognized by its manifest and cleared to exactly what that manifest lists, so a
    /// re-run leaves nothing stale behind; anything else is refused untouched, because a
    /// distribution target is not a licence to delete somebody's files.
    /// </summary>
    private static void Clear(string output)
    {
        if (!Directory.Exists(output))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(output).Any())
        {
            return;
        }

        var manifestPath = Path.Combine(output, OkfDistributionManifest.FileName);
        if (!File.Exists(manifestPath)
            || OkfDistributionManifest.Parse(File.ReadAllText(manifestPath)) is not { } manifest)
        {
            throw new IOException(
                $"'{output}' is not empty and holds no readable {OkfDistributionManifest.FileName}, " +
                "so it was not written by `okf bundle`. Point --out at an empty or new directory.");
        }

        foreach (var file in manifest.Files)
        {
            var target = Path.Combine(output, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        File.Delete(manifestPath);
        PruneEmptyDirectories(output);
    }

    /// <summary>Removes the directories a cleared distribution left behind, deepest first.</summary>
    private static void PruneEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private static (string? Manifest, Dictionary<string, string> Digests, List<string> Foreign) ReadDirectory(
        string root)
    {
        string? manifest = null;
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(path, OkfDistributionManifest.FileName, StringComparison.Ordinal))
            {
                manifest = File.ReadAllText(file);
                continue;
            }

            digests[path] = OkfCaptureManifest.Sha256Of(file);
        }

        return (manifest, digests, []);
    }

    private static (string? Manifest, Dictionary<string, string> Digests, List<string> Foreign) ReadArchive(
        string archive)
    {
        return IsZip(archive) ? ReadZip(archive) : ReadTarGz(archive);
    }

    /// <summary>
    /// Whether a file is a zip, by its magic bytes rather than by its name: a downloaded
    /// artifact may have been renamed, and the bytes are what has to be read.
    /// </summary>
    private static bool IsZip(string archive)
    {
        using var file = File.OpenRead(archive);
        Span<byte> magic = stackalloc byte[2];
        return file.ReadAtLeast(magic, 2, throwOnEndOfStream: false) == 2 && magic[0] == 'P' && magic[1] == 'K';
    }

    private static (string? Manifest, Dictionary<string, string> Digests, List<string> Foreign) ReadZip(string archive)
    {
        string? manifest = null;
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);

        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            using var content = entry.Open();
            if (string.Equals(entry.FullName, OkfDistributionManifest.FileName, StringComparison.Ordinal))
            {
                manifest = Read(content);
                continue;
            }

            digests[entry.FullName] = Digest(content);
        }

        return (manifest, digests, []);
    }

    private static (string? Manifest, Dictionary<string, string> Digests, List<string> Foreign) ReadTarGz(
        string archive)
    {
        string? manifest = null;
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        var foreign = new List<string>();

        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);

        while (reader.GetNextEntry() is { } entry)
        {
            var path = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;

            if (entry.EntryType is TarEntryType.Directory or TarEntryType.DirectoryList)
            {
                // The bundler writes none, and every extractor makes the directories a
                // file's path implies, so a directory entry carries nothing to check.
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile
                or TarEntryType.V7RegularFile
                or TarEntryType.ContiguousFile))
            {
                // A symlink, a hard link, a device node. Reported rather than skipped:
                // `entry.DataStream` is null for all of them, so reading the stream to
                // decide what an entry is would let one join a distribution unnoticed.
                foreign.Add(Path.TrimEndingDirectorySeparator(path));
                continue;
            }

            // A zero-length file has no data section at all, so `DataStream` is null for
            // it — which means "empty", not "absent". Reading it as absent made `--verify`
            // report the bundler's OWN output as missing a file the moment a bundle held
            // one, which the zip and directory shapes handled correctly all along.
            var content = entry.DataStream ?? Stream.Null;

            if (string.Equals(path, OkfDistributionManifest.FileName, StringComparison.Ordinal))
            {
                manifest = Read(content);
                continue;
            }

            digests[path] = Digest(content);
        }

        return (manifest, digests, foreign);
    }

    private static string Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Digest(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    private static OkfDistributionVerification Unreadable(string source, string detail) =>
        new(source, null, [new OkfDistributionFinding(OkfDistributionIssue.Unreadable, source, detail)], 0);

    private static string Short(string sha256) => sha256.Length > 12 ? sha256[..12] + "…" : sha256;
}
