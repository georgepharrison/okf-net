namespace Okf.Core.Tests.Documents;

/// <summary>
/// Parse and serialize behavior (spec §4, PRD CORE-1/CORE-2). Every test named
/// <c>Ported_*</c> is a direct port of a case in the reference implementation's
/// <c>tests/test_document.py</c>; the rest cover edge cases that implementation
/// exhibits but does not test.
/// </summary>
public class OkfDocumentParseTests
{
    private const string Sample = """
        ---
        type: BigQuery Table
        title: Sample
        description: A sample table.
        tags: [a, b]
        timestamp: 2026-05-27T00:00:00+00:00
        ---

        # Sample

        Body text.

        """;

    [Fact]
    public void Ported_RoundtripPreservesFrontmatterAndBody()
    {
        var doc = OkfDocument.Parse(Sample);

        Assert.Equal("BigQuery Table", OkfValues.Text(doc.Frontmatter, "type"));
        Assert.Equal(["a", "b"], OkfValues.Texts(doc.Frontmatter["tags"]));
        Assert.StartsWith("# Sample", doc.Body, StringComparison.Ordinal);

        var serialized = doc.Serialize();
        var reparsed = OkfDocument.Parse(serialized);

        OkfValues.AssertDeepEqual(doc.Frontmatter, reparsed.Frontmatter);
        Assert.Equal(doc.Body.Trim(), reparsed.Body.Trim());
    }

