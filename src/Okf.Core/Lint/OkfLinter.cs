using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Okf.Core.Lint;

/// <summary>Everything <see cref="OkfLinter" /> needs beyond the bundles themselves.</summary>
public sealed class OkfLintOptions
{
    /// <summary>
    /// The resolved severity configuration. Defaults to the built-in severities, under
    /// which only OKF v0.2 §11 conformance errors (PRD CLI-5).
    /// </summary>
    public OkfSeverityResolver Severities { get; set; } = OkfSeverityResolver.Default;

    /// <summary>
    /// The bundle's tag registry. When <see langword="null" /> the unregistered-tag rule
    /// (<c>OKF0305</c>) has nothing to check and stays silent — the registry is a
    /// beyond-spec extension a consumer opts into.
    /// </summary>
    public IReadOnlyList<string>? TagRegistry { get; set; }

    /// <summary>
    /// The vault the bundles were resolved from, when there is one — the scope the
    /// <c>raw/</c>-immutability rule (<c>OKF0310</c>) works in. Every other rule is scoped
    /// to a bundle root; this one cannot be, because <c>raw/</c> sits outside every bundle
    /// root by construction (decisions.md Q3). <see langword="null" /> — a bundle handed
    /// over by path with no vault around it — makes the rule inapplicable rather than
    /// failing (PRD CLI-9).
    /// </summary>
    public string? VaultRoot { get; set; }

    /// <summary>
    /// The date staleness is judged against (§5.5). Injected so lint output is
    /// deterministic in tests (PRD CORE-7).
    /// </summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>The outcome of a lint run.</summary>
public sealed class OkfLintResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="diagnostics">The diagnostics, already ordered deterministically.</param>
    /// <param name="bundles">The bundles that were linted.</param>
    /// <param name="fileCount">How many markdown files were read.</param>
    /// <param name="severities">
    /// The severity configuration the run used, so a report can say which rules were live;
    /// defaults to the built-in severities.
    /// </param>
    public OkfLintResult(
        IReadOnlyList<OkfDiagnostic> diagnostics,
        IReadOnlyList<OkfBundle> bundles,
        int fileCount,
        OkfSeverityResolver? severities = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(bundles);
        Diagnostics = diagnostics;
        Bundles = bundles;
        FileCount = fileCount;
        Severities = severities ?? OkfSeverityResolver.Default;
    }

    /// <summary>Every diagnostic produced, including ones at hidden severity.</summary>
    public IReadOnlyList<OkfDiagnostic> Diagnostics { get; }

    /// <summary>The bundles that were linted.</summary>
    public IReadOnlyList<OkfBundle> Bundles { get; }

    /// <summary>How many markdown files were read.</summary>
    public int FileCount { get; }

    /// <summary>The severity configuration the run used.</summary>
    public OkfSeverityResolver Severities { get; }

    /// <summary>
    /// How many rules could have reported: those whose effective severity is not
    /// <see cref="OkfSeverity.Hidden" />. A clean run is only meaningful next to this
    /// count — nothing found and nothing enabled look identical otherwise.
    /// </summary>
    public int ActiveRuleCount =>
        OkfRules.All.Count(rule => Severities.Resolve(rule.Id) != OkfSeverity.Hidden);

    /// <summary>How many rules are configured to <see cref="OkfSeverity.Hidden" />.</summary>
    public int HiddenRuleCount => OkfRules.All.Count - ActiveRuleCount;

    /// <summary>Whether any diagnostic resolved to <see cref="OkfSeverity.Error" />.</summary>
    public bool HasErrors => Count(OkfSeverity.Error) > 0;

    /// <summary>Counts the diagnostics at one severity.</summary>
    /// <param name="severity">The severity to count.</param>
    /// <returns>The number of diagnostics at that severity.</returns>
    public int Count(OkfSeverity severity) => Diagnostics.Count(diagnostic => diagnostic.Severity == severity);
}

/// <summary>
/// The lint engine: walks bundle trees and reports diagnostics. It lives in
/// <c>Okf.Core</c> so that <c>okf lint</c>, <c>okf index</c>, and <c>okf search</c> share
/// one implementation and one set of rule identifiers (decisions.md §4).
/// </summary>
public sealed class OkfLinter
{
    private readonly OkfLintOptions _options;

    /// <summary>Initializes a linter.</summary>
    /// <param name="options">The severity configuration and inputs the rules need.</param>
    public OkfLinter(OkfLintOptions? options = null) => _options = options ?? new OkfLintOptions();

    /// <summary>Lints a set of bundles.</summary>
    /// <param name="bundles">The bundles to walk.</param>
    /// <returns>The diagnostics, in deterministic order, plus run counts.</returns>
    /// <exception cref="IOException">A file in a bundle could not be read.</exception>
    public OkfLintResult Lint(IEnumerable<OkfBundle> bundles)
    {
        ArgumentNullException.ThrowIfNull(bundles);

        List<OkfBundle> list = bundles.ToList();
        List<OkfDiagnostic> diagnostics = new List<OkfDiagnostic>();
        int files = 0;

        foreach (OkfBundle bundle in list)
        {
            files += LintBundle(new BundleWalk(bundle, diagnostics, _options.Severities));
        }

        CheckVault(diagnostics);

        diagnostics.Sort();
        return new OkfLintResult(diagnostics, list, files, _options.Severities);
    }

