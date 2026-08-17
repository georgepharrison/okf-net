using System.Diagnostics;

namespace Okf.Core.Tests.Capture;

/// <summary>
/// The write side of the capture manifest (AD-52): what it appends, what it refuses, and —
/// the half that matters most for an immutability record — every byte it leaves alone.
/// </summary>
public class OkfCaptureWriterTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 16, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The dogfood manifest as it stands in this repository: one closed entry, a local
    /// offset on both instants, and an inline `concepts` array. It is the golden input
    /// because every one of those is a formatting choice a re-serializing writer would
    /// silently normalize away.
    /// </summary>
    private const string Dogfood = """
        {
          "manifestVersion": 1,
          "captures": [
            {
              "id": "2026-08-14-equipping-agents-with-agent-skills",
              "form": "flat",
              "files": [
                {
                  "path": "2026-08-14-equipping-agents-with-agent-skills.html",
                  "sha256": "5f06488474e380ada57485178e797353597e1ec37d1b17beb01496ed207733b5"
                }
              ],
              "capturedAt": "2026-08-14T23:52:12-05:00",
              "capturedBy": "claude-fable/5",
              "originalUrl": "https://www.anthropic.com/engineering/equipping-agents-for-the-real-world-with-agent-skills",
              "title": "Equipping agents for the real world with Agent Skills",
              "sourceLastModified": "2025-12-18",
              "ingestion": {
                "at": "2026-08-14T23:54:00-05:00",
                "by": "claude-fable/5",
                "concepts": ["bundles/okf-net/references/agent-skills.md"]
              }
            }
          ]
        }

        """;

    [Fact]
    public void TheRepositorysOwnManifestIsExactlyTheGoldenInput()
    {
        // The golden text above is only worth anything while it IS the dogfood manifest.
        // Read from disk rather than copied, so a change to the real file fails here rather
        // than leaving every byte-identity assertion below testing a fiction.
        Assert.Equal(
            Dogfood.ReplaceLineEndings("\n"),
            File.ReadAllText(DogfoodManifestPath()).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AppendingLeavesEveryExistingByteWhereItWas()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-a-second-page.html"));

        Assert.Equal(OkfCaptureWriteOutcome.Added, result.Outcome);

        // The oracle is the input itself: everything up to the first entry's closing brace
        // must be the original text, character for character, including the local-offset
        // timestamps and the one-line `concepts` array a re-emitter would expand.
        var boundary = Dogfood.IndexOf("\n    }\n", StringComparison.Ordinal) + "\n    }".Length;
        Assert.Equal(Dogfood[..boundary], result.Text![..boundary]);
        Assert.Equal("  ]\n}\n", Dogfood[(boundary + 1)..]);
        Assert.EndsWith("\n      \"ingestion\": null\n    }\n  ]\n}\n", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingTheAppendedEntryGivesTheOriginalBytesBack()
    {
        // The strongest statement of "nothing else moved": the edit is exactly one
        // insertion, so deleting the inserted span restores the input byte for byte.
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var written = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-a-second-page.html")).Text!;

        var insertion = written.IndexOf("\n    {\n      \"id\": \"2026-08-16-a-second-page\"", StringComparison.Ordinal);
        var end = written.IndexOf("\n  ]", StringComparison.Ordinal);
        Assert.True(insertion > 0 && end > insertion, "the appended entry was not found in the result");

        var restored = written[..insertion].TrimEnd(',') + written[end..];
        Assert.Equal(Dogfood.ReplaceLineEndings("\n"), restored);
    }

    [Fact]
    public void AManifestWrittenWithCrlfGainsAnEntryInItsOwnLineEnding()
    {
        // The splice inserts text, and inserted text carries whichever line ending the
        // rest of the document uses. A writer that always inserted LF would leave the one
        // new entry as the only mixed-ending region of the immutability record.
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");
        var crlf = Dogfood.ReplaceLineEndings("\r\n");

        var written = OkfCaptureWriter.Add(crlf, raw.Root, raw.Addition("2026-08-16-a-second-page.html")).Text!;

        // Strip the CRLFs and no bare LF is left: every line ending in the result, the
        // inserted entry's included, is the one the file already used.
        Assert.DoesNotContain(
            "\n",
            written.Replace("\r\n", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);

        // And the same deletion oracle the LF case uses: removing the inserted span gives
        // the CRLF input back byte for byte.
        var insertion = written.IndexOf("\r\n    {\r\n      \"id\": \"2026-08-16-a-second-page\"", StringComparison.Ordinal);
        var end = written.IndexOf("\r\n  ]", StringComparison.Ordinal);
        Assert.True(insertion > 0 && end > insertion, "the appended entry was not found in the CRLF result");
        Assert.Equal(crlf, written[..insertion].TrimEnd(',') + written[end..]);
    }

    [Fact]
    public void ClosingRewritesTheIngestionValueAndNothingElse()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");
        var open = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-a-second-page.html")).Text!;

        var closed = OkfCaptureWriter.Close(open, Closure("2026-08-16-a-second-page"));

        Assert.Equal(OkfCaptureWriteOutcome.Closed, closed.Outcome);

        // Deleting the whole ingestion object and putting `null` back gives the open
        // manifest again: the splice touched the value and nothing around it.
        var start = closed.Text!.IndexOf("\"ingestion\": {\n        \"at\": \"2026-08-16T14:00:00Z\"", StringComparison.Ordinal);
        var end = closed.Text.IndexOf("\n      }\n    }\n  ]", StringComparison.Ordinal) + "\n      }".Length;
        Assert.True(start > 0 && end > start, "the closed ingestion was not found in the result");
        Assert.Equal(open, closed.Text[..start] + "\"ingestion\": null" + closed.Text[end..]);
    }

    [Fact]
    public void TheRecordedDigestIsWhatSha256sumReports()
    {
        // An independent oracle rather than a value copied out of the code: `sha256sum` is
        // the command the capture skill used to tell an agent to run by hand, and the whole
        // point of the verb is that its answer does not change.
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-a-second-page.html"));
        var recorded = OkfCaptureManifest.Parse(result.Text!, "manifest.json")!.Captures
            .Single(entry => entry.Id == "2026-08-16-a-second-page").Files.Single();

        Assert.Equal(Sha256Sum(Path.Combine(raw.Root, "2026-08-16-a-second-page.html")), recorded.Sha256);
    }

    [Fact]
    public void APacketRecordsEveryFileUnderTheDirectoryInPathOrder()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-okapi-paper/original.pdf", "%PDF-1.4 pretend\n");
        raw.Drop("2026-08-16-okapi-paper/extracted.md", "# Extracted\n");

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-paper"));
        var entry = OkfCaptureManifest.Parse(result.Text!, "manifest.json")!.Captures
            .Single(candidate => candidate.Id == "2026-08-16-okapi-paper");

        Assert.Equal(OkfCaptureWriteOutcome.Added, result.Outcome);
        Assert.Equal(
            ["2026-08-16-okapi-paper/extracted.md", "2026-08-16-okapi-paper/original.pdf"],
            entry.Files.Select(file => file.Path));
        Assert.Contains("\"form\": \"packet\"", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyManifestGainsItsFirstEntryWithoutLosingItsShape()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var result = OkfCaptureWriter.Add(
            OkfCaptureWriter.EmptyManifest,
            raw.Root,
            raw.Addition("2026-08-16-a-second-page.html"));

        Assert.StartsWith("{\n  \"manifestVersion\": 1,\n  \"captures\": [\n    {\n", result.Text, StringComparison.Ordinal);
        Assert.EndsWith("\n  ]\n}\n", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUningestedEntryIsReplacedRatherThanDuplicated()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");
        var once = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-a-second-page.html")).Text!;

        raw.Drop("2026-08-16-a-second-page.html", "<html>second, retrieved again</html>\n");
        var again = OkfCaptureWriter.Add(
            once,
            raw.Root,
            raw.Addition("2026-08-16-a-second-page.html", url: "https://example.org/again"));

        var entries = OkfCaptureManifest.Parse(again.Text!, "manifest.json")!.Captures;
        Assert.Equal(OkfCaptureWriteOutcome.Recaptured, again.Outcome);
        Assert.Equal(2, entries.Count);
        Assert.Contains("https://example.org/again", again.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.org/a-second-page", again.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIngestedEntryIsRefusedAndNothingIsProduced()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-14-equipping-agents-with-agent-skills.html", "<html>the real one</html>\n");

        var result = OkfCaptureWriter.Add(
            Dogfood,
            raw.Root,
            raw.Addition("2026-08-14-equipping-agents-with-agent-skills.html"));

        Assert.Equal(OkfCaptureWriteOutcome.AlreadyCaptured, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains("already captured and ingested", result.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "manifestVersion": 2, "captures": [] }""")]
    [InlineData("""{ "manifestVersion": 1 }""")]
    [InlineData("""{ "manifestVersion": 1, "captures": {} }""")]
    public void AManifestThatDoesNotReadAsOneIsNeverRepaired(string manifest)
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var added = OkfCaptureWriter.Add(manifest, raw.Root, raw.Addition("2026-08-16-a-second-page.html"));
        var closed = OkfCaptureWriter.Close(manifest, Closure("2026-08-16-a-second-page"));

        Assert.Equal(OkfCaptureWriteOutcome.ManifestUnreadable, added.Outcome);
        Assert.Equal(OkfCaptureWriteOutcome.ManifestUnreadable, closed.Outcome);
        Assert.Null(added.Text);
        Assert.Null(closed.Text);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("true")]
    public void AManifestWithAMistypedCapturesCollectionDoesNotParse(string captures) =>
        Assert.Null(OkfCaptureManifest.Parse(
            $$"""{ "manifestVersion": 1, "captures": {{captures}} }""",
            "manifest.json"));

    [Fact]
    public void AMistypedFilesCollectionIsTreatedAsAbsent()
    {
        OkfCaptureManifest? manifest = OkfCaptureManifest.Parse(
            """
            {
              "manifestVersion": 1,
              "captures": [
                { "id": "2026-08-16-a-second-page", "files": {}, "ingestion": null }
              ]
            }
            """,
            "manifest.json");

        Assert.NotNull(manifest);
        Assert.Empty(Assert.Single(manifest.Captures).Files);
    }

    [Fact]
    public void ClosingAnEntryThatIsAlreadyIngestedIsRefused()
    {
        var result = OkfCaptureWriter.Close(Dogfood, Closure("2026-08-14-equipping-agents-with-agent-skills"));

        Assert.Equal(OkfCaptureWriteOutcome.AlreadyClosed, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains("already ingested", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingAnEntryNothingNamesIsRefused()
    {
        var result = OkfCaptureWriter.Close(Dogfood, Closure("2026-08-16-never-captured"));

        Assert.Equal(OkfCaptureWriteOutcome.NoSuchEntry, result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void AnEntryIsFoundByAPathItClaimsAsWellAsByItsId()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-okapi-paper/extracted.md", "# Extracted\n");
        var open = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-paper")).Text!;

        var result = OkfCaptureWriter.Close(open, Closure("2026-08-16-okapi-paper/extracted.md"));

        Assert.Equal(OkfCaptureWriteOutcome.Closed, result.Outcome);
        Assert.Equal("2026-08-16-okapi-paper", result.Id);
    }

    [Fact]
    public void AnEntryWithNoIngestionKeyIsLeftForAHumanRatherThanGivenOne()
    {
        const string manifest = """
            {
              "manifestVersion": 1,
              "captures": [
                { "id": "2026-08-16-a-second-page", "form": "flat", "files": [] }
              ]
            }
            """;

        var result = OkfCaptureWriter.Close(manifest, Closure("2026-08-16-a-second-page"));

        Assert.Equal(OkfCaptureWriteOutcome.ManifestUnreadable, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains("carries no `ingestion` key", result.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("notdated.html", "which is not `<YYYY-MM-DD>-<slug>`")]
    [InlineData("2026-13-45-impossible.html", "which is not `<YYYY-MM-DD>-<slug>`")]
    [InlineData("2026-08-16-Shouty.html", "which is not `<YYYY-MM-DD>-<slug>`")]
    [InlineData("2026-08-16-double--hyphen.html", "which is not `<YYYY-MM-DD>-<slug>`")]
    public void AnItemWhoseNameIsNotTheIdGrammarIsRefused(string name, string expected)
    {
        using var raw = new RawZone();
        raw.Drop(name, "<html>x</html>\n");

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition(name));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains(expected, result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemOutsideRawIsRefused()
    {
        using var raw = new RawZone();
        var outside = Path.Combine(raw.Vault, "2026-08-16-elsewhere.html");
        File.WriteAllText(outside, "<html>x</html>\n");

        var result = OkfCaptureWriter.Add(
            Dogfood,
            raw.Root,
            new OkfCaptureAddition { ItemPath = outside, CapturedBy = "claude-fable/5", CapturedAt = At });

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Contains("is not under", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemNestedInsideRawIsRefusedBecauseItsIdWouldNotBeItsName()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-okapi-paper/2026-08-16-nested.html", "<html>x</html>\n");

        var result = OkfCaptureWriter.Add(
            Dogfood,
            raw.Root,
            raw.Addition("2026-08-16-okapi-paper/2026-08-16-nested.html"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Contains("nested inside raw/", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AFormThatDisagreesWithTheItemsShapeIsRefused()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>x</html>\n");

        var result = OkfCaptureWriter.Add(
            Dogfood,
            raw.Root,
            raw.Addition("2026-08-16-a-second-page.html", form: OkfCaptureForm.Packet));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Contains("--form said `packet`", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPacketDirectoryIsRefused()
    {
        using var raw = new RawZone();
        Directory.CreateDirectory(Path.Combine(raw.Root, "2026-08-16-okapi-paper"));

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-paper"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Contains("holds no files", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlatItemThatIsASymbolicLinkIsRefused()
    {
        using var raw = new RawZone();
        File.WriteAllText(Path.Combine(raw.Vault, "elsewhere.html"), "<html>elsewhere</html>\n");
        File.CreateSymbolicLink(
            Path.Combine(raw.Root, "2026-08-16-a-second-page.html"),
            Path.Combine(raw.Vault, "elsewhere.html"));

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-a-second-page.html"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains("is a symbolic link", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void APacketThatIsItselfADirectoryLinkIsRefusedRatherThanWalkedThrough()
    {
        // The hole a `Directory.Exists` check alone leaves: the link answers yes, and every
        // file found behind it would be hashed into the record under a `raw/` path.
        using var raw = new RawZone();
        Directory.CreateDirectory(Path.Combine(raw.Vault, "elsewhere"));
        File.WriteAllText(Path.Combine(raw.Vault, "elsewhere", "secret.txt"), "not in raw/\n");
        Directory.CreateSymbolicLink(
            Path.Combine(raw.Root, "2026-08-16-okapi-paper"),
            Path.Combine(raw.Vault, "elsewhere"));

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-paper"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains("is a symbolic link", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void APacketHoldingALinkedSubdirectoryIsRefusedRatherThanDescendedInto()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-okapi-paper/original.pdf", "%PDF-1.4 pretend\n");
        Directory.CreateDirectory(Path.Combine(raw.Vault, "elsewhere"));
        File.WriteAllText(Path.Combine(raw.Vault, "elsewhere", "secret.txt"), "not in raw/\n");
        Directory.CreateSymbolicLink(
            Path.Combine(raw.Root, "2026-08-16-okapi-paper", "linked"),
            Path.Combine(raw.Vault, "elsewhere"));

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-paper"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains("2026-08-16-okapi-paper/linked", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void APacketHoldingALinkedFileIsRefused()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-okapi-paper/original.pdf", "%PDF-1.4 pretend\n");
        File.WriteAllText(Path.Combine(raw.Vault, "elsewhere.md"), "# elsewhere\n");
        File.CreateSymbolicLink(
            Path.Combine(raw.Root, "2026-08-16-okapi-paper", "extracted.md"),
            Path.Combine(raw.Vault, "elsewhere.md"));

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-paper"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Null(result.Text);
        Assert.Contains("2026-08-16-okapi-paper/extracted.md", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingItemIsRefused()
    {
        using var raw = new RawZone();

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-not-there.html"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Contains("no such file or directory", result.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ringo")]
    [InlineData("human:")]
    [InlineData("")]
    public void AnActorThatIsNotASpecActorThrowsRatherThanReachingTheRecord(string actor)
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>x</html>\n");

        Assert.Throws<ArgumentException>(() => OkfCaptureWriter.Add(
            Dogfood,
            raw.Root,
            raw.Addition("2026-08-16-a-second-page.html", by: actor)));
        Assert.Throws<ArgumentException>(() => OkfCaptureWriter.Close(
            Dogfood,
            new OkfCaptureClosure
            {
                Entry = "2026-08-16-a-second-page",
                By = actor,
                At = At,
                Concepts = ["bundles/b/references/page.md"],
            }));
    }

    [Fact]
    public void ClosingWithNoConceptThrows() =>
        Assert.Throws<ArgumentException>(() => OkfCaptureWriter.Close(
            Dogfood,
            new OkfCaptureClosure
            {
                Entry = "2026-08-16-a-second-page",
                By = "claude-fable/5",
                At = At,
                Concepts = [],
            }));

    [Fact]
    public void TheCapturedAtIsTheInjectedInstantRenderedCanonically()
    {
        // AD-24: whatever offset the clock hands over, the manifest gets Z at second
        // precision, so entries sort lexicographically in the order they happened.
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>x</html>\n");

        var result = OkfCaptureWriter.Add(
            Dogfood,
            raw.Root,
            raw.Addition("2026-08-16-a-second-page.html", at: new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.FromHours(-5))));

        Assert.Contains("\"capturedAt\": \"2026-08-16T14:00:00Z\"", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteIsAtomicAndLeavesNoTemporaryFileBehind()
    {
        using var raw = new RawZone();
        var path = Path.Combine(raw.Vault, "raw", OkfCaptureManifest.FileName);

        OkfCaptureWriter.Save(path, Dogfood);

        Assert.Equal(Dogfood, File.ReadAllText(path));
        Assert.Equal(
            [OkfCaptureManifest.FileName],
            Directory.EnumerateFiles(raw.Root).Select(Path.GetFileName));
    }

    /// <summary>
    /// The temp file is cleaned up on the failing path too, which is the only path where it
    /// still exists to clean up. A directory where the manifest must go fails the move on
    /// every platform.
    /// </summary>
    [Fact]
    public void AFailedWriteLeavesNoTemporaryFileBehindEither()
    {
        using var raw = new RawZone();
        var path = Path.Combine(raw.Root, OkfCaptureManifest.FileName);
        Directory.CreateDirectory(path);

        Assert.ThrowsAny<IOException>(() => OkfCaptureWriter.Save(path, Dogfood));
        Assert.Empty(Directory.EnumerateFiles(raw.Root));
    }

    [Fact]
    public void SavingCreatesTheDirectoryWhenItIsAbsent()
    {
        using var raw = new RawZone();
        var path = Path.Combine(raw.Vault, "nowhere", "deeper", OkfCaptureManifest.FileName);

        OkfCaptureWriter.Save(path, Dogfood);

        Assert.Equal(Dogfood, File.ReadAllText(path));
    }

    /// <summary>
    /// A manifest is not required to hold only the keys okf writes, and a number that is not
    /// <c>manifestVersion</c> is not the manifest version.
    /// </summary>
    [Fact]
    public void APropertyOkfDoesNotKnowIsCarriedThroughUntouched()
    {
        const string manifest = """
            {
              "manifestVersion": 1,
              "captureCount": 3,
              "captures": []
            }
            """;
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var result = OkfCaptureWriter.Add(manifest, raw.Root, raw.Addition("2026-08-16-a-second-page.html"));

        Assert.Equal(OkfCaptureWriteOutcome.Added, result.Outcome);
        Assert.Contains("\"captureCount\": 3", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The manifest's own shape decides the indent and the line ending an entry is written
    /// with, and neither is read from a fixed offset: a manifest that opens with a blank
    /// line, and one written on a single line, both still gain a readable entry.
    /// </summary>
    [Fact]
    public void AManifestOpeningWithABlankLineGainsAReadableEntry()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var result = OkfCaptureWriter.Add("\n" + Dogfood, raw.Root, raw.Addition("2026-08-16-a-second-page.html"));

        Assert.Equal(OkfCaptureWriteOutcome.Added, result.Outcome);
        Assert.StartsWith("\n{", result.Text, StringComparison.Ordinal);
        Assert.Equal(
            2,
            OkfCaptureManifest.Parse(result.Text!, "manifest.json")!.Captures.Count);
    }

    [Fact]
    public void ACompactManifestGainsAnIndentedEntryRatherThanAFlushOne()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-a-second-page.html", "<html>second</html>\n");

        var result = OkfCaptureWriter.Add(
            """{"manifestVersion":1,"captures":[]}""",
            raw.Root,
            raw.Addition("2026-08-16-a-second-page.html"));

        Assert.Equal(OkfCaptureWriteOutcome.Added, result.Outcome);
        Assert.Contains(
            "\n    {\n      \"id\": \"2026-08-16-a-second-page\"",
            result.Text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An uningested entry re-captured under the same id is replaced even when the files it
    /// claims changed completely — the id is the entry's identity (AD-18), so a re-fetch
    /// that renamed everything must not leave two entries answering to one name.
    /// </summary>
    [Fact]
    public void ARecaptureWithAWhollyDifferentFileSetStillReplacesTheEntry()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-okapi-paper/first.md", "# First\n");
        var open = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-paper")).Text!;

        File.Delete(Path.Combine(raw.Root, "2026-08-16-okapi-paper", "first.md"));
        raw.Drop("2026-08-16-okapi-paper/second.md", "# Second\n");
        var again = OkfCaptureWriter.Add(open, raw.Root, raw.Addition("2026-08-16-okapi-paper"));

        Assert.Equal(OkfCaptureWriteOutcome.Recaptured, again.Outcome);
        Assert.Equal(
            ["2026-08-14-equipping-agents-with-agent-skills", "2026-08-16-okapi-paper"],
            OkfCaptureManifest.Parse(again.Text!, "manifest.json")!.Captures.Select(capture => capture.Id));
    }

    /// <summary>
    /// A flat capture's id is its file name with the extension taken off, which means a name
    /// with no extension keeps all of it and a packet — whose id is the whole directory
    /// name — keeps a dot rather than being truncated at it.
    /// </summary>
    [Fact]
    public void AFlatItemWithNoExtensionKeepsItsWholeNameAsTheId()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-no-extension", "plain text\n");

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-no-extension"));

        Assert.Equal(OkfCaptureWriteOutcome.Added, result.Outcome);
        Assert.Equal("2026-08-16-no-extension", result.Id);
    }

    [Fact]
    public void APacketWhoseDirectoryNameHasADotIsRefusedRatherThanTruncated()
    {
        using var raw = new RawZone();
        raw.Drop("2026-08-16-okapi-v1.2/page.md", "# Page\n");

        var result = OkfCaptureWriter.Add(Dogfood, raw.Root, raw.Addition("2026-08-16-okapi-v1.2"));

        Assert.Equal(OkfCaptureWriteOutcome.ItemRefused, result.Outcome);
        Assert.Contains("2026-08-16-okapi-v1.2", result.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-08-16-a-slug", true)]
    // The slug's first word is a word: a hyphen straight after the date is an empty one.
    [InlineData("2026-08-16--leading", false)]
    [InlineData("2026-08-16-a", true)]
    [InlineData("2026-02-30-nonexistent-day", false)]
    [InlineData("2026-08-16-", false)]
    [InlineData("2026-08-16-trailing-", false)]
    [InlineData("2026-08-16", false)]
    [InlineData("2026-08-16-under_score", false)]
    [InlineData(null, false)]
    public void TheIdGrammarIsTheOneCheckManifestEnforces(string? id, bool expected) =>
        Assert.Equal(expected, OkfCaptureWriter.IsValidId(id));

    private static OkfCaptureClosure Closure(string entry) => new()
    {
        Entry = entry,
        By = "claude-fable/5",
        At = At,
        Concepts = ["bundles/b/references/page.md"],
    };

    /// <summary>The dogfood manifest's path.</summary>
    private static string DogfoodManifestPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Okf.sln")))
            {
                return OkfCaptureManifest.PathFor(Path.Combine(directory.FullName, "okf"));
            }
        }

        throw new InvalidOperationException(
            "The tests are not running inside a checkout; the dogfood manifest has no oracle here.");
    }

    /// <summary>
    /// The digest an implementation that is not this one reports. <c>sha256sum</c> is the
    /// command the capture skill used to tell an agent to run by hand, which makes it the
    /// right oracle for the verb that replaced the instruction.
    /// </summary>
    private static string Sha256Sum(string path)
    {
        using var process = Process.Start(new ProcessStartInfo("sha256sum", [path])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("sha256sum did not start; the independent oracle is unavailable.");

        var line = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return line.Split(' ')[0];
    }

    /// <summary>
    /// A throwaway <c>&lt;vault&gt;/raw/</c>. Every test writes here and never into the
    /// repository's own vault: these are writes, and a test that mutates the dogfood
    /// manifest would be indistinguishable from the bug it is looking for.
    /// </summary>
    private sealed class RawZone : IDisposable
    {
        private readonly TempTree _tree = new();

        public RawZone() => Root = _tree.CreateDirectory(Path.Combine("okf", "raw"));

        /// <summary>The <c>raw/</c> directory.</summary>
        public string Root { get; }

        /// <summary>The vault root holding it.</summary>
        public string Vault => Path.Combine(_tree.Root, "okf");

        public void Drop(string relativePath, string content) =>
            _tree.Write(Path.Combine("okf", "raw", relativePath), content);

        public OkfCaptureAddition Addition(
            string relativePath,
            string by = "claude-fable/5",
            string? url = null,
            DateTimeOffset? at = null,
            OkfCaptureForm? form = null) => new()
            {
                ItemPath = Path.Combine(Root, relativePath),
                CapturedBy = by,
                CapturedAt = at ?? At,
                OriginalUrl = url ?? "https://example.org/a-second-page",
                Form = form,
            };

        public void Dispose() => _tree.Dispose();
    }
}
