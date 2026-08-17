using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Okf.Core.Bundle;

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

        List<OkfBundle> bundles = Select(workingSet, options.Bundles);
        List<OkfDistributionEntry> entries = CollectEntries(bundles);
        OkfDistributionManifest manifest = CreateManifest(workingSet.VaultRoot, bundles, entries, options);
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

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));

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
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        VerificationPreparation preparation = PrepareVerification(full);
        if (preparation.Refusal is { } refusal)
        {
            return refusal;
        }

        return VerifyDistribution(preparation.Input!);
    }

    /// <summary>
    /// The format an output path names, by suffix: <c>.tar.gz</c>/<c>.tgz</c>, <c>.zip</c>,
    /// or the default when it says nothing.
    /// </summary>
    /// <returns>The format the name implies.</returns>
    private static VerificationPreparation PrepareVerification(string full)
    {
        if (ReadDistribution(full) is not { } distribution)
        {
            return MissingDistribution(full);
        }

        if (RefusalForVerification(full, distribution) is { } refusal)
        {
            return new VerificationPreparation(null, refusal);
        }

        return new VerificationPreparation(ReadVerificationInput(full, distribution), null);
    }

    private static VerificationPreparation MissingDistribution(string full) =>
        new(null, Unreadable(full, $"'{full}' is neither an archive nor a directory."));

    private static OkfDistributionVerification? RefusalForVerification(string full, DistributionContents distribution)
    {
        if (distribution.Problem is { Length: > 0 } problem)
        {
            return Unreadable(full, problem);
        }

        if (distribution.ManifestText is null)
        {
            return Unreadable(full, $"'{full}' carries no {OkfDistributionManifest.FileName}; it was not written by `okf bundle`.");
        }

        return OkfDistributionManifest.Parse(distribution.ManifestText) is null
            ? Unreadable(full, $"{OkfDistributionManifest.FileName} does not parse as a distribution manifest.")
            : null;
    }

    private static VerificationInput ReadVerificationInput(string full, DistributionContents distribution) =>
        new(full, distribution, OkfDistributionManifest.Parse(distribution.ManifestText!)!);

    private static OkfDistributionVerification VerifyDistribution(VerificationInput input)
    {
        List<OkfDistributionFinding> findings = new List<OkfDistributionFinding>();
        HashSet<string> recorded = VerifyRecordedFiles(input.Manifest, input.Contents.Digests, findings);
        VerifyUnlistedFiles(input.Contents.Digests, recorded, findings);
        VerifyForeignEntries(input.Contents.Foreign, findings);
        return new OkfDistributionVerification(input.Source, input.Manifest, findings, input.Manifest.Files.Count);
    }

    /// <inheritdoc/>
    public static OkfDistributionFormat FormatFor(string outputPath)
    {
        ArgumentNullException.ThrowIfNull(outputPath);

        if (outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return OkfDistributionFormat.Zip;
        }

        return OkfDistributionFormat.TarGz;
    }

    private static List<OkfDistributionEntry> CollectEntries(IEnumerable<OkfBundle> bundles)
    {
        List<OkfDistributionEntry> entries = new List<OkfDistributionEntry>();
        foreach (OkfBundle bundle in bundles)
        {
            foreach (string file in bundle.ContentFiles())
            {
                AddDistributionEntry(bundle, file, entries);
            }
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return entries;
    }

    private static void AddDistributionEntry(OkfBundle bundle, string file, List<OkfDistributionEntry> entries)
    {
        if (IsJunk(file))
        {
            return;
        }

        entries.Add(new OkfDistributionEntry(
            BundlesPrefix + bundle.Name + "/" + bundle.RelativePath(file),
            file,
            OkfCaptureManifest.Sha256Of(file),
            new FileInfo(file).Length,
            bundle));
    }

    private static OkfDistributionManifest CreateManifest(
        string? vaultRoot,
        IReadOnlyList<OkfBundle> bundles,
        IReadOnlyList<OkfDistributionEntry> entries,
        OkfBundlerOptions options) =>
        new(
            options.Generator,
            VaultName(vaultRoot),
            OkfCanonicalTimestamp.ToCanonical(options.GeneratedAt),
            [.. bundles.Select(bundle => bundle.Name)],
            DanglingLinks([.. entries], vaultRoot),
            [.. entries.Select(entry => new OkfDistributionFile(entry.Path, entry.Sha256))]);

    private static DistributionContents? ReadDistribution(string full)
    {
        if (Directory.Exists(full))
        {
            return ReadDirectoryDistribution(full);
        }

        return File.Exists(full) ? ReadArchiveDistribution(full) : null;
    }

    private static DistributionContents ReadDirectoryDistribution(string full)
    {
        (string? manifestText, Dictionary<string, string> digests, List<string> foreign) = ReadDirectory(full);
        return new DistributionContents(manifestText, digests, foreign);
    }

    private static DistributionContents ReadArchiveDistribution(string full)
    {
        try
        {
            (string? manifestText, Dictionary<string, string> digests, List<string> foreign) = ReadArchive(full);
            return new DistributionContents(manifestText, digests, foreign);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return new DistributionContents(
                null,
                new Dictionary<string, string>(StringComparer.Ordinal),
                [],
                $"'{full}' is not a readable archive: {exception.Message}");
        }
    }

    private static HashSet<string> VerifyRecordedFiles(
        OkfDistributionManifest manifest,
        IReadOnlyDictionary<string, string> digests,
        List<OkfDistributionFinding> findings)
    {
        HashSet<string> recorded = new HashSet<string>(StringComparer.Ordinal);
        foreach (OkfDistributionFile file in manifest.Files)
        {
            VerifyRecordedFile(file, digests, findings, recorded);
        }

        return recorded;
    }

    private static void VerifyRecordedFile(
        OkfDistributionFile file,
        IReadOnlyDictionary<string, string> digests,
        List<OkfDistributionFinding> findings,
        HashSet<string> recorded)
    {
        recorded.Add(file.Path);
        if (!digests.TryGetValue(file.Path, out string? actual))
        {
            findings.Add(new OkfDistributionFinding(
                OkfDistributionIssue.Missing,
                file.Path,
                "recorded in the manifest and not present."));
            return;
        }

        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new OkfDistributionFinding(
                OkfDistributionIssue.Modified,
                file.Path,
                $"recorded {OkfCaptureManifest.Short(file.Sha256)}, found {OkfCaptureManifest.Short(actual)}."));
        }
    }

    private static void VerifyUnlistedFiles(
        IReadOnlyDictionary<string, string> digests,
        HashSet<string> recorded,
        List<OkfDistributionFinding> findings)
    {
        foreach (string extra in digests.Keys.Where(key => !recorded.Contains(key)).Order(StringComparer.Ordinal))
        {
            findings.Add(new OkfDistributionFinding(
                OkfDistributionIssue.Unlisted,
                extra,
                "present and recorded nowhere in the manifest."));
        }
    }

    private static void VerifyForeignEntries(IEnumerable<string> foreign, List<OkfDistributionFinding> findings)
    {
        foreach (string link in foreign.Order(StringComparer.Ordinal))
        {
            findings.Add(new OkfDistributionFinding(
                OkfDistributionIssue.Unlisted,
                link,
                "present as a link or device entry; the bundler writes regular files only."));
        }
    }

    /// <summary>The bundles a selection names, or all of them when it names none.</summary>
    private static List<OkfBundle> Select(OkfWorkingSet workingSet, IReadOnlyList<string> names)
    {
        List<OkfBundle> available = AvailableBundles(workingSet);
        return names.Count == 0 ? available : SelectedBundles(names, available);
    }

    /// <summary>
    /// The links that will dangle for a consumer: those leaving their bundle root and
    /// landing on something this distribution does not carry. A link into a bundle that
    /// <em>is</em> packaged still resolves, because the distribution keeps the vault's
    /// <c>bundles/&lt;name&gt;/</c> layout — which is the main reason it keeps it.
    /// </summary>
    private static List<OkfBundle> AvailableBundles(OkfWorkingSet workingSet) =>
        workingSet.Bundles.OrderBy(bundle => bundle.Name, StringComparer.Ordinal).ToList();

    private static List<OkfBundle> SelectedBundles(IReadOnlyList<string> names, IReadOnlyList<OkfBundle> available)
    {
        List<OkfBundle> selected = new List<OkfBundle>();
        foreach (string name in names.Distinct(StringComparer.Ordinal))
        {
            selected.Add(RequiredBundle(name, available));
        }

        return [.. selected.OrderBy(bundle => bundle.Name, StringComparer.Ordinal)];
    }

    private static OkfBundle RequiredBundle(string name, IReadOnlyList<OkfBundle> available) =>
        available.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
        ?? throw new OkfDiscoveryException(
            $"No bundle named '{name}' in the working set. Available: {string.Join(", ", available.Select(candidate => candidate.Name))}.");

    private static List<OkfExternalLink> DanglingLinks(List<OkfDistributionEntry> entries, string? vaultRoot)
    {
        HashSet<string> shipped = new HashSet<string>(entries.Select(entry => entry.SourcePath), StringComparer.Ordinal);
        Dictionary<(string From, string To, string? Bundle), OkfExternalLink> dangling = new Dictionary<(string From, string To, string? Bundle), OkfExternalLink>();

        foreach (OkfDistributionEntry entry in entries.Where(entry => entry.Path.EndsWith(".md", StringComparison.Ordinal)))
        {
            ScanExternalLinks(entry, shipped, vaultRoot, dangling);
        }

        return [.. dangling.Values
            .OrderBy(link => link.From, StringComparer.Ordinal)
            .ThenBy(link => link.Line)
            .ThenBy(link => link.To, StringComparer.Ordinal)];
    }

    private static void ScanExternalLinks(
        OkfDistributionEntry entry,
        HashSet<string> shipped,
        string? vaultRoot,
        Dictionary<(string From, string To, string? Bundle), OkfExternalLink> dangling)
    {
        string directory = Path.GetDirectoryName(entry.SourcePath)!;
        FileLayout layout = FileLayout.Of(File.ReadAllText(entry.SourcePath));
        MarkdownScan scan = MarkdownScanner.Scan(layout.Body, layout.BodyFirstLine);

        foreach (MarkdownLink link in scan.Links)
        {
            RecordExternalLink(entry, link, directory, shipped, vaultRoot, dangling);
        }
    }

    private static void RecordExternalLink(
        OkfDistributionEntry entry,
        MarkdownLink link,
        string directory,
        HashSet<string> shipped,
        string? vaultRoot,
        Dictionary<(string From, string To, string? Bundle), OkfExternalLink> dangling)
    {
        if (LintText.Resolve(link.Target, entry.Bundle.Root, directory, out string? resolved) != LinkTarget.Outside
            || resolved is null)
        {
            return;
        }

        string target = Path.TrimEndingDirectorySeparator(resolved);
        if (IsShipped(shipped, target))
        {
            return;
        }

        (string Path, string Target, string? Bundle) key = (entry.Path, link.Target, BundleNameOf(target, vaultRoot));
        if (!dangling.ContainsKey(key))
        {
            dangling[key] = new OkfExternalLink(key.Path, key.Target, key.Bundle, link.Line);
        }
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

        string prefix = Path.Combine(vaultRoot, OkfDiscovery.BundlesDirectoryName) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string remainder = resolved[prefix.Length..];
        int separator = remainder.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);
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

        string trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(vaultRoot));
        string name = Path.GetFileName(trimmed);
        if (!string.Equals(name, OkfDiscovery.VaultDirectoryName, StringComparison.Ordinal))
        {
            return name;
        }

        return Directory.GetParent(trimmed)?.Name is { Length: > 0 } project ? project : name;
    }

    private static bool IsJunk(string path)
    {
        string name = Path.GetFileName(path);
        return JunkSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The distribution's contents in write order: the files, then the manifest, sorted by path.</summary>
    private static IEnumerable<(string Path, Func<Stream> Open)> Contents(OkfDistributionPlan plan)
    {
        byte[] manifest = Encoding.UTF8.GetBytes(plan.Manifest.ToJson());
        return plan.Entries
            .Select(entry => (entry.Path, Open: (Func<Stream>)(() => File.OpenRead(entry.SourcePath))))
            .Append((OkfDistributionManifest.FileName, () => (Stream)new MemoryStream(manifest)))
            .OrderBy(item => item.Item1, StringComparer.Ordinal);
    }

    private static void WriteArchive(OkfDistributionPlan plan, string output, Action<OkfDistributionPlan, Stream> write)
    {
        string? directory = Path.GetDirectoryName(output);
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        // An archive is wholly generated output, so it is replaced rather than defended:
        // the same call that produced it produces it again, byte for byte.
        using FileStream file = File.Create(output);
        write(plan, file);
    }

    private static void WriteTarGz(OkfDistributionPlan plan, Stream output)
    {
        using GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        using TarWriter tar = CreateTarWriter(gzip);
        WriteTarContents(plan, tar);
    }

    // No directory entries: tar and every extractor create the directories a file's
    // path implies, and an entry that carries no bytes is one more thing to have to make
    // deterministic. The gzip header .NET writes carries no timestamp and no filename,
    // which is what keeps the compressed stream reproducible.
    //
    // GNU rather than PAX, and this is a reproducibility decision rather than a taste
    // one: .NET names every PAX extended-header entry `./PaxHeaders.<process-id>/.`, so a
    // PAX archive embeds the pid of the process that wrote it and two builds of the same
    // vault differ. GNU carries mtime in the header itself, needs no extended headers,
    // and — unlike ustar — has no 100-character limit on a path, which an arbitrary
    // consumer's bundle may well exceed.
    //
    // `AccessTime` and `ChangeTime` are deliberately LEFT UNSET, which writes GNU's
    // atime/ctime fields as NUL bytes — what GNU tar's own writer puts there for a
    // non-incremental entry. They are already deterministic either way, so this is an
    // interoperability fix rather than a reproducibility one: those two fields occupy
    // bytes 345–368, which in *ustar* is the start of the `prefix` field, and CPython's
    // `tarfile` joins `prefix` onto the name for every non-GNU-typed entry without first
    // checking the magic. Pinning them to a real instant therefore made the stdlib module
    // every Python consumer reaches for — including the OKF reference implementation —
    // extract this archive into a directory named after the octal timestamp
    // (`02263523000/bundles/…`). GNU tar and libarchive read it correctly either way;
    // writing NULs makes CPython read it correctly too, and costs nothing.
    private static TarWriter CreateTarWriter(GZipStream gzip) =>
        new TarWriter(gzip, TarEntryFormat.Gnu, leaveOpen: true);

    private static void WriteTarContents(OkfDistributionPlan plan, TarWriter tar)
    {
        foreach ((string path, Func<Stream> open) in Contents(plan))
        {
            WriteTarEntry(tar, path, open);
        }
    }

    private static void WriteTarEntry(TarWriter tar, string path, Func<Stream> open)
    {
        using Stream content = open();
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

    private static void WriteZip(OkfDistributionPlan plan, Stream output)
    {
        using ZipArchive zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        foreach ((string path, Func<Stream> open) in Contents(plan))
        {
            ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);

            // A DateTimeOffset with an explicit zero offset, so the DOS timestamp the zip
            // format stores does not depend on the packaging machine's time zone.
            entry.LastWriteTime = ArchiveTimestamp;
            entry.ExternalAttributes = ZipFileAttributes;

            using Stream content = open();
            using Stream target = entry.Open();
            content.CopyTo(target);
        }
    }

    private static void WriteDirectory(OkfDistributionPlan plan, string output)
    {
        Clear(output);
        Directory.CreateDirectory(output);

        foreach ((string path, Func<Stream> open) in Contents(plan))
        {
            string target = Path.Combine(output, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream content = open();
            using FileStream file = File.Create(target);
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
        if (!Directory.Exists(output) || IsEmptyDirectory(output))
        {
            return;
        }

        OkfDistributionManifest manifest = ReadExistingManifest(output);
        DeleteRecordedFiles(output, manifest);
        File.Delete(ManifestPath(output));
        PruneEmptyDirectories(output);
    }

    /// <summary>Removes the directories a cleared distribution left behind, deepest first.</summary>
    private static bool IsEmptyDirectory(string output) => !Directory.EnumerateFileSystemEntries(output).Any();

    private static OkfDistributionManifest ReadExistingManifest(string output)
    {
        string manifestPath = ManifestPath(output);
        if (File.Exists(manifestPath) && OkfDistributionManifest.Parse(File.ReadAllText(manifestPath)) is { } manifest)
        {
            return manifest;
        }

        throw new IOException(
            $"'{output}' is not empty and holds no readable {OkfDistributionManifest.FileName}, "
            + "so it was not written by `okf bundle`. Point --out at an empty or new directory.");
    }

    private static string ManifestPath(string output) => Path.Combine(output, OkfDistributionManifest.FileName);

    private static void DeleteRecordedFiles(string output, OkfDistributionManifest manifest)
    {
        foreach (OkfDistributionFile file in manifest.Files)
        {
            DeleteRecordedFile(output, file);
        }
    }

    private static void DeleteRecordedFile(string output, OkfDistributionFile file)
    {
        string target = Path.Combine(output, file.Path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(target))
        {
            File.Delete(target);
        }
    }

    private static void PruneEmptyDirectories(string root)
    {
        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
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
        Dictionary<string, string> digests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
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
        using FileStream file = File.OpenRead(archive);
        Span<byte> magic = stackalloc byte[2];
        return file.ReadAtLeast(magic, 2, throwOnEndOfStream: false) == 2 && magic[0] == 'P' && magic[1] == 'K';
    }

    private static (string? Manifest, Dictionary<string, string> Digests, List<string> Foreign) ReadZip(string archive)
    {
        ArchiveReadState state = new ArchiveReadState();
        using ZipArchive zip = ZipFile.OpenRead(archive);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            ReadZipEntry(entry, state);
        }

        return state.Result();
    }

    private static (string? Manifest, Dictionary<string, string> Digests, List<string> Foreign) ReadTarGz(
        string archive)
    {
        ArchiveReadState state = new ArchiveReadState();
        using FileStream file = File.OpenRead(archive);
        using GZipStream gzip = new GZipStream(file, CompressionMode.Decompress);
        using TarReader reader = new TarReader(gzip);

        while (reader.GetNextEntry() is { } entry)
        {
            ReadTarEntry(entry, state);
        }

        return state.Result();
    }

    private static void ReadZipEntry(ZipArchiveEntry entry, ArchiveReadState state)
    {
        if (entry.FullName.EndsWith('/'))
        {
            return;
        }

        using Stream content = entry.Open();
        if (IsManifestEntry(entry.FullName))
        {
            state.Manifest = Read(content);
            return;
        }

        state.Digests[entry.FullName] = OkfCaptureManifest.Sha256Of(content);
    }

    private static void ReadTarEntry(TarEntry entry, ArchiveReadState state)
    {
        string path = TarPath(entry);
        if (IsTarDirectory(entry))
        {
            return;
        }

        if (!IsTarRegularFile(entry))
        {
            state.Foreign.Add(Path.TrimEndingDirectorySeparator(path));
            return;
        }

        ReadTarFile(path, entry, state);
    }

    private static string TarPath(TarEntry entry) =>
        entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;

    private static bool IsTarDirectory(TarEntry entry) => entry.EntryType is TarEntryType.Directory or TarEntryType.DirectoryList;

    private static bool IsTarRegularFile(TarEntry entry) =>
        entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;

    private static void ReadTarFile(string path, TarEntry entry, ArchiveReadState state)
    {
        Stream content = entry.DataStream ?? Stream.Null;
        if (IsManifestEntry(path))
        {
            state.Manifest = Read(content);
            return;
        }

        state.Digests[path] = OkfCaptureManifest.Sha256Of(content);
    }

    private static bool IsManifestEntry(string path) =>
        string.Equals(path, OkfDistributionManifest.FileName, StringComparison.Ordinal);

    private static string Read(Stream stream)
    {
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static OkfDistributionVerification Unreadable(string source, string detail) =>
        new(source, null, [new OkfDistributionFinding(OkfDistributionIssue.Unreadable, source, detail)], 0);

    private sealed record DistributionContents(
        string? ManifestText,
        Dictionary<string, string> Digests,
        List<string> Foreign,
        string? Problem = null);

    private sealed record VerificationInput(
        string Source,
        DistributionContents Contents,
        OkfDistributionManifest Manifest);

    private sealed record VerificationPreparation(
        VerificationInput? Input,
        OkfDistributionVerification? Refusal);

    private sealed class ArchiveReadState
    {
        public string? Manifest { get; set; }

        public Dictionary<string, string> Digests { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public List<string> Foreign { get; } = new List<string>();

        public (string? Manifest, Dictionary<string, string> Digests, List<string> Foreign) Result() =>
            (Manifest, Digests, Foreign);
    }
}