    /// <summary>Lints a single bundle.</summary>
    /// <param name="bundle">The bundle to walk.</param>
    /// <returns>The diagnostics, in deterministic order, plus run counts.</returns>
    /// <exception cref="IOException">A file in the bundle could not be read.</exception>
    public OkfLintResult Lint(OkfBundle bundle) => Lint([bundle]);

    private int LintBundle(BundleWalk walk)
    {
        IReadOnlyList<string> files = walk.Bundle.MarkdownFiles();
        foreach (string file in files)
        {
            LintFile(walk, file);
        }

        CheckGeneratedIndexes(walk, files);

        return files.Count;
    }

    private void LintFile(BundleWalk walk, string file)
    {
        string text = File.ReadAllText(file);
        FileLayout layout = FileLayout.Of(text);
        string name = Path.GetFileName(file);

        if (string.Equals(name, OkfBundle.IndexFileName, StringComparison.Ordinal))
        {
            walk.IndexTexts[file] = text;
            CheckIndexFile(walk, file, layout);
        }
        else if (string.Equals(name, OkfBundle.LogFileName, StringComparison.Ordinal))
        {
            CheckLogFile(walk, file, layout);
        }
        else
        {
            CheckConceptFile(walk, file, layout);
        }
    }

    private void CheckGeneratedIndexes(BundleWalk walk, IReadOnlyList<string> files)
    {
        if (_options.Severities.Resolve(OkfRules.GeneratedIndexDrift) == OkfSeverity.Hidden)
        {
            // Nothing would be reported, so nothing is rendered.
            return;
        }

        OkfIndexPlan plan = OkfIndexGenerator.Plan(
            walk.Bundle,
            new OkfIndexOptions
            {
                Files = files,
                ReadText = path => walk.IndexTexts.GetValueOrDefault(path),
                ReadFrontmatter = path => walk.Frontmatters.GetValueOrDefault(path),
            });

        foreach (OkfIndex index in plan.Drift)
        {
            walk.Report(OkfRules.GeneratedIndexDrift, DriftMessage(index), index.Path, 1);
        }
    }

    // Only indexes okf-net wrote can drift: a foreign bundle's hand-styled index carries
    // no marker and is left alone, which is what keeps PRD ACC-1 passing on Google's
    // reference bundles.
    private static string DriftMessage(OkfIndex index) =>
        index.Status == OkfIndexStatus.Orphaned
            ? "This generated index.md describes a directory with nothing left to index; delete it or add concepts (§8)."
            : "This generated index.md no longer matches the directory; run `okf index` to regenerate it (§8).";

    /// <summary>
    /// The one rule scoped to the vault rather than to a bundle root: an ingested
    /// <c>raw/</c> item that no longer matches the <c>sha256</c> its capture manifest
    /// entry recorded (PRD CLI-9).
    /// </summary>
    /// <remarks>
    /// <para>Narrow on purpose. The rule answers "did an artifact somebody already cited
    /// change underneath the citation", and nothing else: an entry still awaiting
    /// ingestion is the custodian's work queue, not a broken record, and every structural
    /// invariant of the manifest — the id grammar, the timestamps, the flat/packet layout,
    /// files in <c>raw/</c> no entry claims — belongs to
    /// <c>okf/custodian/check-manifest.py</c>, which specified all of it first and stays
    /// the belt-and-braces gate beside <c>okf lint</c>.</para>
    /// <para>A manifest that will not parse is therefore silent here rather than
    /// reported: the script reports it, and a rule that cannot read the record cannot
    /// claim the artifact changed. Detection is by hash rather than by git, which is what
    /// the PRD sketched — the recorded <c>sha256</c> is the format-level record, works in
    /// a vault that is not a work tree, and needs no process launched from a library that
    /// is offline and AOT-clean by contract (CLI-16).</para>
    /// </remarks>
    private void CheckVault(List<OkfDiagnostic> diagnostics)
    {
        if (_options.VaultRoot is not { Length: > 0 } vault
            || _options.Severities.Resolve(OkfRules.RawItemMutated) == OkfSeverity.Hidden)
        {
            // No vault, or nothing would be reported — so nothing is read and nothing is
            // hashed. Hashing every ingested artifact is the most expensive thing a lint
            // run does, and a hidden rule must not cost it.
            return;
        }

        OkfCaptureManifest? manifest = OkfCaptureManifest.TryLoad(vault, out string? text);
        if (manifest is null || text is null)
        {
            return;
        }

        CheckIngestedFiles(manifest, text, vault, diagnostics);
    }

