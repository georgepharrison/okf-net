namespace Okf.Core.Tests;

/// <summary>
/// Verification stamping (PRD CORE-14): what it appends, what it normalizes, and — the
/// half that matters most — everything it leaves exactly as it found it.
/// </summary>
public class OkfStampTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 15, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void TheStampIsUtcToTheSecondWithAZ()
    {
        // Two stamps from two machines must be comparable; a local offset makes them look
        // ordered when they are not. The rendering itself is OkfCanonicalTimestamp's, pinned by its
        // own tests; what is pinned here is that a stamp written from a local-offset clock
        // reaches the file in the canonical form and not in the clock's.
        Assert.Equal(
            """{ by: "human:ringo", at: 2026-08-15T19:30:00Z }""",
            OkfStamp.VerifiedEntry("human:ringo", new DateTimeOffset(2026, 8, 15, 14, 30, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void TheEventIsAFlowMappingWithTheActorQuoted()
    {
        // A `human:`/`process:` actor carries a colon, which is an indicator in flow
        // context; the timestamp does not need quoting and does not get it.
        Assert.Equal(
            """{ by: "human:ringo", at: 2026-08-15T14:30:00Z }""",
            OkfStamp.VerifiedEntry("human:ringo", At));
    }

    [Fact]
    public void AConceptWithNoVerifiedKeyGainsOneAtTheEndOfTheFrontmatter()
    {
        var stamped = OkfStamp.VerifyText("---\ntype: Concept\ntitle: Widgets\n---\n\nBody.\n", "human:ringo", At);

        Assert.Equal(
            """
            ---
            type: Concept
            title: Widgets
            verified:
              - { by: "human:ringo", at: 2026-08-15T14:30:00Z }
            ---

            Body.

            """.ReplaceLineEndings("\n"),
            stamped);
    }

    [Fact]
    public void EveryOtherLineOfTheFileIsByteIdentical()
    {
        // The point of stamping the text rather than re-emitting the document: an
        // acknowledgment must not arrive as a diff touching every line of frontmatter.
        var source = """
            ---
            type: Concept
            title: Widgets
            okf_version: "0.2"
            producer_only_key: kept
            generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
            sources:
              - id: spec
                resource: https://example.org/spec
                last_modified: 2026-07-24
            ---

            The body, with a [link](other.md) and a `[^spec]`.
            """.ReplaceLineEndings("\n");

        var stamped = OkfStamp.VerifyText(source, "human:ringo", At);

        var added = stamped.Split('\n').Except(source.Split('\n'), StringComparer.Ordinal).ToArray();
        Assert.Equal(["verified:", """  - { by: "human:ringo", at: 2026-08-15T14:30:00Z }"""], added);
        Assert.Empty(source.Split('\n').Except(stamped.Split('\n'), StringComparer.Ordinal));
    }

    [Fact]
    public void AnExistingBlockSequenceIsAppendedToAtItsOwnIndentation()
    {
        var source = """
            ---
            type: Concept
            verified:
                - { by: "process:schema-check", at: 2026-08-01T00:00:00Z }
            title: Widgets
            ---

            Body.
            """.ReplaceLineEndings("\n");

        var stamped = OkfStamp.VerifyText(source, "human:ringo", At);

        Assert.Contains(
            """
                - { by: "process:schema-check", at: 2026-08-01T00:00:00Z }
                - { by: "human:ringo", at: 2026-08-15T14:30:00Z }
            title: Widgets
            """.ReplaceLineEndings("\n"),
            stamped,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMultiLineSequenceItemIsNotSplitByTheInsertion()
    {
        var source = """
            ---
            type: Concept
            verified:
              - by: "process:schema-check"
                at: 2026-08-01T00:00:00Z
            ---

            Body.
            """.ReplaceLineEndings("\n");

        var events = OkfDocument.NormalizeVerified(
            OkfDocument.Parse(OkfStamp.VerifyText(source, "human:ringo", At)).Frontmatter);

        Assert.Equal(2, events.Count);
        Assert.Equal("process:schema-check", Text(events[0], "by"));
        Assert.Equal("2026-08-01T00:00:00Z", Text(events[0], "at"));
        Assert.Equal("human:ringo", Text(events[1], "by"));
    }

    [Fact]
    public void ABareInlineMappingBecomesTheListsFirstElement()
    {
        // §5.2: a single verifier MAY be written as one mapping without the list dash.
        // Appending to it in place would silently drop the first verifier.
        var source = """
            ---
            type: Concept
            verified: { by: "process:schema-check", at: 2026-08-01T00:00:00Z }
            ---

            Body.
            """.ReplaceLineEndings("\n");

        var stamped = OkfStamp.VerifyText(source, "human:ringo", At);

        Assert.Contains(
            """
            verified:
              - { by: "process:schema-check", at: 2026-08-01T00:00:00Z }
              - { by: "human:ringo", at: 2026-08-15T14:30:00Z }
            """.ReplaceLineEndings("\n"),
            stamped,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyVerifiedKeyKeepsItsPositionAndGainsTheEvent()
    {
        var stamped = OkfStamp.VerifyText("---\ntype: Concept\nverified:\ntitle: T\n---\n\nBody.\n", "human:ringo", At);

        Assert.Equal(
            "---\ntype: Concept\nverified:\n  - { by: \"human:ringo\", at: 2026-08-15T14:30:00Z }\ntitle: T\n---\n\nBody.\n",
            stamped);
    }

    [Fact]
    public void AShapeAnInsertionCannotReachFallsBackToTheEmitterAndStillStampsCorrectly()
    {
        // A block-style bare mapping: turning it into a list would mean re-indenting
        // somebody else's frontmatter, so the emitter takes it. The formatting changes; the
        // content does not.
        var source = """
            ---
            type: Concept
            verified:
              by: "process:schema-check"
              at: 2026-08-01T00:00:00Z
            ---

            Body.
            """.ReplaceLineEndings("\n");

        var reparsed = OkfDocument.Parse(OkfStamp.VerifyText(source, "human:ringo", At));
        var events = OkfDocument.NormalizeVerified(reparsed.Frontmatter);

        Assert.Equal(2, events.Count);
        Assert.Equal("process:schema-check", Text(events[0], "by"));
        Assert.Equal("human:ringo", Text(events[1], "by"));
        Assert.Equal("Body.", reparsed.Body.TrimEnd('\n'));
    }

    [Fact]
    public void AnInlineFlowSequenceFallsBackToTheEmitterAndStillStamps()
    {
        // `verified: [ {...} ]` is a shape the insertion deliberately does not reach: the
        // emitter takes it, and the content is right even though the formatting moves.
        var source = """
            ---
            type: Concept
            verified: [{ by: "process:schema-check", at: 2026-08-01T00:00:00Z }]
            ---

            Body.
            """.ReplaceLineEndings("\n");

        var events = OkfDocument.NormalizeVerified(
            OkfDocument.Parse(OkfStamp.VerifyText(source, "human:ringo", At)).Frontmatter);

        Assert.Equal(2, events.Count);
        Assert.Equal("process:schema-check", Text(events[0], "by"));
        Assert.Equal("human:ringo", Text(events[1], "by"));
    }

    [Fact]
    public void DuplicateVerifiedKeysAreAParseErrorRatherThanAGuess()
    {
        // YAML rejects a duplicate key, so there is never a question of which one to
        // extend — the refusal arrives from the parser before the stamp is attempted.
        var source = """
            ---
            type: Concept
            verified:
              - { by: "process:a", at: 2026-08-01T00:00:00Z }
            verified:
              - { by: "process:b", at: 2026-08-02T00:00:00Z }
            ---

            Body.
            """.ReplaceLineEndings("\n");

        Assert.Throws<OkfDocumentException>(() => OkfStamp.VerifyText(source, "human:ringo", At));
    }

    [Fact]
    public void AFileWithNoFrontmatterGainsSomeFromTheEmitter()
    {
        var stamped = OkfStamp.VerifyText("Just a body, no fence.\n", "human:ringo", At);

        var document = OkfDocument.Parse(stamped);
        Assert.Equal("human:ringo", Text(Assert.Single(OkfDocument.NormalizeVerified(document.Frontmatter)), "by"));
        Assert.Equal("Just a body, no fence.", document.Body.TrimEnd('\n'));
    }

    [Theory]
    [InlineData("---\ntype: Concept\n---\n\nBody.\n")]
    [InlineData("---\ntype: Concept\nverified:\n  - { by: \"process:x\", at: 2026-08-01 }\n---\n\nBody.\n")]
    [InlineData("---\ntype: Concept\nverified: { by: \"process:x\", at: 2026-08-01 }\n---\n\nBody.\n")]
    public void EveryStampedShapeParsesBackWithTheNewEventLast(string source)
    {
        var frontmatter = OkfDocument.Parse(OkfStamp.VerifyText(source, "human:ringo", At)).Frontmatter;
        var events = OkfDocument.NormalizeVerified(frontmatter);

        Assert.Equal("human:ringo", Text(events[^1], "by"));
        Assert.Equal("2026-08-15T14:30:00Z", Text(events[^1], "at"));
        Assert.Equal(OkfTrustTier.HumanReviewed, OkfDocument.TrustTier(frontmatter));
    }

    [Fact]
    public void GeneratedIsNeverReadAndNeverWritten()
    {
        var source = """
            ---
            type: Concept
            generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }
            ---

            Body.
            """.ReplaceLineEndings("\n");

        var stamped = OkfStamp.VerifyText(source, "human:ringo", At);

        Assert.Contains(
            "generated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }",
            stamped,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CrlfLineEndingsSurviveTheStamp()
    {
        var stamped = OkfStamp.VerifyText("---\r\ntype: Concept\r\n---\r\n\r\nBody.\r\n", "human:ringo", At);

        Assert.Equal(
            "---\r\ntype: Concept\r\nverified:\r\n  - { by: \"human:ringo\", at: 2026-08-15T14:30:00Z }\r\n---\r\n\r\nBody.\r\n",
            stamped);
    }

    [Fact]
    public void AStrayCarriageReturnInTheBodyDoesNotRewriteEveryLineEnding()
    {
        // The whole argument for editing text rather than re-emitting is that an
        // acknowledgment is one line of diff. Reading one '\r' anywhere in the file as
        // "this file is CRLF" would hand back a file whose every line moved — the
        // whole-file diff, arriving from the path that exists to prevent it.
        var source = "---\ntype: Concept\n---\n\nA body with a lone \r carriage return.\n";

        var stamped = OkfStamp.VerifyText(source, "human:ringo", At);

        Assert.Equal(
            "---\ntype: Concept\nverified:\n  - { by: \"human:ringo\", at: 2026-08-15T14:30:00Z }\n---"
            + "\n\nA body with a lone \r carriage return.\n",
            stamped);
    }

    [Fact]
    public void ACrlfConceptWhoseBodyHoldsALfOnlyLineKeepsBothEndings()
    {
        // The mirror image: the inserted lines take the fence's ending, and every other
        // line — including the odd one out — keeps its own.
        var source = "---\r\ntype: Concept\r\n---\r\n\r\nCRLF body.\r\nLF line.\nEnd.\r\n";

        var stamped = OkfStamp.VerifyText(source, "human:ringo", At);

        Assert.Equal(
            "---\r\ntype: Concept\r\nverified:\r\n  - { by: \"human:ringo\", at: 2026-08-15T14:30:00Z }\r\n---"
            + "\r\n\r\nCRLF body.\r\nLF line.\nEnd.\r\n",
            stamped);
    }

    [Fact]
    public void EveryInsertionShapeKeepsTheFilesOwnLineEndings()
    {
        // Each of the three shapes the insertion reaches, in CRLF: the inserted lines must
        // carry the fence's ending or the result is a mixed-ending frontmatter.
        string[] sources =
        [
            "---\r\ntype: Concept\r\n---\r\n\r\nBody.\r\n",
            "---\r\ntype: Concept\r\nverified:\r\n  - { by: \"process:x\", at: 2026-08-01 }\r\n---\r\n\r\nBody.\r\n",
            "---\r\ntype: Concept\r\nverified: { by: \"process:x\", at: 2026-08-01 }\r\n---\r\n\r\nBody.\r\n",
            "---\r\ntype: Concept\r\nverified:\r\n---\r\n\r\nBody.\r\n",
        ];

        foreach (var source in sources)
        {
            var stamped = OkfStamp.VerifyText(source, "human:ringo", At);

            Assert.DoesNotContain("Z }\n ", stamped, StringComparison.Ordinal);
            Assert.Contains("\r\n  - { by: \"human:ringo\", at: 2026-08-15T14:30:00Z }\r\n", stamped, StringComparison.Ordinal);
            Assert.Equal(
                "human:ringo",
                Text(OkfDocument.NormalizeVerified(OkfDocument.Parse(stamped).Frontmatter)[^1], "by"));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ringo")]
    [InlineData("human:")]
    [InlineData("process:")]
    [InlineData(" human:ringo")]
    [InlineData("human:ringo\", at: 2026-01-01 }\nowned: yes\nx: \"")]
    public void AnActorThatIsNotWrittenInASpecFormIsRefused(string actor)
    {
        var source = "---\ntype: Concept\n---\n\nBody.\n";

        Assert.Throws<ArgumentException>(() => OkfStamp.VerifyText(source, actor, At));
        Assert.Throws<ArgumentException>(() => OkfStamp.Verify(OkfDocument.Parse(source), actor, At));
    }

    [Theory]
    [InlineData("human:ringo")]
    [InlineData("human:ringo.harrison@gmail.com")]
    [InlineData("process:nightly-schema-check")]
    [InlineData("claude-fable/5")]
    public void EverySpecActorFormIsAccepted(string actor)
    {
        var events = OkfDocument.NormalizeVerified(
            OkfDocument.Parse(OkfStamp.VerifyText("---\ntype: Concept\n---\n\nBody.\n", actor, At)).Frontmatter);

        Assert.Equal(actor, Text(Assert.Single(events), "by"));
    }

    [Fact]
    public void VerifyFileWritesTheStampBackWithNoByteOrderMark()
    {
        using var bundle = new TempBundle();
        bundle.Add("widgets.md", "---\ntype: Concept\ntitle: Widgets\n---\n\nBody.\n");
        var path = Path.Combine(bundle.Root, "widgets.md");

        OkfStamp.VerifyFile(path, "human:ringo", At);

        Assert.Equal(
            "---\ntype: Concept\ntitle: Widgets\nverified:\n  - { by: \"human:ringo\", at: 2026-08-15T14:30:00Z }\n---\n\nBody.\n",
            File.ReadAllText(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void AConceptWithNoGeneratedKeyGainsOneAtTheEndOfTheFrontmatter()
    {
        // Shape one of three: the key is absent, so it is inserted before the closing fence
        // — the same place `verified` lands, for the same reason.
        var stamped = OkfStamp.StampGeneratedText(
            "---\ntype: Concept\ntitle: Widgets\n---\n\nBody.\n",
            "claude-fable/5",
            At);

        Assert.Equal(
            "---\ntype: Concept\ntitle: Widgets\ngenerated: { by: \"claude-fable/5\", at: 2026-08-15T14:30:00Z }\n"
            + "---\n\nBody.\n",
            stamped);
    }

    [Fact]
    public void AnExistingOneLineGeneratedMappingIsReplacedInPlace()
    {
        // Shape two: the key exists as §5.2's flow mapping. Only that line moves, and it
        // keeps its position among the other keys rather than migrating to the end.
        var source = """
            ---
            type: Concept
            generated: { by: claude-opus/4, at: 2020-01-01T00:00:00Z }
            title: Widgets
            verified:
              - { by: "human:ringo", at: 2021-01-01T00:00:00Z }
            ---

            Body.

            """.ReplaceLineEndings("\n");

        var stamped = OkfStamp.StampGeneratedText(source, "claude-fable/5", At);

        Assert.Equal(
            source.Replace(
                "generated: { by: claude-opus/4, at: 2020-01-01T00:00:00Z }",
                "generated: { by: \"claude-fable/5\", at: 2026-08-15T14:30:00Z }",
                StringComparison.Ordinal),
            stamped);
    }

    [Fact]
    public void ABlockMappingUnderGeneratedFallsBackToTheEmitter()
    {
        // Shape three: `generated` written as a block mapping. Replacing it by line surgery
        // means re-indenting somebody else's frontmatter, so the emitter takes it — the
        // content survives and the formatting moves, which is the documented trade.
        var stamped = OkfStamp.StampGeneratedText(
            "---\ntype: Concept\ngenerated:\n  by: claude-opus/4\n  at: 2020-01-01T00:00:00Z\ntitle: Widgets\n---\n\nBody.\n",
            "claude-fable/5",
            At);

        var frontmatter = OkfDocument.Parse(stamped).Frontmatter;
        var generated = Assert.IsType<OkfMapping>(frontmatter["generated"]);

        Assert.Equal("claude-fable/5", Text(generated, "by"));
        Assert.Equal("2026-08-15T14:30:00Z", Text(generated, "at"));
        Assert.DoesNotContain("claude-opus/4", stamped, StringComparison.Ordinal);

        // The rest of the document is still there, which is what "the content survives"
        // means when the formatting is allowed to move.
        Assert.Equal("Concept", Text(frontmatter, "type"));
        Assert.Equal("Widgets", Text(frontmatter, "title"));
        Assert.Contains("Body.", stamped, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStampIsParsedBackAndHoldsExactlyWhatWasWritten()
    {
        // AD-23's rule applied to `generated`: text surgery that produced something okf
        // cannot read did not happen. Every shape goes through the same check, so this
        // walks all three and asks the reader rather than the writer.
        string[] sources =
        [
            "---\ntype: Concept\n---\n\nBody.\n",
            "---\ntype: Concept\ngenerated: { by: claude-opus/4, at: 2020-01-01T00:00:00Z }\n---\n\nBody.\n",
            "---\ntype: Concept\ngenerated:\n  by: claude-opus/4\n  at: 2020-01-01T00:00:00Z\n---\n\nBody.\n",
        ];

        foreach (var source in sources)
        {
            var generated = Assert.IsType<OkfMapping>(
                OkfDocument.Parse(OkfStamp.StampGeneratedText(source, "process:nightly", At)).Frontmatter["generated"]);

            Assert.Equal("process:nightly", Text(generated, "by"));
            Assert.Equal("2026-08-15T14:30:00Z", Text(generated, "at"));
        }
    }

    [Fact]
    public void RestampingLeavesEveryOtherLineByteIdentical()
    {
        var source = """
            ---
            type: Concept
            title: Widgets
            okf_producer_note: kept
            generated: { by: claude-opus/4, at: 2020-01-01T00:00:00Z }
            verified:
              - { by: "human:ringo", at: 2021-01-01T00:00:00Z }
            ---

            Body with a `[^spec]` footnote.

            """.ReplaceLineEndings("\n");

        var stamped = OkfStamp.StampGeneratedText(source, "claude-fable/5", At);

        var added = stamped.Split('\n').Except(source.Split('\n'), StringComparer.Ordinal).ToArray();
        var removed = source.Split('\n').Except(stamped.Split('\n'), StringComparer.Ordinal).ToArray();

        Assert.Equal(["generated: { by: \"claude-fable/5\", at: 2026-08-15T14:30:00Z }"], added);
        Assert.Equal(["generated: { by: claude-opus/4, at: 2020-01-01T00:00:00Z }"], removed);
    }

    [Fact]
    public void RestampingKeepsTheLineEndingsTheFileAlreadyHad()
    {
        var stamped = OkfStamp.StampGeneratedText(
            "---\r\ntype: Concept\r\ngenerated: { by: claude-opus/4, at: 2020-01-01T00:00:00Z }\r\n---\r\n\r\nBody.\r\n",
            "claude-fable/5",
            At);

        Assert.Contains(
            "\r\ngenerated: { by: \"claude-fable/5\", at: 2026-08-15T14:30:00Z }\r\n",
            stamped,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Z }\n", stamped, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ringo")]
    [InlineData("human:")]
    [InlineData("claude-fable/5\", at: 2020-01-01 }\nowned: yes\nx: \"")]
    public void AGenerationStampIsRefusedForAnActorThatIsNotASpecForm(string actor)
    {
        var source = "---\ntype: Concept\n---\n\nBody.\n";

        Assert.Throws<ArgumentException>(() => OkfStamp.StampGeneratedText(source, actor, At));
        Assert.Throws<ArgumentException>(() => OkfStamp.StampGenerated(OkfDocument.Parse(source), actor, At));
    }

    [Fact]
    public void StampGeneratedFileWritesTheStampBackWithNoByteOrderMark()
    {
        using var bundle = new TempBundle();
        bundle.Add("widgets.md", "---\ntype: Concept\ntitle: Widgets\n---\n\nBody.\n");
        var path = Path.Combine(bundle.Root, "widgets.md");

        OkfStamp.StampGeneratedFile(path, "claude-fable/5", At);

        Assert.Equal(
            "---\ntype: Concept\ntitle: Widgets\ngenerated: { by: \"claude-fable/5\", at: 2026-08-15T14:30:00Z }\n"
            + "---\n\nBody.\n",
            File.ReadAllText(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    private static string? Text(OkfMapping mapping, string key) =>
        mapping.TryGetValue(key, out var value) && value is OkfScalar scalar ? scalar.Value : null;
}
