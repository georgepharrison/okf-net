using System.Globalization;

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
    private readonly OkfLintOptions options;

    /// <summary>Initializes a linter.</summary>
    /// <param name="options">The severity configuration and inputs the rules need.</param>
    public OkfLinter(OkfLintOptions? options = null) => this.options = options ?? new OkfLintOptions();

    /// <summary>Lints a set of bundles.</summary>
    /// <param name="bundles">The bundles to walk.</param>
    /// <returns>The diagnostics, in deterministic order, plus run counts.</returns>
    /// <exception cref="IOException">A file in a bundle could not be read.</exception>
    public OkfLintResult Lint(IEnumerable<OkfBundle> bundles)
    {
        ArgumentNullException.ThrowIfNull(bundles);

        var list = bundles.ToList();
        var diagnostics = new List<OkfDiagnostic>();
        var files = 0;

        foreach (var bundle in list)
        {
            files += LintBundle(bundle, diagnostics);
        }

        CheckVault(diagnostics);

        diagnostics.Sort();
        return new OkfLintResult(diagnostics, list, files, this.options.Severities);
    }

    /// <summary>Lints a single bundle.</summary>
    /// <param name="bundle">The bundle to walk.</param>
    /// <returns>The diagnostics, in deterministic order, plus run counts.</returns>
    /// <exception cref="IOException">A file in the bundle could not be read.</exception>
    public OkfLintResult Lint(OkfBundle bundle) => Lint([bundle]);

    private int LintBundle(OkfBundle bundle, List<OkfDiagnostic> diagnostics)
    {
        var files = bundle.MarkdownFiles();
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var stems = new Dictionary<string, string>(StringComparer.Ordinal);

        // What the index-drift rule needs out of this walk, kept so it reuses the walk
        // instead of repeating it: every `index.md`'s text, to compare against, and every
        // concept's parsed frontmatter, to render from. Surfacing OKF0306 in `okf lint`
        // therefore costs one in-memory render per directory and no extra file read or
        // YAML parse. Only index texts are kept — the generator never asks for a concept's
        // text once its frontmatter is to hand — so the walk does not accumulate the whole
        // bundle in memory to answer one rule.
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var frontmatters = new Dictionary<string, OkfMapping>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var layout = FileLayout.Of(text);
            var name = Path.GetFileName(file);

            if (string.Equals(name, OkfBundle.IndexFileName, StringComparison.Ordinal))
            {
                texts[file] = text;
                CheckIndexFile(bundle, file, layout, diagnostics);
            }
            else if (string.Equals(name, OkfBundle.LogFileName, StringComparison.Ordinal))
            {
                CheckLogFile(bundle, file, layout, diagnostics);
            }
            else
            {
                OkfDocument? document = null;
                OkfDocumentException? failure = null;
                try
                {
                    document = OkfDocument.Parse(layout.Text);
                }
                catch (OkfDocumentException exception)
                {
                    failure = exception;
                }

                if (document is not null)
                {
                    frontmatters[file] = document.Frontmatter;
                }

                CheckConcept(bundle, file, layout, document, failure, titles, stems, diagnostics);
            }
        }

        CheckGeneratedIndexes(bundle, files, texts, frontmatters, diagnostics);

        return files.Count;
    }

    private void CheckGeneratedIndexes(
        OkfBundle bundle,
        IReadOnlyList<string> files,
        Dictionary<string, string> texts,
        Dictionary<string, OkfMapping> frontmatters,
        List<OkfDiagnostic> diagnostics)
    {
        if (this.options.Severities.Resolve(OkfRules.GeneratedIndexDrift) == OkfSeverity.Hidden)
        {
            // Nothing would be reported, so nothing is rendered.
            return;
        }

        var plan = OkfIndexGenerator.Plan(
            bundle,
            new OkfIndexOptions
            {
                Files = files,
                ReadText = path => texts.GetValueOrDefault(path),
                ReadFrontmatter = path => frontmatters.GetValueOrDefault(path),
            });

        foreach (var index in plan.Drift)
        {
            // Only indexes okf-net wrote can drift: a foreign bundle's hand-styled index
            // carries no marker and is left alone, which is what keeps PRD ACC-1 passing
            // on Google's reference bundles.
            var message = index.Status == OkfIndexStatus.Orphaned
                ? "This generated index.md describes a directory with nothing left to index; delete it or add concepts (§8)."
                : "This generated index.md no longer matches the directory; run `okf index` to regenerate it (§8).";

            diagnostics.Add(Diagnostic(OkfRules.GeneratedIndexDrift, message, index.Path, 1, bundle));
        }
    }

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
        if (this.options.VaultRoot is not { Length: > 0 } vault
            || this.options.Severities.Resolve(OkfRules.RawItemMutated) == OkfSeverity.Hidden)
        {
            // No vault, or nothing would be reported — so nothing is read and nothing is
            // hashed. Hashing every ingested artifact is the most expensive thing a lint
            // run does, and a hidden rule must not cost it.
            return;
        }

        var manifest = OkfCaptureManifest.TryLoad(vault, out var text);
        if (manifest is null || text is null)
        {
            return;
        }

        var rawDirectory = Path.Combine(vault, OkfCaptureManifest.RawDirectoryName);

        foreach (var entry in manifest.Captures.Where(capture => capture.IsIngested))
        {
            foreach (var file in entry.Files)
            {
                CheckRawFile(manifest, text, rawDirectory, entry, file, diagnostics);
            }
        }
    }

    private void CheckRawFile(
        OkfCaptureManifest manifest,
        string manifestText,
        string rawDirectory,
        OkfCaptureEntry entry,
        OkfCaptureFile file,
        List<OkfDiagnostic> diagnostics)
    {
        if (!TryResolveRawPath(rawDirectory, file.Path, out var absolute))
        {
            // A recorded path that escapes raw/ is a malformed record, which is
            // check-manifest.py's finding to report; reading the file it names is exactly
            // what this rule must not do.
            return;
        }

        if (!File.Exists(absolute))
        {
            diagnostics.Add(VaultDiagnostic(
                $"`{file.Path}`, ingested under capture `{entry.Id}`, is no longer in raw/. " +
                "An ingested artifact is the evidence a concept rests on; restore it rather than editing the manifest.",
                manifest.Path,
                LineOf(manifestText, file.Sha256)));
            return;
        }

        string actual;
        try
        {
            actual = OkfCaptureManifest.Sha256Of(absolute);
        }
        catch (IOException)
        {
            // An unreadable artifact is not a changed one, and lint never fails a run on
            // what it could not open.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(VaultDiagnostic(
                $"`{file.Path}`, ingested under capture `{entry.Id}`, no longer matches its recorded sha256 " +
                $"(recorded {OkfCaptureManifest.Short(file.Sha256)}, on disk {OkfCaptureManifest.Short(actual)}). " +
                "The artifact changed after ingestion, " +
                "or the record did; resolve it by hand, never by rewriting the manifest.",
                manifest.Path,
                LineOf(manifestText, file.Sha256)));
        }
    }

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

        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(rawDirectory, recorded)));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawDirectory));

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
        var lines = manifestText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return null;
    }

    private OkfDiagnostic VaultDiagnostic(string message, string path, int? line) =>
        new(OkfRules.RawItemMutated, this.options.Severities.Resolve(OkfRules.RawItemMutated), message, path, line);

    private static string? NestedText(OkfMapping mapping, string key, string child) =>
        mapping.TryGetValue(key, out var value) && value is OkfMapping nested
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
            out var date)
            ? date
            : null;
    }

    private static string Normalize(string text)
    {
        var normalized = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.ToString();
    }

    private static IEnumerable<OkfMapping> Sources(OkfMapping frontmatter) =>
        frontmatter.TryGetValue("sources", out var value) && value is OkfSequence sequence
            ? sequence.OfType<OkfMapping>()
            : [];

    private static IEnumerable<string> Tags(OkfMapping frontmatter)
    {
        if (!frontmatter.TryGetValue("tags", out var value))
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

    private OkfDiagnostic Diagnostic(string ruleId, string message, string path, int? line, OkfBundle bundle) =>
        new(ruleId, this.options.Severities.Resolve(ruleId), message, path, line, bundle.Root);

    private void CheckIndexFile(OkfBundle bundle, string path, FileLayout layout, List<OkfDiagnostic> diagnostics)
    {
        // §8: index files carry no frontmatter, with one exception — a bundle-root
        // index.md MAY declare `okf_version` (§12).
        var isRoot = string.Equals(
            Path.GetDirectoryName(path),
            bundle.Root,
            StringComparison.Ordinal);

        if (layout.HasFrontmatter)
        {
            OkfMapping? frontmatter = null;
            try
            {
                frontmatter = OkfDocument.Parse(layout.Text).Frontmatter;
            }
            catch (OkfDocumentException exception)
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.InvalidIndexStructure,
                    $"index.md has a malformed frontmatter block: {exception.Message} (§8).",
                    path,
                    1,
                    bundle));
            }

            if (frontmatter is not null && !isRoot)
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.InvalidIndexStructure,
                    "index.md must not carry frontmatter; only a bundle-root index.md may, and only `okf_version` (§8, §12).",
                    path,
                    1,
                    bundle));
            }
            else if (frontmatter is not null)
            {
                foreach (var entry in frontmatter)
                {
                    if (entry.Key is OkfScalar key && !string.Equals(key.Value, "okf_version", StringComparison.Ordinal))
                    {
                        diagnostics.Add(Diagnostic(
                            OkfRules.InvalidIndexStructure,
                            $"A bundle-root index.md may only carry `okf_version` in frontmatter; found `{key.Value}` (§8, §12).",
                            path,
                            layout.FrontmatterKeyLine(key.Value) ?? 1,
                            bundle));
                    }
                }
            }
        }

        var scan = MarkdownScanner.Scan(layout.Body, layout.BodyFirstLine);
        if (scan.HasContent && scan.Headings.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                OkfRules.InvalidIndexStructure,
                "index.md has no `#` section heading; entries are grouped under headings (§8).",
                path,
                layout.BodyFirstLine,
                bundle));
        }

        foreach (var bullet in scan.Bullets)
        {
            if (!LintText.IsIndexEntry(bullet.Text))
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.InvalidIndexStructure,
                    $"index.md entry is not of the form `* [Title](link) - description` (§8): `{bullet.Text}`.",
                    path,
                    bullet.Line,
                    bundle));
            }
        }

        CheckLinks(bundle, path, scan, diagnostics);
    }

    private void CheckLogFile(OkfBundle bundle, string path, FileLayout layout, List<OkfDiagnostic> diagnostics)
    {
        if (layout.HasFrontmatter)
        {
            try
            {
                OkfDocument.Parse(layout.Text);
            }
            catch (OkfDocumentException exception)
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.InvalidLogStructure,
                    $"log.md has a malformed frontmatter block: {exception.Message} (§9).",
                    path,
                    1,
                    bundle));
            }
        }

        var scan = MarkdownScanner.Scan(layout.Body, layout.BodyFirstLine);
        DateOnly? previous = null;

        // §9: `##` headings are ISO YYYY-MM-DD dates, newest first. A `#` title above
        // them is conventional, and entry prose is unconstrained.
        foreach (var heading in scan.Headings.Where(heading => heading.Level == 2))
        {
            var date = Date(heading.Text);
            if (date is null)
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.InvalidLogStructure,
                    $"log.md date heading `## {heading.Text}` is not an ISO YYYY-MM-DD date (§9).",
                    path,
                    heading.Line,
                    bundle));
                continue;
            }

            if (previous is not null && date > previous)
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.InvalidLogStructure,
                    $"log.md entries must be newest first: `## {heading.Text}` follows `## {previous:yyyy-MM-dd}` (§9).",
                    path,
                    heading.Line,
                    bundle));
            }

            previous = date;
        }

        CheckLinks(bundle, path, scan, diagnostics);
    }

    private void CheckConcept(
        OkfBundle bundle,
        string path,
        FileLayout layout,
        OkfDocument? document,
        OkfDocumentException? failure,
        Dictionary<string, string> titles,
        Dictionary<string, string> stems,
        List<OkfDiagnostic> diagnostics)
    {
        if (document is null)
        {
            // §11.1: without a parseable frontmatter block nothing else about the file
            // can be judged, so this is the only diagnostic it produces.
            diagnostics.Add(Diagnostic(
                OkfRules.UnparseableFrontmatter,
                $"{failure!.Message} (§11.1).",
                path,
                1,
                bundle));
            return;
        }

        if (!layout.HasFrontmatter)
        {
            // §11.1 asks for a frontmatter *block*; a file without one parses fine (the
            // whole text is body) but is not a concept, so report the missing block
            // rather than the `type` it could not have carried.
            diagnostics.Add(Diagnostic(
                OkfRules.UnparseableFrontmatter,
                "File has no YAML frontmatter block; every non-reserved .md file is a concept (§11.1).",
                path,
                1,
                bundle));
            return;
        }

        var frontmatter = document.Frontmatter;

        try
        {
            document.Validate();
        }
        catch (OkfDocumentException exception)
        {
            diagnostics.Add(Diagnostic(
                OkfRules.MissingType,
                $"{exception.Message} (§11.2).",
                path,
                layout.FrontmatterKeyLine("type") ?? 1,
                bundle));
        }

        var scan = MarkdownScanner.Scan(document.Body, layout.BodyFirstLine);

        CheckHygiene(bundle, path, layout, frontmatter, titles, stems, diagnostics);
        CheckProvenance(bundle, path, layout, frontmatter, scan, diagnostics);
        CheckTrust(bundle, path, layout, frontmatter, diagnostics);
        CheckLinks(bundle, path, scan, diagnostics);
    }

    private void CheckHygiene(
        OkfBundle bundle,
        string path,
        FileLayout layout,
        OkfMapping frontmatter,
        Dictionary<string, string> titles,
        Dictionary<string, string> stems,
        List<OkfDiagnostic> diagnostics)
    {
        if (FrontmatterValues.Scalar(frontmatter, "description") is null)
        {
            diagnostics.Add(Diagnostic(
                OkfRules.MissingDescription,
                "Concept has no `description`; index entries and search results degrade without one (§4.1).",
                path,
                1,
                bundle));
        }

        var tags = Tags(frontmatter).ToList();
        if (tags.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                OkfRules.MissingTags,
                "Concept has no `tags` (§4.1).",
                path,
                1,
                bundle));
        }
        else if (this.options.TagRegistry is { } registry)
        {
            foreach (var tag in tags.Where(tag => !registry.Contains(tag, StringComparer.Ordinal)))
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.UnregisteredTag,
                    $"Tag `{tag}` is not in the bundle's tag registry.",
                    path,
                    layout.FrontmatterKeyLine("tags") ?? 1,
                    bundle));
            }
        }

        // Near-duplicate detection, MVP heuristic (PRD Q8): a title or filename collision
        // up to case and punctuation. Vectorization would do better and is post-MVP.
        //
        // Convention-bearing filenames are exempt from both arms (the Q1/Q8
        // reconciliation). `about.md` is the designated carrier of a subdirectory's
        // description, so a well-maintained bundle holds one per subdirectory; neither
        // that name nor the generic title that usually accompanies it says anything about
        // the content, and reporting them would punish following the convention. Such a
        // file neither reports a collision nor seeds one for a later file.
        if (OkfBundle.IsConventionalFile(path))
        {
            return;
        }

        var reportedAgainst = (string?)null;
        if (FrontmatterValues.Scalar(frontmatter, "title") is { } title && Normalize(title) is { Length: > 0 } normalizedTitle)
        {
            if (titles.TryGetValue(normalizedTitle, out var first))
            {
                reportedAgainst = first;
                diagnostics.Add(Diagnostic(
                    OkfRules.NearDuplicateConcept,
                    $"Concept title duplicates `{bundle.RelativePath(first)}` up to case and punctuation.",
                    path,
                    layout.FrontmatterKeyLine("title") ?? 1,
                    bundle));
            }
            else
            {
                titles[normalizedTitle] = path;
            }
        }

        var stem = Normalize(Path.GetFileNameWithoutExtension(path));
        if (stem.Length == 0)
        {
            return;
        }

        if (stems.TryGetValue(stem, out var firstByStem))
        {
            if (!string.Equals(firstByStem, reportedAgainst, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.NearDuplicateConcept,
                    $"Concept filename duplicates `{bundle.RelativePath(firstByStem)}` up to case and punctuation.",
                    path,
                    1,
                    bundle));
            }
        }
        else
        {
            stems[stem] = path;
        }
    }

    private void CheckProvenance(
        OkfBundle bundle,
        string path,
        FileLayout layout,
        OkfMapping frontmatter,
        MarkdownScan scan,
        List<OkfDiagnostic> diagnostics)
    {
        var sourcesLine = layout.FrontmatterKeyLine("sources") ?? 1;
        var sourceIds = new List<string>();
        foreach (var source in Sources(frontmatter))
        {
            if (FrontmatterValues.Scalar(source, "id") is { } id)
            {
                sourceIds.Add(id);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Only a footnote *reference* counts as a citation. A `[^id]: …` definition line is
        // the source's own entry in the notes, not a claim attributed to it, so a
        // definition with no reference above it is exactly the shape of a source that was
        // listed and then never used (the first dogfood bundle shipped one, and only
        // markdownlint's MD053 caught it — decisions.md, lint review flags).
        var referenced = new HashSet<string>(
            scan.Footnotes.Where(footnote => !footnote.IsDefinition).Select(footnote => footnote.Label),
            StringComparer.Ordinal);

        foreach (var footnote in scan.Footnotes)
        {
            if (!seen.Add(footnote.Label))
            {
                continue;
            }

            // §5.1: the footnote label is the join key into `sources`; a label that joins
            // to nothing attributes a claim to a source the bundle does not record. Both
            // occurrence forms are held to it — a definition for a label no source
            // declares is as dangling as a reference to one.
            if (!sourceIds.Contains(footnote.Label, StringComparer.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.UncitedFootnote,
                    $"Footnote `[^{footnote.Label}]` matches no `sources[].id` (§5.1).",
                    path,
                    footnote.Line,
                    bundle));
            }
        }

        var defined = new HashSet<string>(
            scan.Footnotes.Where(footnote => footnote.IsDefinition).Select(footnote => footnote.Label),
            StringComparer.Ordinal);

        foreach (var id in sourceIds.Where(id => !referenced.Contains(id)))
        {
            // The two shapes read very differently to the author. "Never cited by a
            // `[^id]` footnote" is false on its face when a `[^id]:` line is sitting in
            // the document — which is precisely the case this rule newly reports — so the
            // definition-only finding says what is actually missing instead.
            diagnostics.Add(Diagnostic(
                OkfRules.UnusedSourceId,
                defined.Contains(id)
                    ? $"Source `{id}` has a `[^{id}]:` footnote definition but is never referenced by a "
                        + $"`[^{id}]` in the body; a definition is the note, not a citation (§5.1)."
                    : $"Source `{id}` is never cited by a `[^{id}]` footnote in the body (§5.1).",
                path,
                sourcesLine,
                bundle));
        }

        CheckSourceResources(bundle, path, sourcesLine, frontmatter, diagnostics);

        // PRD CORE-8: the compensating control for cited-live sources — the source moved
        // after the concept was written, so the concept may no longer reflect it.
        var generatedAt = Date(NestedText(frontmatter, "generated", "at"));
        if (generatedAt is null)
        {
            return;
        }

        foreach (var source in Sources(frontmatter))
        {
            var modified = Date(FrontmatterValues.Scalar(source, "last_modified"));
            if (modified > generatedAt)
            {
                var name = FrontmatterValues.Scalar(source, "id") ?? FrontmatterValues.Scalar(source, "resource") ?? "source";
                diagnostics.Add(Diagnostic(
                    OkfRules.SourceDrift,
                    $"Source `{name}` was last modified {modified:yyyy-MM-dd}, after this concept was generated on {generatedAt:yyyy-MM-dd}.",
                    path,
                    sourcesLine,
                    bundle));
            }
        }
    }

    /// <summary>
    /// §5.1's `resource` is REQUIRED within a `sources` entry, and §6.2 fixes the forms a
    /// path-valued one may take. Neither is §11 conformance, so neither errors by default:
    /// a missing pointer is a provenance record that cannot be followed (warning), and a
    /// path that names nothing in the bundle is reported the way a broken link is (info),
    /// because the same "the target may simply not be here" tolerance applies.
    /// </summary>
    private void CheckSourceResources(
        OkfBundle bundle,
        string path,
        int sourcesLine,
        OkfMapping frontmatter,
        List<OkfDiagnostic> diagnostics)
    {
        var directory = Path.GetDirectoryName(path)!;
        var position = 0;

        foreach (var source in Sources(frontmatter))
        {
            position++;
            var name = FrontmatterValues.Scalar(source, "id") is { } id
                ? $"`{id}`"
                : $"#{position.ToString(CultureInfo.InvariantCulture)}";

            if (FrontmatterValues.Scalar(source, "resource") is not { } resource)
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.MissingSourceResource,
                    $"Source {name} has no `resource`; every `sources` entry needs one (§5.1).",
                    path,
                    sourcesLine,
                    bundle));
                continue;
            }

            // §5.1 explicitly allows a scope descriptor here and §6.2 an absolute URL;
            // neither is checked, and neither is a network call (PRD CLI-16).
            if (LintText.ClassifyResource(resource, bundle.Root, directory) == SourceResource.Unresolved)
            {
                diagnostics.Add(Diagnostic(
                    OkfRules.UnresolvableSourceResource,
                    $"Source {name} points at `{resource}`, which resolves to nothing inside the bundle (§6.2).",
                    path,
                    sourcesLine,
                    bundle));
            }
        }
    }

    private void CheckTrust(
        OkfBundle bundle,
        string path,
        FileLayout layout,
        OkfMapping frontmatter,
        List<OkfDiagnostic> diagnostics)
    {
        var generatedBy = NestedText(frontmatter, "generated", "by");
        if (generatedBy is not null)
        {
            foreach (var verification in OkfDocument.NormalizeVerified(frontmatter))
            {
                if (string.Equals(FrontmatterValues.Scalar(verification, "by"), generatedBy, StringComparison.Ordinal))
                {
                    // decisions.md §7: an agent MAY verify, but never its own output.
                    diagnostics.Add(Diagnostic(
                        OkfRules.SelfVerification,
                        $"`verified[].by` is the generating actor `{generatedBy}`; a concept must not verify itself (§5.3).",
                        path,
                        layout.FrontmatterKeyLine("verified") ?? 1,
                        bundle));
                }
            }
        }

        if (OkfDocument.IsStale(frontmatter, this.options.Today))
        {
            diagnostics.Add(Diagnostic(
                OkfRules.StaleConcept,
                $"Concept is stale: `stale_after: {FrontmatterValues.Scalar(frontmatter, "stale_after")}` has passed (§5.5).",
                path,
                layout.FrontmatterKeyLine("stale_after") ?? 1,
                bundle));
        }
    }

    private void CheckLinks(OkfBundle bundle, string path, MarkdownScan scan, List<OkfDiagnostic> diagnostics)
    {
        var directory = Path.GetDirectoryName(path)!;

        foreach (var link in scan.Links)
        {
            var target = LintText.Resolve(link.Target, bundle.Root, directory, out var resolved);

            if (target == LinkTarget.Outside)
            {
                // Spec-tolerated and therefore never an error by default: §6.2 grants
                // relative paths and says nothing about staying inside the root. But an
                // unreported one is indistinguishable from a correct link, and a bundle
                // that only reads correctly from inside this checkout is not portable —
                // so it is said out loud, at info. Nothing is read: containment on the
                // read paths (MCP-5) is a separate, harder refusal.
                diagnostics.Add(Diagnostic(
                    OkfRules.LinkLeavesBundle,
                    $"Link target `{link.Target}` resolves outside the bundle root (§6.2).",
                    path,
                    link.Line,
                    bundle));
                continue;
            }

            if (target != LinkTarget.Inside)
            {
                continue;
            }

            if (File.Exists(resolved) || Directory.Exists(resolved))
            {
                continue;
            }

            // §6.1: consumers MUST tolerate broken links — a link may simply be
            // not-yet-written knowledge — so this defaults to info, not warning (Q5).
            diagnostics.Add(Diagnostic(
                OkfRules.BrokenInternalLink,
                $"Link target `{link.Target}` does not exist in the bundle (§6.1).",
                path,
                link.Line,
                bundle));
        }
    }
}
