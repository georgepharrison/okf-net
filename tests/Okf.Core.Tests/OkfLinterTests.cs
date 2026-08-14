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
