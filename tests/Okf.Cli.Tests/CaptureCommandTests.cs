using System.Text.Json;
using Okf.Core;

namespace Okf.Cli.Tests;

/// <summary>
/// End-to-end <c>okf capture add</c> and <c>okf capture close</c> (AD-52): what reaches the
/// manifest, every refusal, and the exit-code contract (CLI-14).
/// </summary>
/// <remarks>
/// Every test writes into a throwaway vault under the system temp directory. Nothing here
/// reads or writes the repository's own <c>okf/raw/</c> or the operator's home: these are
/// the first commands in the toolset whose whole product is a mutated file, so a test that
/// leaked would be indistinguishable from the bug it looks for.
/// </remarks>
public class CaptureCommandTests
{
    [Fact]
    public void HelpExitsZeroAndSaysTheManifestIsNeverRepaired()
    {
        using var vault = new CaptureVault();
        var run = vault.Run("capture", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf capture add <file-or-directory-under-raw>", run.Output, StringComparison.Ordinal);
        Assert.Contains("LEFT AS FOUND", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsReachableFromTheTopLevelUsage()
    {
        using var vault = new CaptureVault();
        Assert.Contains("okf capture <add|close>", vault.Run("help").Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRecordsTheDigestTheHashCommandReports()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");

        var run = vault.Run(
            "capture", "add", "okf/raw/2026-08-16-a-page.html",
            "--by", "claude-fable/5",
            "--url", "https://example.org/a?x=1&y=2",
            "--title", "A Page",
            "--source-last-modified", "2026-08-01",
            "--captured-at", "2026-08-16T14:00:00Z");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("added `2026-08-16-a-page`", run.Output, StringComparison.Ordinal);

        var entry = vault.Entry("2026-08-16-a-page");
        Assert.Equal(
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(vault.Raw, "2026-08-16-a-page.html")))),
            entry.Files.Single().Sha256);
        Assert.False(entry.IsIngested);

        // The URL survives unescaped, and the instant is the canonical form (AD-24).
        var manifest = vault.Manifest();
        Assert.Contains("\"originalUrl\": \"https://example.org/a?x=1&y=2\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"capturedAt\": \"2026-08-16T14:00:00Z\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"sourceLastModified\": \"2026-08-01\"", manifest, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void TheWrittenManifestSatisfiesTheCustodianCheck()
    {
        // The script specified these invariants first and remains the belt-and-braces gate
        // beside `okf lint`, so it — not this repository's reading of it — is the oracle
        // for whether a CLI-written entry is well formed.
        Skip.IfNot(
            Tooling.IsOnPath("python3"),
            "python3 not installed — check-manifest.py oracle skipped (the dogfood CI job runs it)");

        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.Drop("2026-08-16-okapi-paper/original.pdf", "%PDF pretend\n");
        vault.Drop("2026-08-16-okapi-paper/extracted.md", "# Extracted\n");
        vault.Concept("bundles/b/references/page.md");

        vault.Add("2026-08-16-a-page.html");
        vault.Add("2026-08-16-okapi-paper");
        vault.Run(
            "capture", "close", "2026-08-16-a-page",
            "--concept", "bundles/b/references/page.md",
            "--by", "claude-fable/5",
            "--at", "2026-08-16T15:00:00Z");

        var (exitCode, output) = vault.CheckManifest();
        Assert.Equal(0, exitCode);
        Assert.Contains("Checked 2 captures (3 files hashed, 1 awaiting ingestion): 0 violations.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AddIsWhatCreatesTheManifestInAVaultWithoutOne()
    {
        using var vault = new CaptureVault(withManifest: false);
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");

        Assert.Equal(CliApplication.ExitSuccess, vault.Add("2026-08-16-a-page.html").ExitCode);
        Assert.StartsWith("{\n  \"manifestVersion\": 1,\n  \"captures\": [\n", vault.Manifest(), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonReportsTheEntryAsItNowStandsInTheManifest()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");

        var run = vault.Run(
            "capture", "add", "okf/raw/2026-08-16-a-page.html", "--by", "claude-fable/5", "--json");

        using var report = JsonDocument.Parse(run.Output);
        Assert.Equal("added", report.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("2026-08-16-a-page", report.RootElement.GetProperty("id").GetString());
        Assert.False(report.RootElement.GetProperty("ingested").GetBoolean());
        Assert.Equal(
            "2026-08-16-a-page.html",
            report.RootElement.GetProperty("files")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void CloseSetsTheIngestionAndTheEntryBecomesImmutable()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.Concept("bundles/b/references/page.md");
        vault.Add("2026-08-16-a-page.html");

        var run = vault.Run(
            "capture", "close", "2026-08-16-a-page",
            "--concept", "bundles/b/references/page.md",
            "--by", "claude-fable/5",
            "--at", "2026-08-16T15:00:00Z");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("closed `2026-08-16-a-page` into 1 concept", run.Output, StringComparison.Ordinal);
        Assert.True(vault.Entry("2026-08-16-a-page").IsIngested);

        // Immutability starts at the close, so a re-capture is now refused where a moment
        // ago it would have replaced the entry.
        var again = vault.Add("2026-08-16-a-page.html");
        Assert.Equal(CliApplication.ExitDiagnostics, again.ExitCode);
        Assert.Contains("already captured and ingested", again.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingAnAlreadyClosedEntryIsRefusedAndTheManifestDoesNotMove()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.Concept("bundles/b/references/page.md");
        vault.Add("2026-08-16-a-page.html");
        vault.Run("capture", "close", "2026-08-16-a-page", "--concept", "bundles/b/references/page.md", "--by", "claude-fable/5");
        var before = vault.Manifest();

        var run = vault.Run(
            "capture", "close", "2026-08-16-a-page", "--concept", "bundles/b/references/page.md", "--by", "claude-opus/4");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("already ingested", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, vault.Manifest());
    }

    [Fact]
    public void AConceptThatDoesNotExistIsRefusedBeforeTheManifestIsTouched()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.Concept("bundles/b/references/page.md");
        vault.Add("2026-08-16-a-page.html");
        var before = vault.Manifest();

        var run = vault.Run(
            "capture", "close", "2026-08-16-a-page",
            "--concept", "bundles/b/references/page.md",
            "--concept", "bundles/b/references/missing.md",
            "--by", "claude-fable/5");

        // One bad pointer refuses the whole close: `ingestion` cannot be reopened, so a
        // half-correct one would be permanent.
        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("names no file under", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, vault.Manifest());
        Assert.False(vault.Entry("2026-08-16-a-page").IsIngested);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.md")]
    [InlineData("bundles/../../outside.md")]
    public void AConceptPathThatLeavesTheVaultIsRefused(string concept)
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.Add("2026-08-16-a-page.html");
        var before = vault.Manifest();

        var run = vault.Run(
            "capture", "close", "2026-08-16-a-page", "--concept", concept, "--by", "claude-fable/5");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Equal(before, vault.Manifest());
    }

    [Fact]
    public void AManifestThatDoesNotParseIsReportedAndLeftExactlyAsFound()
    {
        // AD-18, the rule this whole verb is built around: the immutability record is never
        // rewritten to make it parse.
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.Concept("bundles/b/references/page.md");
        const string broken = "{ \"manifestVersion\": 1, \"captures\": [ oops\n";
        vault.WriteManifest(broken);

        var added = vault.Add("2026-08-16-a-page.html");
        var closed = vault.Run(
            "capture", "close", "2026-08-16-a-page",
            "--concept", "bundles/b/references/page.md",
            "--by", "claude-fable/5");

        Assert.Equal(CliApplication.ExitUsage, added.ExitCode);
        Assert.Equal(CliApplication.ExitUsage, closed.ExitCode);
        Assert.Contains("does not read as a capture manifest", added.Error, StringComparison.Ordinal);
        Assert.Contains("does not read as a capture manifest", closed.Error, StringComparison.Ordinal);
        Assert.Equal(broken, vault.Manifest());
    }

    [Fact]
    public void AByteOrderMarkOnTheManifestIsCarriedThroughTheWrite()
    {
        // `File.ReadAllText` eats a BOM and the writer emits none, so without care the one
        // verb that promises to move no byte but its own would silently delete three.
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.WriteManifest("\uFEFF" + OkfCaptureWriter.EmptyManifest);

        var run = vault.Add("2026-08-16-a-page.html");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal([0xEF, 0xBB, 0xBF], vault.ManifestBytes()[..3]);
        Assert.Equal("2026-08-16-a-page", vault.Entry("2026-08-16-a-page").Id);
    }

    [Fact]
    public void AnEntryNothingNamesIsAUsageFailure()
    {
        using var vault = new CaptureVault();
        vault.Concept("bundles/b/references/page.md");

        var run = vault.Run(
            "capture", "close", "2026-08-16-never-captured",
            "--concept", "bundles/b/references/page.md",
            "--by", "claude-fable/5");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no capture entry has the id", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAByActorNothingIsWritten()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        var before = vault.Manifest();

        var run = vault.Run("capture", "add", "okf/raw/2026-08-16-a-page.html");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no `--by` actor", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, vault.Manifest());
    }

    [Theory]
    [InlineData("ringo")]
    [InlineData("human:")]
    [InlineData("Human:ringo")]
    public void AByThatIsNotASpecActorIsRefused(string actor)
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        var before = vault.Manifest();

        var run = vault.Run("capture", "add", "okf/raw/2026-08-16-a-page.html", "--by", actor);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("is not an actor", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, vault.Manifest());
    }

    [Theory]
    [InlineData("2026-08-16T14:00:00-05:00")]
    [InlineData("2026-08-16 14:00:00Z")]
    [InlineData("today")]
    public void APinnedInstantThatIsNotTheCanonicalFormIsRefused(string instant)
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");

        var run = vault.Run(
            "capture", "add", "okf/raw/2026-08-16-a-page.html", "--by", "claude-fable/5", "--captured-at", instant);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("is not a canonical instant", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--nope")]
    [InlineData("--by")]
    [InlineData("--form=sideways")]
    [InlineData("--source-last-modified=yesterday")]
    public void AMalformedCommandLineIsAUsageFailure(string argument)
    {
        using var vault = new CaptureVault();
        var run = vault.Run("capture", "add", "okf/raw/x.html", argument);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASubcommandIsRequired()
    {
        using var vault = new CaptureVault();
        var run = vault.Run("capture");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no subcommand", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSubcommandIsAUsageFailure()
    {
        using var vault = new CaptureVault();
        var run = vault.Run("capture", "remove", "2026-08-16-a-page");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("unknown capture subcommand 'remove'", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingWithNoConceptIsRefused()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");
        vault.Add("2026-08-16-a-page.html");
        var before = vault.Manifest();

        var run = vault.Run("capture", "close", "2026-08-16-a-page", "--by", "claude-fable/5");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no `--concept` named", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, vault.Manifest());
    }

    /// <summary>
    /// Each subcommand names exactly one thing. Zero and two are both refused, and the
    /// refusal counts what was named, because "exactly one" is the invariant the manifest's
    /// per-entry shape rests on (AD-52).
    /// </summary>
    [Theory]
    [InlineData(new string[0], "records exactly one item, and 0 items were named.")]
    [InlineData(new[] { "a.html", "b.html" }, "records exactly one item, and 2 items were named.")]
    public void AddRecordsExactlyOneItem(string[] operands, string expected)
    {
        using var vault = new CaptureVault();
        var run = vault.Run(["capture", "add", .. operands, "--by", "claude-fable/5"]);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new string[0], "closes exactly one entry, and 0 entries were named.")]
    [InlineData(new[] { "one", "two" }, "closes exactly one entry, and 2 entries were named.")]
    public void CloseClosesExactlyOneEntry(string[] operands, string expected)
    {
        using var vault = new CaptureVault();
        var run = vault.Run(["capture", "close", .. operands, "--by", "claude-fable/5", "--concept", "c.md"]);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// `--concept` records which concepts now carry an ingested artifact, so it belongs to
    /// the close and is refused on the add: a fresh capture is uningested by definition.
    /// </summary>
    [Fact]
    public void AConceptOnTheAddIsRefused()
    {
        using var vault = new CaptureVault();
        vault.Drop("2026-08-16-a-page.html", "<html>a page</html>\n");

        var run = vault.Run(
            "capture", "add", "okf/raw/2026-08-16-a-page.html",
            "--by", "claude-fable/5",
            "--concept", "bundles/b/page.md");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("`--concept` belongs to `okf capture close`", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Closing an ingestion in a vault where nothing was ever captured names the manifest
    /// it looked for, rather than reporting the entry as merely unknown.
    /// </summary>
    [Fact]
    public void ClosingWithNoManifestNamesTheManifestItLookedFor()
    {
        using var vault = new CaptureVault(withManifest: false);
        vault.Concept("bundles/b/page.md");

        var run = vault.Run(
            "capture", "close", "2026-08-16-a-page",
            "--by", "claude-fable/5",
            "--concept", "bundles/b/page.md");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no capture manifest at", run.Error, StringComparison.Ordinal);
        Assert.Contains("nothing has been captured.", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A throwaway vault: <c>okf/bundles/b/</c>, <c>okf/raw/</c>, and a home directory that
    /// holds no okf configuration. Nothing outside this tree is read or written.
    /// </summary>
    private sealed class CaptureVault : IDisposable
    {
        private readonly TempTree tree = new();

        public CaptureVault(bool withManifest = true)
        {
            this.tree.CreateDirectory(Path.Combine("okf", "bundles", "b"));
            Raw = this.tree.CreateDirectory(Path.Combine("okf", "raw"));
            Home = this.tree.CreateDirectory("home");

            if (withManifest)
            {
                WriteManifest(OkfCaptureWriter.EmptyManifest);
            }
        }

        /// <summary>The vault's <c>raw/</c> directory.</summary>
        public string Raw { get; }

        /// <summary>A home directory with no okf configuration in it.</summary>
        public string Home { get; }

        private string Vault => Path.Combine(this.tree.Root, "okf");

        public CliRun Run(params string[] args) => Cli.RunIn(this.tree.Root, Home, args);

        public CliRun Add(string relativePath) =>
            Run("capture", "add", $"okf/raw/{relativePath}", "--by", "claude-fable/5", "--captured-at", "2026-08-16T14:00:00Z");

        public void Drop(string relativePath, string content) =>
            this.tree.Write(Path.Combine("okf", "raw", relativePath), content);

        public void Concept(string vaultRelativePath) =>
            this.tree.Write(
                Path.Combine("okf", vaultRelativePath),
                "---\ntype: Reference\ntitle: Page\n---\n\nBody.\n");

        public void WriteManifest(string text) =>
            this.tree.Write(Path.Combine("okf", "raw", OkfCaptureManifest.FileName), text);

        public string Manifest() => File.ReadAllText(OkfCaptureManifest.PathFor(Vault));

        public byte[] ManifestBytes() => File.ReadAllBytes(OkfCaptureManifest.PathFor(Vault));

        public OkfCaptureEntry Entry(string id) =>
            OkfCaptureManifest.TryLoad(Vault, out _)!.Captures.Single(entry => entry.Id == id);

        /// <summary>Runs the repository's own <c>check-manifest.py</c> against this vault.</summary>
        public (int ExitCode, string Output) CheckManifest()
        {
            var script = Path.Combine(
                Repository.Root
                    ?? throw new InvalidOperationException("The tests are not running inside a checkout."),
                "okf",
                "custodian",
                "check-manifest.py");

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "python3",
                [script, Vault])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("python3 did not start.");

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }

        public void Dispose() => this.tree.Dispose();
    }
}