    [Fact]
    public void Ported_ParseNoFrontmatterTreatsAllAsBody()
    {
        var doc = OkfDocument.Parse("# Hello\n\nNo frontmatter here.\n");

        Assert.Empty(doc.Frontmatter);
        Assert.Contains("Hello", doc.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Ported_UnterminatedFrontmatterRaises()
    {
        var ex = Assert.Throws<OkfDocumentException>(
            () => OkfDocument.Parse("---\ntype: X\nstill in frontmatter\n"));

        Assert.Contains("Unterminated", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeIsIdempotentOnItsOwnOutput()
    {
        var once = OkfDocument.Parse(Sample).Serialize();
        var twice = OkfDocument.Parse(once).Serialize();

        Assert.Equal(once, twice);
    }

    [Fact]
    public void RoundTripPreservesKeyOrderWithoutAlphabetizing()
    {
        var doc = OkfDocument.Parse(Sample);
        Assert.Equal(["type", "title", "description", "tags", "timestamp"], OkfValues.Keys(doc.Frontmatter));

        var reparsed = OkfDocument.Parse(doc.Serialize());
        Assert.Equal(["type", "title", "description", "tags", "timestamp"], OkfValues.Keys(reparsed.Frontmatter));
    }

    [Fact]
    public void RoundTripPreservesUnknownProducerKeys()
    {
        // acme_retail's `not:` extension, and a nested unknown structure (§4.1:
        // consumers SHOULD preserve unknown keys, MUST NOT reject them).
        var source = """
            ---
            type: Metric
            not:
              - a competitor metric
              - a forecast
            executor: { resource: file://compute.py, kind: python }
            ---

            Body.

            """;

        var doc = OkfDocument.Parse(source);
        var reparsed = OkfDocument.Parse(doc.Serialize());

        OkfValues.AssertDeepEqual(doc.Frontmatter, reparsed.Frontmatter);
        Assert.Equal(["a competitor metric", "a forecast"], OkfValues.Texts(reparsed.Frontmatter["not"]));
        Assert.Equal("python", OkfValues.Text(Assert.IsType<OkfMapping>(reparsed.Frontmatter["executor"]), "kind"));
    }

    [Fact]
    public void RoundTripDoesNotRetypeScalars()
    {
        // PRD CORE-2: quoted version strings, dates, and numeric-looking strings
        // keep their original form. okf-net stores scalars as text plus style, so
        // nothing is resolved into a typed value and re-rendered.
        var source = """
            ---
            type: Guide
            okf_version: "0.2"
            zip: "01234"
            released: 2026-05-27
            count: 7
            ---

            Body.

            """;

        var serialized = OkfDocument.Parse(source).Serialize();

        Assert.Contains("okf_version: \"0.2\"", serialized, StringComparison.Ordinal);
        Assert.Contains("zip: \"01234\"", serialized, StringComparison.Ordinal);
        Assert.Contains("released: 2026-05-27", serialized, StringComparison.Ordinal);
        Assert.Contains("count: 7", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTripPreservesFlowAndBlockCollectionStyle()
    {
        var source = """
            ---
            type: Metric
            tags: [a, b]
            executor: {kind: python}
            sources:
              - id: s1
            ---

            Body.

            """;

        var serialized = OkfDocument.Parse(source).Serialize();

        Assert.Contains("tags: [a, b]", serialized, StringComparison.Ordinal);
        Assert.Contains("executor: {kind: python}", serialized, StringComparison.Ordinal);
        Assert.Contains("sources:\n- id: s1", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTripKeepsAnUnquotedNullAsNullNotAnEmptyString()
    {
        // PRD CORE-2: a round trip must not retype a scalar. `stale_after:` is YAML
        // null; emitting it as `''` would hand every downstream consumer an empty
        // string instead, so it is written as the explicit `null` the reference
        // dumper produces.
        var doc = OkfDocument.Parse("---\ntype: X\nstale_after:\n---\n\nBody.\n");

        Assert.True(Assert.IsType<OkfScalar>(doc.Frontmatter["stale_after"]).IsNull);

        var serialized = doc.Serialize();
        Assert.Contains("stale_after: null", serialized, StringComparison.Ordinal);
        Assert.True(Assert.IsType<OkfScalar>(OkfDocument.Parse(serialized).Frontmatter["stale_after"]).IsNull);

        // Idempotent from there on, and a code-built empty string stays a string.
        Assert.Equal(serialized, OkfDocument.Parse(serialized).Serialize());
        Assert.Contains(
            "title: ''",
            new OkfDocument(new OkfMapping { { "title", string.Empty } }, "Body.").Serialize(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsDuplicateFrontmatterKeys_DeviationFromReference()
    {
        // DELIBERATE DEVIATION, pinned here so it stays a decision rather than an
        // accident. PyYAML's safe_load accepts duplicate mapping keys and keeps the
        // last; YAML 1.2 requires keys to be unique and the loader okf-net uses
        // enforces that. A duplicate key is an authoring bug §11 reporting should
        // surface rather than silently discard — but it does mean a document the
        // reference agent reads without complaint is an error here.
        var ex = Assert.Throws<OkfDocumentException>(
            () => OkfDocument.Parse("---\ntype: X\ntype: Y\n---\n\nBody.\n"));

        Assert.StartsWith("Invalid YAML in frontmatter:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseEmptyTextYieldsEmptyDocument()
    {
        var doc = OkfDocument.Parse(string.Empty);

        Assert.Empty(doc.Frontmatter);
        Assert.Equal(string.Empty, doc.Body);
    }

    [Fact]
    public void ParseIgnoresADelimiterThatIsNotOnTheFirstLine()
    {
        var source = "intro\n---\ntype: X\n---\n";

        var doc = OkfDocument.Parse(source);

        Assert.Empty(doc.Frontmatter);
        Assert.Equal(source, doc.Body);
    }

    [Fact]
    public void ParseToleratesWhitespaceAroundTheDelimiters()
    {
        // The reference implementation compares `line.strip()` to `---`.
        var doc = OkfDocument.Parse("  ---  \ntype: X\n\t---\n\nBody.\n");

        Assert.Equal("X", OkfValues.Text(doc.Frontmatter, "type"));
        Assert.Equal("Body.", doc.Body);
    }

    [Fact]
    public void ParseDoesNotTreatALongerRuleAsADelimiter()
    {
        var source = "----\ntype: X\n----\n";

        var doc = OkfDocument.Parse(source);

        Assert.Empty(doc.Frontmatter);
        Assert.Equal(source, doc.Body);
    }

    [Fact]
    public void ParseEmptyFrontmatterBlockIsNotAnError()
    {
        var doc = OkfDocument.Parse("---\n---\n\nBody.\n");

        Assert.Empty(doc.Frontmatter);
        Assert.Equal("Body.", doc.Body);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"\"")]
    public void ParseFalsyFrontmatterDocumentYieldsEmptyFrontmatter(string frontmatter)
    {
        // `yaml.safe_load(fm_text) or {}` in the reference implementation: a falsy
        // document becomes an empty mapping instead of failing the mapping check.
        var doc = OkfDocument.Parse($"---\n{frontmatter}\n---\n\nBody.\n");

        Assert.Empty(doc.Frontmatter);
    }

    [Theory]
    [InlineData("- a\n- b")]
    [InlineData("just a string")]
    [InlineData("42")]
    public void ParseTruthyNonMappingFrontmatterRaises(string frontmatter)
    {
        var ex = Assert.Throws<OkfDocumentException>(
            () => OkfDocument.Parse($"---\n{frontmatter}\n---\n\nBody.\n"));

        Assert.Contains("must be a YAML mapping", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseInvalidYamlRaisesWithTheParserMessage()
    {
        var ex = Assert.Throws<OkfDocumentException>(
            () => OkfDocument.Parse("---\ntype: [unclosed\n---\n\nBody.\n"));

        Assert.StartsWith("Invalid YAML in frontmatter:", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void ParseMultipleYamlDocumentsInTheFrontmatterRaises()
    {
        var ex = Assert.Throws<OkfDocumentException>(
            () => OkfDocument.Parse("---\ntype: X\n...\ntype: Y\n---\n\nBody.\n"));

        Assert.Contains("single document", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseConsumesExactlyOneBlankLineAfterTheFrontmatter()
    {
        var one = OkfDocument.Parse("---\ntype: X\n---\n\nBody.\n");
        var two = OkfDocument.Parse("---\ntype: X\n---\n\n\nBody.\n");
        var none = OkfDocument.Parse("---\ntype: X\n---\nBody.\n");

        Assert.Equal("Body.", one.Body);
        Assert.Equal("\nBody.", two.Body);
        Assert.Equal("Body.", none.Body);
    }

    [Fact]
    public void ParseDropsTheTrailingNewlineAndSerializeRestoresIt()
    {
        // splitlines()/"\n".join() in the reference implementation loses a trailing
        // newline; serialize() adds one back.
        var doc = OkfDocument.Parse("---\ntype: X\n---\n\nBody.\n");

        Assert.Equal("Body.", doc.Body);
        Assert.EndsWith("Body.\n", doc.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseNormalizesCarriageReturnLineEndings()
    {
        var doc = OkfDocument.Parse("---\r\ntype: X\r\n---\r\n\r\nLine one.\r\nLine two.\r\n");

        Assert.Equal("X", OkfValues.Text(doc.Frontmatter, "type"));
        Assert.Equal("Line one.\nLine two.", doc.Body);
    }

    [Fact]
    public void SerializeKeepsNonAsciiTextUnescaped()
    {
        // The reference implementation dumps with allow_unicode=True.
        var source = "---\ntype: Guide\ntitle: Café — 日本語\n---\n\nBody.\n";

        Assert.Contains("title: Café — 日本語", OkfDocument.Parse(source).Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeEmptyFrontmatterEmitsAnEmptyFlowMapping()
    {
        var serialized = new OkfDocument(new OkfMapping(), "Body.").Serialize();

        Assert.Equal("---\n{}\n---\n\nBody.\n", serialized);
    }

    [Fact]
    public void SerializeBuildsTheDocumentFromCodeWithoutStyleHints()
    {
        var frontmatter = new OkfMapping
        {
            { "type", "Guide" },
            { "title", "Getting started" },
        };

        Assert.Equal("---\ntype: Guide\ntitle: Getting started\n---\n\nBody.\n", new OkfDocument(frontmatter, "Body.").Serialize());
    }

    [Fact]
    public void SerializeRejectsRecursiveFrontmatter()
    {
        var sequence = new OkfSequence();
        sequence.Add(sequence);
        var frontmatter = new OkfMapping { { "type", OkfValue.Scalar("X") }, { "loop", sequence } };

        var ex = Assert.Throws<OkfDocumentException>(() => new OkfDocument(frontmatter, "Body.").Serialize());

        Assert.Contains("recursive", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reference implementation splits with <c>str.splitlines()</c>, which ends a line
    /// on a bare CR as well as on LF and CRLF. Expectations here are Python's:
    /// <c>"Line one.\rLine two.\r".splitlines()</c> is <c>['Line one.', 'Line two.']</c>.
    /// </summary>
    [Fact]
    public void ParseEndsALineOnABareCarriageReturnIncludingTheLastOne()
    {
        var doc = OkfDocument.Parse("---\ntype: X\r---\n\nLine one.\rLine two.\r");

        Assert.Equal("X", OkfValues.Text(doc.Frontmatter, "type"));
        Assert.Equal("Line one.\nLine two.", doc.Body);
    }

    [Fact]
    public void SerializeDoesNotAddASecondNewlineToABodyThatHasOne()
    {
        var frontmatter = new OkfMapping { { "type", "Guide" } };

        Assert.Equal("---\ntype: Guide\n---\n\nBody.\n", new OkfDocument(frontmatter, "Body.\n").Serialize());
    }

    /// <summary>
    /// An anchor and its alias parse to one node held twice, which decisions.md records as
    /// a shared node rather than a cycle. Serializing writes the content out at both
    /// places; only a value that contains itself is refused.
    /// </summary>
    [Fact]
    public void SerializeEmitsASharedNodeAtEveryPlaceItAppears()
    {
        var tags = new OkfSequence();
        tags.Add(OkfValue.Scalar("revenue"));
        var executor = new OkfMapping { { "kind", "python" } };
        var frontmatter = new OkfMapping
        {
            { "type", OkfValue.Scalar("Metric") },
            { "tags", tags },
            { "also_tags", tags },
            { "executor", executor },
            { "also_executor", executor },
        };

        var serialized = new OkfDocument(frontmatter, "Body.").Serialize();

        Assert.Equal(
            "---\ntype: Metric\ntags:\n- revenue\nalso_tags:\n- revenue\nexecutor:\n  kind: python\nalso_executor:\n  kind: python\n---\n\nBody.\n",
            serialized);
    }
}