    private void CheckIngestedFiles(
        OkfCaptureManifest manifest,
        string manifestText,
        string vault,
        List<OkfDiagnostic> diagnostics)
    {
        string rawDirectory = Path.Combine(vault, OkfCaptureManifest.RawDirectoryName);
        foreach (OkfCaptureEntry entry in manifest.Captures.Where(capture => capture.IsIngested))
        {
            foreach (OkfCaptureFile file in entry.Files)
            {
                CheckRawFile(new IngestedFile(manifest, manifestText, entry, file), rawDirectory, diagnostics);
            }
        }
    }

    private void CheckRawFile(IngestedFile ingested, string rawDirectory, List<OkfDiagnostic> diagnostics)
    {
        if (!TryResolveRawPath(rawDirectory, ingested.File.Path, out string absolute))
        {
            // A recorded path that escapes raw/ is a malformed record, which is
            // check-manifest.py's finding to report; reading the file it names is exactly
            // what this rule must not do.
            return;
        }

        if (!File.Exists(absolute))
        {
            diagnostics.Add(MissingFromRawDiagnostic(ingested));
            return;
        }

        if (TryHashOnDisk(absolute, out string? actual)
            && !string.Equals(actual, ingested.File.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(ChangedSinceIngestDiagnostic(ingested, actual));
        }
    }

    /// <summary>
    /// Hashes an artifact, reporting failure rather than throwing: an unreadable artifact
    /// is not a changed one, and lint never fails a run on what it could not open.
    /// </summary>
    private static bool TryHashOnDisk(string absolute, [NotNullWhen(true)] out string? sha256)
    {
        try
        {
            sha256 = OkfCaptureManifest.Sha256Of(absolute);
            return true;
        }
        catch (IOException)
        {
            sha256 = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            sha256 = null;
            return false;
        }
    }

    private OkfDiagnostic MissingFromRawDiagnostic(IngestedFile ingested) => VaultDiagnostic(
        $"`{ingested.File.Path}`, ingested under capture `{ingested.Entry.Id}`, is no longer in raw/. " +
        "An ingested artifact is the evidence a concept rests on; restore it rather than editing the manifest.",
        ingested.Manifest.Path,
        LineOf(ingested.ManifestText, ingested.File.Sha256));

    private OkfDiagnostic ChangedSinceIngestDiagnostic(IngestedFile ingested, string actual) => VaultDiagnostic(
        $"`{ingested.File.Path}`, ingested under capture `{ingested.Entry.Id}`, no longer matches its recorded sha256 " +
        $"(recorded {OkfCaptureManifest.Short(ingested.File.Sha256)}, on disk {OkfCaptureManifest.Short(actual)}). " +
        "The artifact changed after ingestion, " +
        "or the record did; resolve it by hand, never by rewriting the manifest.",
        ingested.Manifest.Path,
        LineOf(ingested.ManifestText, ingested.File.Sha256));

    /// <summary>
    /// Resolves a manifest-recorded path against <c>raw/</c>, refusing anything that
    /// leaves it — absolute, rooted, or climbing out with <c>..</c> however it is spelled.
    /// </summary>
    private static bool TryResolveRawPath(string rawDirectory, string recorded, out string absolute)
    {
        absolute = string.Empty;

        if (Path.IsPathRooted(recorded) || recorded.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(rawDirectory, recorded)));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawDirectory));

        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        absolute = candidate;
        return true;
    }

    /// <summary>
    /// The 1-based line the recorded hash sits on, so the diagnostic points at the record
    /// rather than at the top of the file. Null when the text does not carry it, which a
    /// hand-reformatted manifest can manage.
    /// </summary>
    private static int? LineOf(string manifestText, string needle)
    {
        string[] lines = manifestText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return null;
    }

    private OkfDiagnostic VaultDiagnostic(string message, string path, int? line) =>
        new(OkfRules.RawItemMutated, _options.Severities.Resolve(OkfRules.RawItemMutated), message, path, line);

    private static string? NestedText(OkfMapping mapping, string key, string child) =>
        mapping.TryGetValue(key, out OkfValue? value) && value is OkfMapping nested
            ? FrontmatterValues.Scalar(nested, child)
            : null;

    private static DateOnly? Date(string? text)
    {
        if (text is null || text.Length < 10)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            text[..10],
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly date)
            ? date
            : null;
    }

    private static string Normalize(string text)
    {
        StringBuilder normalized = new System.Text.StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.ToString();
    }

    private static IEnumerable<OkfMapping> Sources(OkfMapping frontmatter) =>
        frontmatter.TryGetValue("sources", out OkfValue? value) && value is OkfSequence sequence
            ? sequence.OfType<OkfMapping>()
            : [];

    private static IEnumerable<string> Tags(OkfMapping frontmatter)
    {
        if (!frontmatter.TryGetValue("tags", out OkfValue? value))
        {
            return [];
        }

        return value switch
        {
            OkfSequence sequence => sequence.OfType<OkfScalar>().Where(tag => tag.IsTruthy).Select(tag => tag.Value),
            OkfScalar scalar when scalar.IsTruthy => [scalar.Value],
            _ => [],
        };
    }

    private static void CheckIndexFile(BundleWalk walk, string path, FileLayout layout)
    {
        if (layout.HasFrontmatter)
        {
            CheckIndexFrontmatter(walk, path, layout);
        }

        MarkdownScan scan = MarkdownScanner.Scan(layout.Body, layout.BodyFirstLine);
        if (scan.HasContent && scan.Headings.Count == 0)
        {
            walk.Report(
                OkfRules.InvalidIndexStructure,
                "index.md has no `#` section heading; entries are grouped under headings (§8).",
                path,
                layout.BodyFirstLine);
        }

        CheckIndexEntries(walk, path, scan);
        CheckLinks(walk, path, scan);
    }

    // §8: index files carry no frontmatter, with one exception — a bundle-root index.md
    // MAY declare `okf_version` (§12).
    private static void CheckIndexFrontmatter(BundleWalk walk, string path, FileLayout layout)
    {
        if (TryParseIndexFrontmatter(walk, path, layout) is not { } frontmatter)
        {
            return;
        }

        if (IsBundleRootIndex(walk.Bundle, path))
        {
            CheckRootIndexKeys(walk, path, layout, frontmatter);
            return;
        }

        walk.Report(
            OkfRules.InvalidIndexStructure,
            "index.md must not carry frontmatter; only a bundle-root index.md may, and only `okf_version` (§8, §12).",
            path,
            1);
    }

    private static OkfMapping? TryParseIndexFrontmatter(BundleWalk walk, string path, FileLayout layout)
    {
        try
        {
            return OkfDocument.Parse(layout.Text).Frontmatter;
        }
        catch (OkfDocumentException exception)
        {
            walk.Report(
                OkfRules.InvalidIndexStructure,
                $"index.md has a malformed frontmatter block: {exception.Message} (§8).",
                path,
                1);
            return null;
        }
    }

    private static void CheckRootIndexKeys(BundleWalk walk, string path, FileLayout layout, OkfMapping frontmatter)
    {
        foreach (KeyValuePair<OkfValue, OkfValue> entry in frontmatter)
        {
            if (entry.Key is OkfScalar key && !string.Equals(key.Value, "okf_version", StringComparison.Ordinal))
            {
                walk.Report(
                    OkfRules.InvalidIndexStructure,
                    $"A bundle-root index.md may only carry `okf_version` in frontmatter; found `{key.Value}` (§8, §12).",
                    path,
                    layout.FrontmatterKeyLine(key.Value) ?? 1);
            }
        }
    }

    private static bool IsBundleRootIndex(OkfBundle bundle, string path) =>
        string.Equals(Path.GetDirectoryName(path), bundle.Root, StringComparison.Ordinal);

    private static void CheckIndexEntries(BundleWalk walk, string path, MarkdownScan scan)
    {
        foreach (MarkdownBullet bullet in scan.Bullets)
        {
            if (!LintText.IsIndexEntry(bullet.Text))
            {
                walk.Report(
                    OkfRules.InvalidIndexStructure,
                    $"index.md entry is not of the form `* [Title](link) - description` (§8): `{bullet.Text}`.",
                    path,
                    bullet.Line);
            }
        }
    }

    private static void CheckLogFile(BundleWalk walk, string path, FileLayout layout)
    {
        if (layout.HasFrontmatter)
        {
            CheckLogFrontmatter(walk, path, layout);
        }

        MarkdownScan scan = MarkdownScanner.Scan(layout.Body, layout.BodyFirstLine);
        CheckLogDateHeadings(walk, path, scan);
        CheckLinks(walk, path, scan);
    }

    private static void CheckLogFrontmatter(BundleWalk walk, string path, FileLayout layout)
    {
        try
        {
            OkfDocument.Parse(layout.Text);
        }
        catch (OkfDocumentException exception)
        {
            walk.Report(
                OkfRules.InvalidLogStructure,
                $"log.md has a malformed frontmatter block: {exception.Message} (§9).",
                path,
                1);
        }
    }

    // §9: `##` headings are ISO YYYY-MM-DD dates, newest first. A `#` title above them is
    // conventional, and entry prose is unconstrained.
    private static void CheckLogDateHeadings(BundleWalk walk, string path, MarkdownScan scan)
    {
        DateOnly? previous = null;

        foreach (MarkdownHeading heading in scan.Headings.Where(heading => heading.Level == 2))
        {
            DateOnly? date = Date(heading.Text);
            if (date is null)
            {
                ReportUndatedLogHeading(walk, path, heading);
                continue;
            }

            if (previous is not null && date > previous)
            {
                ReportOutOfOrderLogHeading(walk, path, heading, previous.Value);
            }

            previous = date;
        }
    }

    private static void ReportUndatedLogHeading(BundleWalk walk, string path, MarkdownHeading heading) => walk.Report(
        OkfRules.InvalidLogStructure,
        $"log.md date heading `## {heading.Text}` is not an ISO YYYY-MM-DD date (§9).",
        path,
        heading.Line);

    private static void ReportOutOfOrderLogHeading(
        BundleWalk walk,
        string path,
        MarkdownHeading heading,
        DateOnly previous) => walk.Report(
        OkfRules.InvalidLogStructure,
        $"log.md entries must be newest first: `## {heading.Text}` follows `## {previous:yyyy-MM-dd}` (§9).",
        path,
        heading.Line);

    private void CheckConceptFile(BundleWalk walk, string path, FileLayout layout)
    {
        OkfDocument? document = TryParseConcept(walk, path, layout);
        if (document is null)
        {
            return;
        }

        walk.Frontmatters[path] = document.Frontmatter;
        if (!layout.HasFrontmatter)
        {
            ReportMissingFrontmatterBlock(walk, path);
            return;
        }

        CheckRequiredKeys(walk, path, layout, document);
        MarkdownScan scan = MarkdownScanner.Scan(document.Body, layout.BodyFirstLine);
        LintedConcept concept = new LintedConcept(path, layout, document.Frontmatter, scan);

        CheckHygiene(walk, concept);
        CheckProvenance(walk, concept);
        CheckTrust(walk, concept);
        CheckLinks(walk, concept.Path, concept.Scan);
    }

    /// <summary>
    /// §11.1 asks for a frontmatter <em>block</em>; a file without one parses fine (the
    /// whole text is body) but is not a concept, so the missing block is reported rather
    /// than the <c>type</c> it could not have carried.
    /// </summary>
    private static void ReportMissingFrontmatterBlock(BundleWalk walk, string path) => walk.Report(
        OkfRules.UnparseableFrontmatter,
        "File has no YAML frontmatter block; every non-reserved .md file is a concept (§11.1).",
        path,
        1);

    /// <summary>
    /// §11.1: without a parseable frontmatter block nothing else about the file can be
    /// judged, so that is the only diagnostic an unparseable one produces.
    /// </summary>
    private static OkfDocument? TryParseConcept(BundleWalk walk, string path, FileLayout layout)
    {
        try
        {
            return OkfDocument.Parse(layout.Text);
        }
        catch (OkfDocumentException exception)
        {
            walk.Report(OkfRules.UnparseableFrontmatter, $"{exception.Message} (§11.1).", path, 1);
            return null;
        }
    }

    private static void CheckRequiredKeys(BundleWalk walk, string path, FileLayout layout, OkfDocument document)
    {
        try
        {
            document.Validate();
        }
        catch (OkfDocumentException exception)
        {
            walk.Report(
                OkfRules.MissingType,
                $"{exception.Message} (§11.2).",
                path,
                layout.FrontmatterKeyLine("type") ?? 1);
        }
    }

    private void CheckHygiene(BundleWalk walk, LintedConcept concept)
    {
        CheckDescription(walk, concept);
        CheckTags(walk, concept);
        CheckNearDuplicates(walk, concept);
    }

    private static void CheckDescription(BundleWalk walk, LintedConcept concept)
    {
        if (FrontmatterValues.Scalar(concept.Frontmatter, "description") is null)
        {
            walk.Report(
                OkfRules.MissingDescription,
                "Concept has no `description`; index entries and search results degrade without one (§4.1).",
                concept.Path,
                1);
        }
    }

    private void CheckTags(BundleWalk walk, LintedConcept concept)
    {
        List<string> tags = Tags(concept.Frontmatter).ToList();
        if (tags.Count == 0)
        {
            walk.Report(OkfRules.MissingTags, "Concept has no `tags` (§4.1).", concept.Path, 1);
        }
        else if (_options.TagRegistry is { } registry)
        {
            CheckTagsAreRegistered(walk, concept, tags, registry);
        }
    }

    private static void CheckTagsAreRegistered(
        BundleWalk walk,
        LintedConcept concept,
        List<string> tags,
        IReadOnlyList<string> registry)
    {
        foreach (string tag in tags.Where(tag => !registry.Contains(tag, StringComparer.Ordinal)))
        {
            walk.Report(
                OkfRules.UnregisteredTag,
                $"Tag `{tag}` is not in the bundle's tag registry.",
                concept.Path,
                concept.Layout.FrontmatterKeyLine("tags") ?? 1);
        }
    }

    /// <summary>
    /// Near-duplicate detection, MVP heuristic (PRD Q8): a title or filename collision up
    /// to case and punctuation. Vectorization would do better and is post-MVP.
    /// </summary>
    /// <remarks>
    /// Convention-bearing filenames are exempt from both arms (the Q1/Q8 reconciliation).
    /// <c>about.md</c> is the designated carrier of a subdirectory's description, so a
    /// well-maintained bundle holds one per subdirectory; neither that name nor the generic
    /// title that usually accompanies it says anything about the content, and reporting
    /// them would punish following the convention. Such a file neither reports a collision
    /// nor seeds one for a later file.
    /// </remarks>
    private static void CheckNearDuplicates(BundleWalk walk, LintedConcept concept)
    {
        if (OkfBundle.IsConventionalFile(concept.Path))
        {
            return;
        }

        // Reporting the filename collision as well would say the same thing twice about
        // one pair of files, so a title finding suppresses it.
        string? reportedAgainst = CheckDuplicateTitle(walk, concept);
        CheckDuplicateStem(walk, concept, reportedAgainst);
    }

    /// <summary>Reports a title collision, and returns the file it collided with if there was one.</summary>
    private static string? CheckDuplicateTitle(BundleWalk walk, LintedConcept concept)
    {
        if (FrontmatterValues.Scalar(concept.Frontmatter, "title") is not { } title
            || Normalize(title) is not { Length: > 0 } normalized)
        {
            return null;
        }

        if (!walk.TitleOwners.TryGetValue(normalized, out string? first))
        {
            walk.TitleOwners[normalized] = concept.Path;
            return null;
        }

        walk.Report(
            OkfRules.NearDuplicateConcept,
            $"Concept title duplicates `{walk.Bundle.RelativePath(first)}` up to case and punctuation.",
            concept.Path,
            concept.Layout.FrontmatterKeyLine("title") ?? 1);

        return first;
    }

    private static void CheckDuplicateStem(BundleWalk walk, LintedConcept concept, string? reportedAgainst)
    {
        string stem = Normalize(Path.GetFileNameWithoutExtension(concept.Path));
        if (stem.Length == 0)
        {
            return;
        }

        if (!walk.StemOwners.TryGetValue(stem, out string? first))
        {
            walk.StemOwners[stem] = concept.Path;
            return;
        }

        if (!string.Equals(first, reportedAgainst, StringComparison.Ordinal))
        {
            walk.Report(
                OkfRules.NearDuplicateConcept,
                $"Concept filename duplicates `{walk.Bundle.RelativePath(first)}` up to case and punctuation.",
                concept.Path,
                1);
        }
    }

    private static void CheckProvenance(BundleWalk walk, LintedConcept concept)
    {
        List<string> sourceIds = SourceIds(concept.Frontmatter);
        int sourcesLine = concept.Layout.FrontmatterKeyLine("sources") ?? 1;

        CheckFootnotesJoinSources(walk, concept, sourceIds);
        CheckSourcesAreCited(walk, concept, sourceIds, sourcesLine);
        CheckSourceResources(walk, concept, sourcesLine);
        CheckSourceDrift(walk, concept, sourcesLine);
    }

    private static List<string> SourceIds(OkfMapping frontmatter)
    {
        List<string> ids = new List<string>();
        foreach (OkfMapping source in Sources(frontmatter))
        {
            if (FrontmatterValues.Scalar(source, "id") is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static void CheckFootnotesJoinSources(BundleWalk walk, LintedConcept concept, List<string> sourceIds)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (MarkdownFootnote footnote in concept.Scan.Footnotes)
        {
            // §5.1: the footnote label is the join key into `sources`; a label that joins
            // to nothing attributes a claim to a source the bundle does not record. Both
            // occurrence forms are held to it — a definition for a label no source declares
            // is as dangling as a reference to one.
            if (seen.Add(footnote.Label) && !sourceIds.Contains(footnote.Label, StringComparer.Ordinal))
            {
                walk.Report(
                    OkfRules.UncitedFootnote,
                    $"Footnote `[^{footnote.Label}]` matches no `sources[].id` (§5.1).",
                    concept.Path,
                    footnote.Line);
            }
        }
    }

    private static void CheckSourcesAreCited(
        BundleWalk walk,
        LintedConcept concept,
        List<string> sourceIds,
        int sourcesLine)
    {
        HashSet<string> referenced = ReferencedFootnoteLabels(concept.Scan);
        HashSet<string> defined = DefinedFootnoteLabels(concept.Scan);

        foreach (string id in sourceIds.Where(id => !referenced.Contains(id)))
        {
            walk.Report(
                OkfRules.UnusedSourceId,
                defined.Contains(id) ? DefinedButNeverReferenced(id) : NeverCited(id),
                concept.Path,
                sourcesLine);
        }
    }

    // Only a footnote *reference* counts as a citation. A `[^id]: …` definition line is the
    // source's own entry in the notes, not a claim attributed to it, so a definition with no
    // reference above it is exactly the shape of a source that was listed and then never
    // used (the first dogfood bundle shipped one, and only markdownlint's MD053 caught it —
    // decisions.md, lint review flags).
    private static HashSet<string> ReferencedFootnoteLabels(MarkdownScan scan) =>
        new(scan.Footnotes.Where(footnote => !footnote.IsDefinition).Select(footnote => footnote.Label),
            StringComparer.Ordinal);

    private static HashSet<string> DefinedFootnoteLabels(MarkdownScan scan) =>
        new(scan.Footnotes.Where(footnote => footnote.IsDefinition).Select(footnote => footnote.Label),
            StringComparer.Ordinal);

    // The two shapes read very differently to the author. "Never cited by a `[^id]` footnote"
    // is false on its face when a `[^id]:` line is sitting in the document — which is
    // precisely the case this rule newly reports — so the definition-only finding says what
    // is actually missing instead.
    private static string DefinedButNeverReferenced(string id) =>
        $"Source `{id}` has a `[^{id}]:` footnote definition but is never referenced by a "
            + $"`[^{id}]` in the body; a definition is the note, not a citation (§5.1).";

    private static string NeverCited(string id) =>
        $"Source `{id}` is never cited by a `[^{id}]` footnote in the body (§5.1).";

    /// <summary>
    /// §5.1's `resource` is REQUIRED within a `sources` entry, and §6.2 fixes the forms a
    /// path-valued one may take. Neither is §11 conformance, so neither errors by default:
    /// a missing pointer is a provenance record that cannot be followed (warning), and a
    /// path that names nothing in the bundle is reported the way a broken link is (info),
    /// because the same "the target may simply not be here" tolerance applies.
    /// </summary>
    private static void CheckSourceResources(BundleWalk walk, LintedConcept concept, int sourcesLine)
    {
        foreach (NamedSource source in NamedSources(concept.Frontmatter))
        {
            CheckSourceResource(walk, concept, sourcesLine, source);
        }
    }

    private static void CheckSourceResource(BundleWalk walk, LintedConcept concept, int sourcesLine, NamedSource source)
    {
        if (FrontmatterValues.Scalar(source.Entry, "resource") is not { } resource)
        {
            walk.Report(
                OkfRules.MissingSourceResource,
                $"Source {source.Name} has no `resource`; every `sources` entry needs one (§5.1).",
                concept.Path,
                sourcesLine);
            return;
        }

        if (NamesNothingInTheBundle(walk, concept, resource))
        {
            walk.Report(
                OkfRules.UnresolvableSourceResource,
                $"Source {source.Name} points at `{resource}`, which resolves to nothing inside the bundle (§6.2).",
                concept.Path,
                sourcesLine);
        }
    }

    // §5.1 explicitly allows a scope descriptor here and §6.2 an absolute URL; neither is
    // checked, and neither is a network call (PRD CLI-16).
    private static bool NamesNothingInTheBundle(BundleWalk walk, LintedConcept concept, string resource) =>
        LintText.ClassifyResource(resource, walk.Bundle.Root, Path.GetDirectoryName(concept.Path)!)
            == SourceResource.Unresolved;

    private static IEnumerable<NamedSource> NamedSources(OkfMapping frontmatter)
    {
        int position = 0;
        foreach (OkfMapping source in Sources(frontmatter))
        {
            position++;
            yield return new NamedSource(
                source,
                FrontmatterValues.Scalar(source, "id") is { } id
                    ? $"`{id}`"
                    : $"#{position.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    // PRD CORE-8: the compensating control for cited-live sources — the source moved after
    // the concept was written, so the concept may no longer reflect it.
    private static void CheckSourceDrift(BundleWalk walk, LintedConcept concept, int sourcesLine)
    {
        DateOnly? generatedAt = Date(NestedText(concept.Frontmatter, "generated", "at"));
        if (generatedAt is null)
        {
            return;
        }

        foreach (OkfMapping source in Sources(concept.Frontmatter))
        {
            DateOnly? modified = Date(FrontmatterValues.Scalar(source, "last_modified"));
            if (modified > generatedAt)
            {
                walk.Report(
                    OkfRules.SourceDrift,
                    $"Source `{DriftedSourceName(source)}` was last modified {modified:yyyy-MM-dd}, after this concept was generated on {generatedAt:yyyy-MM-dd}.",
                    concept.Path,
                    sourcesLine);
            }
        }
    }

    private static string DriftedSourceName(OkfMapping source) =>
        FrontmatterValues.Scalar(source, "id") ?? FrontmatterValues.Scalar(source, "resource") ?? "source";

    private void CheckTrust(BundleWalk walk, LintedConcept concept)
    {
        CheckSelfVerification(walk, concept);
        CheckStaleness(walk, concept);
    }

    // decisions.md §7: an agent MAY verify, but never its own output.
    private static void CheckSelfVerification(BundleWalk walk, LintedConcept concept)
    {
        if (NestedText(concept.Frontmatter, "generated", "by") is not { } generatedBy)
        {
            return;
        }

        foreach (OkfMapping verification in OkfDocument.NormalizeVerified(concept.Frontmatter))
        {
            if (string.Equals(FrontmatterValues.Scalar(verification, "by"), generatedBy, StringComparison.Ordinal))
            {
                walk.Report(
                    OkfRules.SelfVerification,
                    $"`verified[].by` is the generating actor `{generatedBy}`; a concept must not verify itself (§5.3).",
                    concept.Path,
                    concept.Layout.FrontmatterKeyLine("verified") ?? 1);
            }
        }
    }

    private void CheckStaleness(BundleWalk walk, LintedConcept concept)
    {
        if (OkfDocument.IsStale(concept.Frontmatter, _options.Today))
        {
            walk.Report(
                OkfRules.StaleConcept,
                $"Concept is stale: `stale_after: {FrontmatterValues.Scalar(concept.Frontmatter, "stale_after")}` has passed (§5.5).",
                concept.Path,
                concept.Layout.FrontmatterKeyLine("stale_after") ?? 1);
        }
    }

    private static void CheckLinks(BundleWalk walk, string path, MarkdownScan scan)
    {
        string directory = Path.GetDirectoryName(path)!;
        foreach (MarkdownLink link in scan.Links)
        {
            CheckLink(walk, path, directory, link);
        }
    }

    private static void CheckLink(BundleWalk walk, string path, string directory, MarkdownLink link)
    {
        LinkTarget target = LintText.Resolve(link.Target, walk.Bundle.Root, directory, out string? resolved);

        if (target == LinkTarget.Outside)
        {
            ReportLinkLeavesBundle(walk, path, link);
            return;
        }

        if (target == LinkTarget.Inside && !File.Exists(resolved) && !Directory.Exists(resolved))
        {
            ReportBrokenLink(walk, path, link);
        }
    }

    /// <summary>
    /// Spec-tolerated and therefore never an error by default: §6.2 grants relative paths
    /// and says nothing about staying inside the root. But an unreported one is
    /// indistinguishable from a correct link, and a bundle that only reads correctly from
    /// inside this checkout is not portable — so it is said out loud, at info. Nothing is
    /// read: containment on the read paths (MCP-5) is a separate, harder refusal.
    /// </summary>
    private static void ReportLinkLeavesBundle(BundleWalk walk, string path, MarkdownLink link) => walk.Report(
        OkfRules.LinkLeavesBundle,
        $"Link target `{link.Target}` resolves outside the bundle root (§6.2).",
        path,
        link.Line);

    /// <summary>
    /// §6.1: consumers MUST tolerate broken links — a link may simply be not-yet-written
    /// knowledge — so this defaults to info, not warning (Q5).
    /// </summary>
    private static void ReportBrokenLink(BundleWalk walk, string path, MarkdownLink link) => walk.Report(
        OkfRules.BrokenInternalLink,
        $"Link target `{link.Target}` does not exist in the bundle (§6.1).",
        path,
        link.Line);

    /// <summary>A concept file as the rules see it: where it is, and everything already parsed out of it.</summary>
    private sealed record LintedConcept(string Path, FileLayout Layout, OkfMapping Frontmatter, MarkdownScan Scan);

    /// <summary>
    /// A <c>sources</c> entry together with the way a message names it: its <c>id</c> when
    /// it declares one, else its 1-based position in the block.
    /// </summary>
    private sealed record NamedSource(OkfMapping Entry, string Name);

    /// <summary>One file an ingested capture entry recorded, with the manifest that recorded it.</summary>
    private sealed record IngestedFile(
        OkfCaptureManifest Manifest,
        string ManifestText,
        OkfCaptureEntry Entry,
        OkfCaptureFile File);

    /// <summary>
    /// One bundle's walk: the bundle being linted, where its diagnostics go, and the state
    /// the rules that need more than the file in front of them accumulate along the way.
    /// </summary>
    private sealed class BundleWalk
    {
        private readonly List<OkfDiagnostic> _diagnostics;
        private readonly OkfSeverityResolver _severities;

        public BundleWalk(OkfBundle bundle, List<OkfDiagnostic> diagnostics, OkfSeverityResolver severities)
        {
            Bundle = bundle;
            _diagnostics = diagnostics;
            _severities = severities;
        }

        /// <summary>The bundle being walked.</summary>
        public OkfBundle Bundle { get; }

        /// <summary>
        /// Every <c>index.md</c>'s text, kept so the index-drift rule reuses this walk
        /// instead of repeating it: surfacing OKF0306 in <c>okf lint</c> therefore costs one
        /// in-memory render per directory and no extra file read or YAML parse.
        /// </summary>
        public Dictionary<string, string> IndexTexts { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Every concept's parsed frontmatter, for the same reason as <see cref="IndexTexts" />.
        /// Concept *texts* are deliberately not kept — the generator never asks for one once
        /// its frontmatter is to hand — so the walk does not accumulate the whole bundle in
        /// memory to answer one rule.
        /// </summary>
        public Dictionary<string, OkfMapping> Frontmatters { get; } =
            new Dictionary<string, OkfMapping>(StringComparer.Ordinal);

        /// <summary>The first concept seen carrying each normalized title.</summary>
        public Dictionary<string, string> TitleOwners { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The first concept seen carrying each normalized filename stem.</summary>
        public Dictionary<string, string> StemOwners { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Records a diagnostic against this bundle, at the rule's resolved severity.</summary>
        public void Report(string ruleId, string message, string path, int? line) =>
            _diagnostics.Add(new OkfDiagnostic(ruleId, _severities.Resolve(ruleId), message, path, line, Bundle.Root));
    }
}
