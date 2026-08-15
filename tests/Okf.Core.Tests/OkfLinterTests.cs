namespace Okf.Core.Tests;

/// <summary>
/// One test per rule, each over a bundle built for it. Conformance rules (OKF00xx) are
/// the only ones that error by default (PRD CLI-5).
/// </summary>
public class OkfLinterTests
{
    private const string CleanConcept = """
        ---
        type: Reference
        title: Clean
        description: A concept that trips nothing.
        tags: [fixture]
        ---

        # Clean
        """;

    [Fact]
    public void ACleanBundleProducesNoDiagnosticsAtAll()
    {
        using var bundle = new TempBundle();
        bundle.Add("clean.md", CleanConcept);

        Assert.Empty(bundle.Lint());
    }

    [Fact]
    public void NonMarkdownFilesAndDotDirectoriesAreIgnored()
    {
        using var bundle = new TempBundle();
        bundle.Add("clean.md", CleanConcept)
            .Add("viz.html", "<html>not a concept</html>")
            .Add("attesters/sql_equality.py", "# not a concept")
            .Add(".git/config", "[core]");

        var result = new OkfLinter(new OkfLintOptions { Today = TempBundle.Today }).Lint(bundle.Bundle);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.FileCount);
    }

    [SkippableFact]
    public void ASymlinkedSubdirectoryIsNotWalked()
    {
        using var bundle = new TempBundle();
        bundle.Add("sub/clean.md", CleanConcept);

        using var outside = new TempBundle("outside");
        outside.Add("stranger.md", CleanConcept);

        // `back` is a cycle: descending it re-lints `sub/clean.md` under an ever-longer
        // path until the OS refuses, reporting the same file dozens of times as a
        // near-duplicate of itself. `elsewhere` escapes the bundle root entirely.
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(bundle.Root, "sub", "back"), bundle.Root);
            Directory.CreateSymbolicLink(Path.Combine(bundle.Root, "elsewhere"), outside.Root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows needs Developer Mode or elevation to create a directory symlink.
            throw new SkipException($"This platform will not create directory symlinks: {exception.Message}");
        }

        var result = new OkfLinter(new OkfLintOptions { Today = TempBundle.Today }).Lint(bundle.Bundle);

        Assert.Equal(1, result.FileCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void OKF0001FiresForAMissingOrUnparseableFrontmatterBlock()
    {
        using var bundle = new TempBundle();
        bundle.Add("none.md", "# No frontmatter\n")
            .Add("unterminated.md", "---\ntype: Reference\n")
            .Add("invalid.md", "---\ntype: [oops\n---\n\n# Body\n")
            .Add("not-a-mapping.md", "---\njust a scalar\n---\n\n# Body\n");

        var diagnostics = bundle.Lint();

        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal(OkfRules.UnparseableFrontmatter, diagnostic.RuleId));
        Assert.All(diagnostics, diagnostic => Assert.Equal(OkfSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void OKF0002FiresForAnEmptyType()
    {
        using var bundle = new TempBundle();
        bundle.Add("empty.md", "---\ntitle: No type\ndescription: d\ntags: [t]\n---\n\n# Body\n")
            .Add("null.md", "---\ntype:\ndescription: d\ntags: [t]\n---\n\n# Body\n")
            .Add("falsy.md", "---\ntype: false\ndescription: d\ntags: [t]\n---\n\n# Body\n");

        var diagnostics = bundle.Lint();

        // `type: false` counts as missing: okf-net follows the reference implementation's
        // truthiness over a literal reading of §11 (decisions.md, port-review proposal).
        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal(OkfRules.MissingType, diagnostic.RuleId));
        Assert.Equal(2, diagnostics[1].Line);
    }

    [Fact]
    public void OKF0003FiresForIndexFilesThatBreakSection8()
    {
        using var bundle = new TempBundle();
        bundle.Add("index.md", "---\nokf_version: \"0.2\"\n---\n\n# Bundle\n\n* [Sub](sub/index.md) - fine\n")
            .Add("sub/index.md", "---\ntype: Index\n---\n\n# Sub\n\n* loose prose bullet\n");

        var ids = bundle.LintIds();

        // A bundle-root index MAY carry okf_version (§12); a nested one may carry nothing,
        // and every bullet must be an entry.
        Assert.Equal([OkfRules.InvalidIndexStructure, OkfRules.InvalidIndexStructure], ids);
    }

    [Fact]
    public void AnIndexWithNoHeadingIsReportedButAnEmptyOneIsNot()
    {
        using var bundle = new TempBundle();
        bundle.Add("index.md", "* [Entry](entry.md) - no heading above it\n")
            .Add("entry.md", CleanConcept)
            .Add("sub/index.md", "\n");

        var diagnostics = bundle.Lint();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(OkfRules.InvalidIndexStructure, diagnostic.RuleId);
        Assert.EndsWith("index.md", diagnostic.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void OKF0004FiresForLogFilesThatBreakSection9()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "log.md",
            """
            # History

            ## Yesterday

            - **Note**: not an ISO date.

            ## 2026-01-01

            - **Initialization**: created.

            ## 2026-02-01

            - **Update**: out of order.
            """);

        var diagnostics = bundle.Lint();

        Assert.Equal([OkfRules.InvalidLogStructure, OkfRules.InvalidLogStructure], diagnostics.Select(d => d.RuleId));
        Assert.All(diagnostics, diagnostic => Assert.Equal(OkfSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void AConventionalLogPassesIncludingItsFrontmatter()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "log.md",
            """
            ---
            type: Log
            title: History
            ---

            # History

            ## 2026-02-01

            - **Update**: newest first.

            ## 2026-01-01

            - **Initialization**: created.
            """);

        Assert.Empty(bundle.Lint());
    }

    [Fact]
    public void ReservedFilesAreNotHeldToTheConceptRules()
    {
        using var bundle = new TempBundle();
        bundle.Add("index.md", "# Bundle\n\n* [Clean](clean.md) - a concept.\n")
            .Add("log.md", "# History\n\n## 2026-01-01\n\n- **Initialization**: created.\n")
            .Add("clean.md", CleanConcept);

        // No missing-type, missing-description, or missing-tags for index.md and log.md.
        Assert.Empty(bundle.Lint());
    }

    [Fact]
    public void OKF0101AndOKF0102CheckTheFootnoteToSourceJoin()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "concept.md",
            """
            ---
            type: Reference
            title: Citations
            description: d
            tags: [t]
            sources:
              - id: cited
                resource: https://example.invalid/one
              - id: uncited
                resource: https://example.invalid/two
            ---

            # Citations

            A cited claim.[^cited] A dangling one.[^dangling]

            [^cited]: One
            """);

        var diagnostics = bundle.Lint();

        Assert.Equal([OkfRules.UncitedFootnote, OkfRules.UnusedSourceId], diagnostics.Select(d => d.RuleId).Order());
        Assert.Contains("dangling", diagnostics.First(d => d.RuleId == OkfRules.UncitedFootnote).Message, StringComparison.Ordinal);
        Assert.Contains("uncited", diagnostics.First(d => d.RuleId == OkfRules.UnusedSourceId).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OKF0102TreatsOnlyAFootnoteReferenceAsACitation()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "citations.md",
            """
            ---
            type: Reference
            title: Citations
            description: d
            tags: [t]
            sources:
              - id: defined-only
                resource: https://example.invalid/one
              - id: referenced
                resource: https://example.invalid/two
              - id: on-its-own-line
                resource: https://example.invalid/three
            ---

            # Citations

            A cited claim.[^referenced]

            A block quoted from elsewhere.

            [^on-its-own-line]

            [^defined-only]: Never referenced above.
            [^referenced]: One.
            [^on-its-own-line]: Three.
            """);

        var diagnostics = bundle.Lint();

        // §5.1: `id` attributes individual claims, so the citation is the `[^id]` in the
        // prose. A `[^id]: …` definition line is the note itself — a source that has only
        // one is listed, footnoted, and never actually used, which is the same finding
        // markdownlint reports as MD053 (unused link reference definition).
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(OkfRules.UnusedSourceId, diagnostic.RuleId);
        Assert.Contains("`defined-only`", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OKF0103FiresWhenASourceMovedAfterGeneration()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "drifted.md",
            """
            ---
            type: Reference
            title: Drifted
            description: d
            tags: [t]
            generated: { by: okf-net/tests, at: 2026-01-01T00:00:00Z }
            sources:
              - id: moved
                resource: https://example.invalid/one
                last_modified: 2026-02-01
              - id: still
                resource: https://example.invalid/two
                last_modified: 2025-12-31
            ---

            # Drifted

            Claims.[^moved][^still]
            """);

        var diagnostic = Assert.Single(bundle.Lint(), d => d.RuleId == OkfRules.SourceDrift);

        Assert.Contains("moved", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(OkfSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void OKF0201FiresWhenTheGeneratingActorVerifiesItsOwnWork()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "self.md",
            """
            ---
            type: Reference
            title: Self
            description: d
            tags: [t]
            generated: { by: okf-net/tests, at: 2026-01-01T00:00:00Z }
            verified: { by: okf-net/tests, at: 2026-01-02T00:00:00Z }
            ---

            # Self
            """)
            .Add(
            "other.md",
            """
            ---
            type: Reference
            title: Other
            description: d
            tags: [t]
            generated: { by: okf-net/tests, at: 2026-01-01T00:00:00Z }
            verified: { by: human:reviewer, at: 2026-01-02T00:00:00Z }
            ---

            # Other
            """);

        var diagnostic = Assert.Single(bundle.Lint());

        // The bare-mapping `verified` form is normalized before the check (§5.2).
        Assert.Equal(OkfRules.SelfVerification, diagnostic.RuleId);
        Assert.EndsWith("self.md", diagnostic.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void OKF0202FiresOnceTheStaleAfterDateHasPassed()
    {
        using var bundle = new TempBundle();
        bundle.Add("stale.md", Concept("stale", "stale_after: 2026-01-01"))
            .Add("fresh.md", Concept("fresh", "stale_after: 2026-12-31"));

        var diagnostic = Assert.Single(bundle.Lint());

        Assert.Equal(OkfRules.StaleConcept, diagnostic.RuleId);
        Assert.EndsWith("stale.md", diagnostic.Path, StringComparison.Ordinal);

        // The comparison date is injected, so the rule is deterministic (PRD CORE-7).
        Assert.Empty(bundle.Lint(new OkfLintOptions { Today = new DateOnly(2025, 1, 1) }));
    }

    [Fact]
    public void OKF0301AndOKF0304CoverTheOptionalHygieneFields()
    {
        using var bundle = new TempBundle();
        bundle.Add("bare.md", "---\ntype: Reference\ntitle: Bare\n---\n\n# Bare\n");

        var diagnostics = bundle.Lint();

        Assert.Equal(
            [OkfRules.MissingDescription, OkfRules.MissingTags],
            diagnostics.Select(d => d.RuleId).Order());
        Assert.Equal(OkfSeverity.Warning, diagnostics.Single(d => d.RuleId == OkfRules.MissingDescription).Severity);

        // Missing tags is computed but hidden until a consumer opts in (decisions.md §7).
        Assert.Equal(OkfSeverity.Hidden, diagnostics.Single(d => d.RuleId == OkfRules.MissingTags).Severity);
    }

    [Fact]
    public void OKF0302ResolvesBothLinkFormsAgainstTheBundleTree()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/orders.md", CleanConcept)
            .Add(
                "metrics/revenue.md",
                """
                ---
                type: Metric
                title: Revenue
                description: d
                tags: [t]
                ---

                # Revenue

                Rooted [orders](/tables/orders.md), relative [orders](../tables/orders.md),
                external [docs](https://example.invalid/x), missing [gone](/tables/gone.md).
                """);

        var diagnostic = Assert.Single(bundle.Lint());

        Assert.Equal(OkfRules.BrokenInternalLink, diagnostic.RuleId);
        Assert.Equal(OkfSeverity.Info, diagnostic.Severity);
        Assert.Contains("/tables/gone.md", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OKF0307FiresWhenASourceEntryHasNoResource()
    {
        using var bundle = new TempBundle();
        bundle.Add(
            "sourced.md",
            """
            ---
            type: Reference
            title: Sourced
            description: d
            tags: [t]
            sources:
              - id: no-resource
                title: A source nobody can follow
              - resource: ""
                title: An empty one
              - id: fine
                resource: https://example.invalid/one
            ---

            # Sourced

            Claims.[^no-resource][^fine]
            """);

        var diagnostics = bundle.Lint().Where(d => d.RuleId == OkfRules.MissingSourceResource).ToList();

        // §5.1: `resource` is REQUIRED within an entry. The unnamed entry is reported by
        // its position, since it has no id to name it by.
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal(OkfSeverity.Warning, diagnostic.Severity));
        Assert.All(diagnostics, diagnostic => Assert.Equal(6, diagnostic.Line));
        Assert.Contains(diagnostics, d => d.Message.Contains("`no-resource`", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Message.Contains("#2", StringComparison.Ordinal));
    }

    [Fact]
    public void OKF0308OnlyJudgesResourceValuesThatReadAsPaths()
    {
        using var bundle = new TempBundle();
        bundle.Add("policies/margin-standard.md", CleanConcept)
            .Add(
                "metrics/margin.md",
                """
                ---
                type: Metric
                title: Margin
                description: d
                tags: [t]
                sources:
                  - id: url
                    resource: https://example.invalid/one
                  - id: scope
                    resource: all queries in BigQuery project X
                  - id: descriptor
                    resource: dashboards/exec-margin
                  - id: rooted
                    resource: /policies/margin-standard.md
                  - id: root-relative
                    resource: policies/margin-standard.md
                  - id: typo
                    resource: ../policies/margin-standrd.md
                  - id: escapes
                    resource: ../../elsewhere/margin.md
                ---

                # Margin

                Claims.[^url][^scope][^descriptor][^rooted][^root-relative][^typo][^escapes]
                """);

        var diagnostics = bundle.Lint().Where(d => d.RuleId == OkfRules.UnresolvableSourceResource).ToList();

        // §5.1 allows a scope descriptor and §6.2 an absolute URL, so neither is checked;
        // `dashboards/exec-margin` is SPEC §5.1's own descriptor-shaped example and is left
        // alone too. What is left: a misspelled path and one that leaves the bundle.
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal(OkfSeverity.Info, diagnostic.Severity));
        Assert.Contains(
            diagnostics,
            d => d.Message.Contains("`typo`", StringComparison.Ordinal)
                && d.Message.Contains("margin-standrd.md", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Message.Contains("`escapes`", StringComparison.Ordinal));
    }

    [Fact]
    public void OKF0309ReportsALinkThatResolvesOutsideTheBundleRoot()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/orders.md", CleanConcept)
            .Add(
                "metrics/revenue.md",
                """
                ---
                type: Metric
                title: Revenue
                description: d
                tags: [t]
                ---

                # Revenue

                Inside [orders](../tables/orders.md), outside [decisions](../../../docs/decisions.md),
                external [docs](https://example.invalid/x), anchor [here](#revenue).
                """);

        var diagnostic = Assert.Single(bundle.Lint());

        // A URL and an in-page anchor are not paths and say nothing about the filesystem;
        // a path that leaves the root is spec-tolerated, so it is info rather than an
        // error — but it is no longer silent (friction #4).
        Assert.Equal(OkfRules.LinkLeavesBundle, diagnostic.RuleId);
        Assert.Equal(OkfSeverity.Info, diagnostic.Severity);
        Assert.Contains("../../../docs/decisions.md", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(10, diagnostic.Line);
    }

    [Fact]
    public void OKF0303CatchesTitleAndFilenameCollisions()
    {
        using var bundle = new TempBundle();
        bundle.Add("a.md", Concept("Revenue"))
            .Add("b.md", Concept("revenue!"))
            .Add("sub/a.md", Concept("Something else"));

        var diagnostics = bundle.Lint();

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal(OkfRules.NearDuplicateConcept, diagnostic.RuleId));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("title", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("filename", StringComparison.Ordinal));
    }

    [Fact]
    public void OKF0303ExemptsTheConventionalAboutFilename()
    {
        // Q1 makes `<subdir>/about.md` the designated carrier of a subdirectory's
        // description, so one per subdirectory is the convention working, not a
        // near-duplicate. Both arms are exempt: the shared filename and the generic
        // title that usually goes with it.
        using var bundle = new TempBundle();
        bundle.Add("tables/about.md", Concept("About"))
            .Add("metrics/about.md", Concept("About"))
            .Add("policies/about.md", Concept("Policies, and about them"));

        Assert.Empty(bundle.Lint());
    }

    [Fact]
    public void OKF0303StillCatchesRealCollisionsBesideExemptAboutFiles()
    {
        // The exemption is scoped to the conventional name: ordinary concepts colliding
        // across subdirectories still report, and an about.md is never the file a later
        // collision is reported against.
        using var bundle = new TempBundle();
        bundle.Add("tables/about.md", Concept("About"))
            .Add("metrics/about.md", Concept("About"))
            .Add("tables/orders.md", Concept("Orders"))
            .Add("metrics/orders.md", Concept("Something else"));

        var diagnostic = Assert.Single(bundle.Lint());

        Assert.Equal(OkfRules.NearDuplicateConcept, diagnostic.RuleId);
        Assert.Contains("filename", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("metrics/orders.md", diagnostic.Message, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("tables", "orders.md"), diagnostic.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void OKF0305OnlyFiresWhenATagRegistryIsConfigured()
    {
        using var bundle = new TempBundle();
        bundle.Add("tagged.md", CleanConcept);

        Assert.Empty(bundle.Lint());

        var withRegistry = bundle.Lint(new OkfLintOptions
        {
            Today = TempBundle.Today,
            TagRegistry = ["approved"],
        });

        var diagnostic = Assert.Single(withRegistry);
        Assert.Equal(OkfRules.UnregisteredTag, diagnostic.RuleId);
        Assert.Equal(OkfSeverity.Hidden, diagnostic.Severity);
    }

    [Fact]
    public void SeverityConfigurationReachesEveryDiagnostic()
    {
        using var bundle = new TempBundle();
        bundle.Add("bare.md", "---\ntype: Reference\ntitle: Bare\n---\n\n# Bare\n");

        var layer = new OkfSeverityLayer("test");
        layer.Severities[OkfRules.MissingDescription] = OkfSeverity.Error;
        var options = new OkfLintOptions
        {
            Today = TempBundle.Today,
            Severities = new OkfSeverityResolver([layer]),
        };

        var result = new OkfLinter(options).Lint(bundle.Bundle);

        Assert.True(result.HasErrors);
        Assert.Equal(1, result.Count(OkfSeverity.Error));
    }

    [Fact]
    public void DiagnosticsAreOrderedByPathThenLine()
    {
        using var bundle = new TempBundle();
        bundle.Add("b.md", "---\ntype: Reference\ntitle: B\n---\n\n# B\n")
            .Add("a.md", "---\ntype: Reference\ntitle: A\n---\n\n# A\n");

        var diagnostics = bundle.Lint();

        Assert.Equal(
            [.. diagnostics.Select(d => (d.Path, d.Line ?? 0, d.RuleId)).Order()],
            [.. diagnostics.Select(d => (d.Path, d.Line ?? 0, d.RuleId))]);
    }

    private static string Concept(string title, string extra = "") =>
        $"""
        ---
        type: Reference
        title: {title}
        description: A concept.
        tags: [fixture]
        {extra}
        ---

        # {title}
        """;
}
