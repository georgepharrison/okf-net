using System.Globalization;

namespace Okf.Core;

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
    public OkfLintResult(IReadOnlyList<OkfDiagnostic> diagnostics, IReadOnlyList<OkfBundle> bundles, int fileCount)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(bundles);
        Diagnostics = diagnostics;
        Bundles = bundles;
        FileCount = fileCount;
    }

    /// <summary>Every diagnostic produced, including ones at hidden severity.</summary>
    public IReadOnlyList<OkfDiagnostic> Diagnostics { get; }

    /// <summary>The bundles that were linted.</summary>
    public IReadOnlyList<OkfBundle> Bundles { get; }

    /// <summary>How many markdown files were read.</summary>
    public int FileCount { get; }

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

        diagnostics.Sort();
        return new OkfLintResult(diagnostics, list, files);
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

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var layout = FileLayout.Of(text);
            var name = Path.GetFileName(file);

            if (string.Equals(name, OkfBundle.IndexFileName, StringComparison.Ordinal))
            {
                CheckIndexFile(bundle, file, layout, diagnostics);
            }
            else if (string.Equals(name, OkfBundle.LogFileName, StringComparison.Ordinal))
            {
                CheckLogFile(bundle, file, layout, diagnostics);
            }
            else
            {
                CheckConcept(bundle, file, layout, titles, stems, diagnostics);
            }
        }

        return files.Count;
    }

    private static string? Text(OkfMapping mapping, string key) =>
        mapping.TryGetValue(key, out var value) && value is OkfScalar scalar && scalar.IsTruthy ? scalar.Value : null;

    private static string? NestedText(OkfMapping mapping, string key, string child) =>
        mapping.TryGetValue(key, out var value) && value is OkfMapping nested ? Text(nested, child) : null;

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
        Dictionary<string, string> titles,
        Dictionary<string, string> stems,
        List<OkfDiagnostic> diagnostics)
    {
        OkfDocument document;
        try
        {
            document = OkfDocument.Parse(layout.Text);
        }
        catch (OkfDocumentException exception)
        {
            // §11.1: without a parseable frontmatter block nothing else about the file
            // can be judged, so this is the only diagnostic it produces.
            diagnostics.Add(Diagnostic(
                OkfRules.UnparseableFrontmatter,
                $"{exception.Message} (§11.1).",
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
        if (Text(frontmatter, "description") is null)
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
        if (Text(frontmatter, "title") is { } title && Normalize(title) is { Length: > 0 } normalizedTitle)
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
            if (Text(source, "id") is { } id)
            {
                sourceIds.Add(id);
            }
        }

        var cited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var footnote in scan.Footnotes)
        {
            if (!cited.Add(footnote.Label))
            {
                continue;
            }

            // §5.1: the footnote label is the join key into `sources`; a label that joins
            // to nothing attributes a claim to a source the bundle does not record.
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

        foreach (var id in sourceIds.Where(id => !cited.Contains(id)))
        {
            diagnostics.Add(Diagnostic(
                OkfRules.UnusedSourceId,
                $"Source `{id}` is never cited by a `[^{id}]` footnote in the body (§5.1).",
                path,
                sourcesLine,
                bundle));
        }

        // PRD CORE-8: the compensating control for cited-live sources — the source moved
        // after the concept was written, so the concept may no longer reflect it.
        var generatedAt = Date(NestedText(frontmatter, "generated", "at"));
        if (generatedAt is null)
        {
            return;
        }

        foreach (var source in Sources(frontmatter))
        {
            var modified = Date(Text(source, "last_modified"));
            if (modified > generatedAt)
            {
                var name = Text(source, "id") ?? Text(source, "resource") ?? "source";
                diagnostics.Add(Diagnostic(
                    OkfRules.SourceDrift,
                    $"Source `{name}` was last modified {modified:yyyy-MM-dd}, after this concept was generated on {generatedAt:yyyy-MM-dd}.",
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
                if (string.Equals(Text(verification, "by"), generatedBy, StringComparison.Ordinal))
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
                $"Concept is stale: `stale_after: {Text(frontmatter, "stale_after")}` has passed (§5.5).",
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
            if (!LintText.TryResolveLink(link.Target, bundle.Root, directory, out var resolved))
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
