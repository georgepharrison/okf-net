namespace Okf.Core.Tests;

/// <summary>
/// <c>OKF0310</c>, the raw-item-mutation rule (PRD CLI-9). It is the only rule scoped to
/// the <em>vault</em> rather than to a bundle root, because <c>raw/</c> sits outside every
/// bundle root by construction (decisions.md Q3).
/// </summary>
/// <remarks>
/// The expected behaviour is taken from <c>okf/custodian/check-manifest.py</c>, which
/// specified these invariants before any of this existed, and from the milestone decision
/// that named it the rule's specification — not from the C# under test. The script's
/// division of labour is kept: this rule reports <em>mutation of an ingested artifact</em>
/// and nothing else; manifest structure stays the script's job.
/// </remarks>
public class OkfRawImmutabilityTests
{
    private const string Captured = "the bytes that were retrieved\n";

    [Fact]
    public void AnIngestedCaptureThatStillMatchesItsRecordedHashIsSilent()
    {
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha);

        Assert.Empty(vault.LintIds());
    }

    [Fact]
    public void AnIngestedCaptureWhoseBytesChangedIsReported()
    {
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha);
        vault.WriteRaw("2026-06-01-thing.html", "something else entirely\n");

        var diagnostic = Assert.Single(vault.Lint());

        Assert.Equal(OkfRules.RawItemMutated, diagnostic.RuleId);
        Assert.Equal(OkfSeverity.Warning, diagnostic.Severity);
        Assert.Equal(vault.ManifestPath, diagnostic.Path);
        Assert.Contains("2026-06-01-thing.html", diagnostic.Message, StringComparison.Ordinal);

        // The capture id too: a packet's files are named after it, and the entry is what
        // a reader has to go and look at.
        Assert.Contains("capture `2026-06-01-thing`", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDiagnosticPointsAtTheRecordedHashInTheManifest()
    {
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha);
        vault.WriteRaw("2026-06-01-thing.html", "something else entirely\n");

        var diagnostic = Assert.Single(vault.Lint());
        var line = File.ReadAllLines(vault.ManifestPath)[diagnostic.Line!.Value - 1];

        Assert.Contains(sha, line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIngestedCaptureWhoseFileIsGoneIsReported()
    {
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha);
        File.Delete(Path.Combine(vault.RawDirectory, "2026-06-01-thing.html"));

        var diagnostic = Assert.Single(vault.Lint());
        var line = File.ReadAllLines(vault.ManifestPath)[diagnostic.Line!.Value - 1];

        Assert.Equal(OkfRules.RawItemMutated, diagnostic.RuleId);
        Assert.Contains("no longer in raw/", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(sha, line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUningestedCaptureIsNotYetImmutable()
    {
        // Q3 makes immutability a property of an item *after ingestion*: an open entry is
        // the custodian's work queue, and re-capturing a page before anything cited it is
        // ordinary work, not a broken record.
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha, ingested: false);
        vault.WriteRaw("2026-06-01-thing.html", "a better capture of the same page\n");

        Assert.Empty(vault.LintIds());
    }

    [Fact]
    public void AVaultWithNoManifestIsSilent()
    {
        using var vault = new TempVault();
        vault.WriteRaw("2026-06-01-thing.html", Captured);

        Assert.Empty(vault.LintIds());
    }

    [Fact]
    public void AManifestThatDoesNotParseIsLeftToTheScript()
    {
        // check-manifest.py reports an unparseable manifest and repairs nothing. This rule
        // answers one question — did an ingested artifact change — and a manifest it
        // cannot read is not an answer of "yes".
        using var vault = new TempVault();
        vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteManifest("{ this is not json");

        Assert.Empty(vault.LintIds());
    }

    [Fact]
    public void AnEntryWhosePathEscapesRawIsLeftToTheScript()
    {
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "../../escaped.html", sha);

        Assert.Empty(vault.LintIds());
    }

    [Fact]
    public void AHostilePathIsRefusedRatherThanThrown()
    {
        // A manifest is a file anyone can write, and a lint run must not become a crash
        // because one of them names a path the filesystem API refuses to answer about at
        // all: Path.GetFullPath throws on an embedded NUL rather than returning.
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);

        // The NUL rides in as a JSON escape, so the manifest itself parses cleanly and
        // the rule really does reach the path — a raw control character would make this
        // an unparseable-manifest test wearing the wrong name.
        vault.WriteManifest($$"""
            {
              "manifestVersion": 1,
              "captures": [
                {
                  "id": "2026-06-01-thing",
                  "form": "flat",
                  "files": [{ "path": "2026-06-01-\u0000thing.html", "sha256": "{{sha}}" }],
                  "capturedAt": "2026-06-01T00:00:00Z",
                  "capturedBy": "tests",
                  "ingestion": { "at": "2026-06-01T00:00:00Z", "by": "tests", "concepts": ["bundles/bundle/x.md"] }
                }
              ]
            }
            """);

        Assert.Empty(vault.LintIds());
    }

    [Fact]
    public void WithoutAVaultTheRuleIsInapplicableRatherThanFailing()
    {
        // A foreign bundle handed to `okf lint` by path has no vault around it, so there
        // is no raw/ to judge — PRD CLI-9's "reports as inapplicable rather than failing".
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha);
        vault.WriteRaw("2026-06-01-thing.html", "mutated\n");

        var diagnostics = new OkfLinter(new OkfLintOptions { Today = TempBundle.Today })
            .Lint(new OkfBundle(vault.BundleRoot))
            .Diagnostics;

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TheRuleIsPromotableToError()
    {
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha);
        vault.WriteRaw("2026-06-01-thing.html", "mutated\n");

        var layer = new OkfSeverityLayer("test");
        layer.Severities[OkfRules.RawItemMutated] = OkfSeverity.Error;

        var diagnostic = Assert.Single(vault.Lint(new OkfLintOptions
        {
            Today = TempBundle.Today,
            Severities = new OkfSeverityResolver([layer]),
        }));

        Assert.Equal(OkfSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void AtHiddenSeverityNothingIsReported()
    {
        using var vault = new TempVault();
        var sha = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", sha);
        vault.WriteRaw("2026-06-01-thing.html", "mutated\n");

        var layer = new OkfSeverityLayer("test");
        layer.Severities[OkfRules.RawItemMutated] = OkfSeverity.Hidden;

        Assert.Empty(vault.Lint(new OkfLintOptions
        {
            Today = TempBundle.Today,
            Severities = new OkfSeverityResolver([layer]),
        }));
    }

    [Fact]
    public void EveryFileOfAPacketIsChecked()
    {
        using var vault = new TempVault();
        var original = vault.WriteRaw("2026-06-01-paper/original.pdf", "%PDF-1.4 pretend\n");
        var extracted = vault.WriteRaw("2026-06-01-paper/extracted.md", "# Extracted\n");
        vault.WriteManifest($$"""
            {
              "manifestVersion": 1,
              "captures": [
                {
                  "id": "2026-06-01-paper",
                  "form": "packet",
                  "files": [
                    { "path": "2026-06-01-paper/original.pdf", "sha256": "{{original}}" },
                    { "path": "2026-06-01-paper/extracted.md", "sha256": "{{extracted}}" }
                  ],
                  "capturedAt": "2026-06-01T00:00:00Z",
                  "capturedBy": "tests",
                  "ingestion": { "at": "2026-06-01T00:00:00Z", "by": "tests", "concepts": ["bundles/bundle/x.md"] }
                }
              ]
            }
            """);
        vault.WriteRaw("2026-06-01-paper/extracted.md", "# Rewritten\n");

        var diagnostic = Assert.Single(vault.Lint());

        Assert.Contains("2026-06-01-paper/extracted.md", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuleIsCataloguedAsAWarningInTheHygieneRange()
    {
        var rule = OkfRules.Get(OkfRules.RawItemMutated);

        Assert.Equal(OkfRuleCategory.Hygiene, rule.Category);
        Assert.Equal(OkfSeverity.Warning, rule.DefaultSeverity);
        Assert.Equal("raw-item-mutated", rule.Nickname);
    }

    /// <summary>
    /// The finding quotes both hashes short enough to read. A sha256 is 64 characters, but
    /// the recorded one is whatever the manifest says — the message itself allows that the
    /// record, not the artifact, is what changed — so a short or truncated value has to
    /// come out whole rather than be cut at a length it does not have.
    /// </summary>
    /// <param name="recorded">The sha256 the manifest records.</param>
    /// <param name="quoted">How the finding should quote it.</param>
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("0123456789ab", "0123456789ab")]
    [InlineData("0123456789abc", "0123456789ab\u2026")]
    public void ARecordedHashIsQuotedWholeUnlessItIsLongerThanTwelveCharacters(string recorded, string quoted)
    {
        using var vault = new TempVault();
        var actual = vault.WriteRaw("2026-06-01-thing.html", Captured);
        vault.WriteCapture("2026-06-01-thing", "2026-06-01-thing.html", recorded);

        var diagnostic = Assert.Single(vault.Lint());

        Assert.Contains($"(recorded {quoted}, on disk {actual[..12]}\u2026)", diagnostic.Message, StringComparison.Ordinal);
    }
}
