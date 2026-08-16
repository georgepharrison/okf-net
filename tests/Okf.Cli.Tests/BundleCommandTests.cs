using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Okf.Core;

namespace Okf.Cli.Tests;

/// <summary>
/// <c>okf bundle</c> end to end (work item #5): what ships, what is warned about, what
/// <c>--verify</c> and <c>--lint</c> say, and the exit codes a release job branches on.
/// </summary>
[Collection(BundleLintCollection.Name)]
public class BundleCommandTests
{
    /// <summary>A pinned stamp, so nothing here depends on the clock.</summary>
    private const string Stamp = "2026-08-15T14:00:00Z";

    [Fact]
    public void HelpDescribesWhatIsStrippedAndExitsZero()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "bundle", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf-bundle.json", run.Output, StringComparison.Ordinal);
        Assert.Contains("custodian/", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "bundle" }, "--out")]
    [InlineData(new[] { "bundle", "--out" }, "requires a value")]
    [InlineData(new[] { "bundle", "--out", "a", "--format", "rar" }, "Unknown --format value")]
    [InlineData(new[] { "bundle", "--out", "a", "--generated-at", "yesterday" }, "not a readable instant")]
    [InlineData(new[] { "bundle", "--out", "a", "--out", "b" }, "was given twice")]
    [InlineData(new[] { "bundle", "--verify", "a", "--out", "b" }, "cannot be combined with")]
    [InlineData(new[] { "bundle", "--out", "a", "one", "two" }, "at most one path")]
    [InlineData(new[] { "bundle", "--out", "a", "--nope" }, "Unknown option")]
    public void MalformedArgumentsAreUsageFailures(string[] args, string expected)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, args);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagesTheVaultsBundlesAndStripsEverythingElse()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", archive, "--generated-at", Stamp);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        var names = TarNames(archive);
        Assert.Contains("bundles/alpha/index.md", names);
        Assert.Contains("bundles/beta/gadgets.md", names);
        Assert.Contains(OkfDistributionManifest.FileName, names);
        Assert.DoesNotContain(names, name => name.Contains("custodian", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("raw/", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.EndsWith("okf.json", StringComparison.Ordinal));
        Assert.DoesNotContain("README.md", names);
    }

    [Fact]
    public void ReportsWhatItPackagedAndThatNothingDangles()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", archive, "--generated-at", Stamp);

        Assert.Contains("Packaged 2 bundles", run.Output, StringComparison.Ordinal);
        Assert.Contains("No link leaves the packaged bundles.", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASubsetWarnsOnStderrAndRecordsTheDanglingLink()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "alpha.tar.gz");

        var run = Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            archive,
            "--bundle",
            "alpha",
            "--generated-at",
            Stamp);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("bundles/alpha/topics/widgets.md:", run.Error, StringComparison.Ordinal);
        Assert.Contains("into bundle `beta`", run.Error, StringComparison.Ordinal);
        Assert.Contains("1 link leaves the packaged bundles", run.Output, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(TarEntry(archive, OkfDistributionManifest.FileName));
        var link = Assert.Single(document.RootElement.GetProperty("externalLinks").EnumerateArray());
        Assert.Equal("beta", link.GetProperty("bundle").GetString());
        Assert.Equal(["alpha"], document.RootElement.GetProperty("bundles").EnumerateArray()
            .Select(value => value.GetString()));
    }

    [Fact]
    public void AnUnknownBundleNameIsRefusedBeforeAnythingIsWritten()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", archive, "--bundle", "gamma");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("No bundle named 'gamma'", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public void TheFormatFollowsTheOutputNameUnlessItIsGiven()
    {
        using var vault = new DistributableVault();
        var zipped = Path.Combine(vault.Output, "dist.zip");
        var named = Path.Combine(vault.Output, "dist.bin");

        Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", zipped, "--generated-at", Stamp);
        Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            named,
            "--format",
            "zip",
            "--generated-at",
            Stamp);

        Assert.Equal(File.ReadAllBytes(zipped), File.ReadAllBytes(named));
        using var zip = ZipFile.OpenRead(zipped);
        Assert.Contains(zip.Entries, entry => entry.FullName == "bundles/alpha/index.md");
    }

    [Fact]
    public void OptionsAreAcceptedInTheirInlineFormToo()
    {
        using var vault = new DistributableVault();
        var spaced = Path.Combine(vault.Output, "spaced.zip");

        // Also a directory that does not exist yet: an --out naming a new path has to
        // create the tree on the way, which is what a release job's `artifacts/` is.
        var inline = Path.Combine(vault.Output, "nested", "deeper", "inline.zip");

        Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            spaced,
            "--format",
            "zip",
            "--bundle",
            "beta",
            "--generated-at",
            Stamp);
        var run = Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            $"--out={inline}",
            "--format=zip",
            "--bundle=beta",
            $"--generated-at={Stamp}");
        var verified = Cli.RunIn(vault.Project, vault.Home, "bundle", $"--verify={inline}");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Equal(File.ReadAllBytes(spaced), File.ReadAllBytes(inline));
        Assert.Equal(CliApplication.ExitSuccess, verified.ExitCode);
    }

    [Fact]
    public void VerifyingSomethingThatIsNotAnArchiveFailsWithASentence()
    {
        using var vault = new DistributableVault();
        var impostor = Path.Combine(vault.Output, "download.tar.gz");
        File.WriteAllText(impostor, "<html>404 Not Found</html>\n");

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", "--verify", impostor);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("unreadable", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stamp with no offset is read as UTC, not as the packaging machine's local time —
    /// otherwise the same command on two laptops writes two different manifests, which is
    /// the one thing the reproducibility claim cannot survive.
    /// </summary>
    [Fact]
    public void AStampWithNoOffsetIsReadAsUtcAndRenderedCanonically()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            archive,
            "--generated-at",
            "2026-08-15T14:00:00");

        using var document = JsonDocument.Parse(TarEntry(archive, OkfDistributionManifest.FileName));
        Assert.Equal(Stamp, document.RootElement.GetProperty("generatedAt").GetString());
    }

    [Fact]
    public void TheStampGoesIntoTheManifestExactlyAsGiven()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", archive, "--generated-at", "2026-01-02T03:04:05Z");

        using var document = JsonDocument.Parse(TarEntry(archive, OkfDistributionManifest.FileName));
        Assert.Equal("2026-01-02T03:04:05Z", document.RootElement.GetProperty("generatedAt").GetString());
    }

    [Fact]
    public void VerboseReportsResolutionAndTheDestinationOnStderr()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var quiet = Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", archive, "--generated-at", Stamp);
        var loud = Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            archive,
            "--verbose",
            "--generated-at",
            Stamp);

        Assert.DoesNotContain("okf: resolved", quiet.Error, StringComparison.Ordinal);
        Assert.Contains("okf: resolved vault", loud.Error, StringComparison.Ordinal);
        Assert.Contains("okf: writing tar.gz to", loud.Error, StringComparison.Ordinal);
        Assert.Equal(
            2,
            loud.Error.Split('\n').Count(line => line.StartsWith("okf: packaging ", StringComparison.Ordinal)));
    }

    [Fact]
    public void WithoutTheFlagNothingIsLinted()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", archive, "--generated-at", Stamp);

        Assert.DoesNotContain("Linting the distribution", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectoryDistributionIsLintedWhereItWasWritten()
    {
        using var vault = new DistributableVault();
        var directory = Path.Combine(vault.Output, "dist");

        var run = Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            directory,
            "--format",
            "dir",
            "--lint",
            "--generated-at",
            Stamp);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("Linting the distribution", run.Output, StringComparison.Ordinal);
        // Paths are reported relative to the distribution, which is what a consumer sees.
        Assert.Contains("Checked 6 files in 2 bundles", run.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(directory, OkfDistributionManifest.FileName)));
    }

    [Fact]
    public void VerifyPassesOnWhatWasJustPackaged()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");
        Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", archive, "--generated-at", Stamp);

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", "--verify", archive);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("every file matches its recorded sha256", run.Output, StringComparison.Ordinal);
        Assert.Contains($"packaged by okf/{CliApplication.Version}", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyFailsOnATamperedDirectoryAndNamesTheFile()
    {
        using var vault = new DistributableVault();
        var directory = Path.Combine(vault.Output, "dist");
        Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            directory,
            "--format",
            "dir",
            "--generated-at",
            Stamp);
        File.AppendAllText(Path.Combine(directory, "bundles", "beta", "gadgets.md"), "\nsmuggled\n");

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", "--verify", directory);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("bundles/beta/gadgets.md: modified", run.Output, StringComparison.Ordinal);
        Assert.Contains("1 problem.", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("every file matches", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyRefusesSomethingTheBundlerNeverWrote()
    {
        using var vault = new DistributableVault();

        var run = Cli.RunIn(vault.Project, vault.Home, "bundle", "--verify", vault.Root);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("carries no okf-bundle.json", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void LintReportsTheDistributionAsAConsumerSeesIt()
    {
        using var vault = new DistributableVault();
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var run = Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            archive,
            "--lint",
            "--generated-at",
            Stamp);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("Checked ", run.Output, StringComparison.Ordinal);
        Assert.Contains("0 errors", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The vault's own config promotes OKF0301 to error, and the distribution carries no
    /// config at all — so a concept with no description fails inside the vault and passes
    /// as a stranger's bundle. That difference is the whole point of linting the output
    /// separately, and it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void LintOfTheDistributionUsesDefaultSeveritiesNotTheVaultsConfig()
    {
        using var vault = new DistributableVault();
        vault.Write("bundles/beta/undescribed.md", "---\ntype: Concept\ntitle: Undescribed\n---\n\nNo description.\n");
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var packaged = Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            archive,
            "--lint",
            "--generated-at",
            Stamp);
        var linted = Cli.RunIn(vault.Project, vault.Home, "lint", vault.Root);

        Assert.Equal(CliApplication.ExitSuccess, packaged.ExitCode);
        Assert.Contains("warning OKF0301", packaged.Output, StringComparison.Ordinal);
        Assert.Equal(CliApplication.ExitDiagnostics, linted.ExitCode);
        Assert.Contains("error OKF0301", linted.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void LintFailsTheRunWhenTheDistributionWouldNotConform()
    {
        using var vault = new DistributableVault();
        vault.Write("bundles/beta/broken.md", "no frontmatter at all\n");
        var archive = Path.Combine(vault.Output, "dist.tar.gz");

        var run = Cli.RunIn(
            vault.Project,
            vault.Home,
            "bundle",
            vault.Root,
            "--out",
            archive,
            "--lint",
            "--generated-at",
            Stamp);

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("OKF0001", run.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public void TheSameVaultAndStampProduceAByteIdenticalArchive()
    {
        using var vault = new DistributableVault();
        var first = Path.Combine(vault.Output, "first.tar.gz");
        var second = Path.Combine(vault.Output, "second.tar.gz");

        Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", first, "--generated-at", Stamp);
        Cli.RunIn(vault.Project, vault.Home, "bundle", vault.Root, "--out", second, "--generated-at", Stamp);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    /// <summary>
    /// ACC-5's vault, packaged and read back the way a stranger would: the dogfood bundle
    /// has to survive its own bundler, conform with no configuration to lean on, and
    /// verify against the manifest it shipped with.
    /// </summary>
    [SkippableFact]
    public void TheDogfoodVaultPackagesVerifiesAndLintsClean()
    {
        Skip.If(Repository.DogfoodVault is null, "not running from a checkout");
        using var output = new TempTree();
        var archive = Path.Combine(output.Root, "okf-net-knowledge.tar.gz");

        var packaged = Cli.RunIn(
            output.Root,
            output.Root,
            "bundle",
            Repository.DogfoodVault!,
            "--out",
            archive,
            "--lint",
            "--generated-at",
            Stamp);
        var verified = Cli.RunIn(output.Root, output.Root, "bundle", "--verify", archive);

        Assert.Equal(CliApplication.ExitSuccess, packaged.ExitCode);
        Assert.Contains("0 errors", packaged.Output, StringComparison.Ordinal);
        Assert.Equal(CliApplication.ExitSuccess, verified.ExitCode);
        Assert.Contains("every file matches its recorded sha256", verified.Output, StringComparison.Ordinal);
    }

    private static List<string> TarNames(string archive)
    {
        var names = new List<string>();
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        return names;
    }

    private static string TarEntry(string archive, string name)
    {
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                using var data = new MemoryStream();
                entry.DataStream!.CopyTo(data);
                return Encoding.UTF8.GetString(data.ToArray());
            }
        }

        throw new InvalidOperationException($"'{name}' is not in '{archive}'.");
    }

    /// <summary>A vault with two bundles, a cross-bundle link, and the machinery a bundler strips.</summary>
    private sealed class DistributableVault : IDisposable
    {
        private readonly TempTree tree = new();

        public DistributableVault()
        {
            Home = this.tree.CreateDirectory("home");
            Project = this.tree.CreateDirectory("project");
            Root = Path.Combine(Project, OkfDiscovery.VaultDirectoryName);
            Output = this.tree.CreateDirectory("out");

            Write("bundles/alpha/index.md", "<!-- generated by okf -->\n\n# Guide\n\n* [About](about-this-bundle.md) - About alpha.\n");
            Write("bundles/alpha/about-this-bundle.md", Concept("About Alpha", "The alpha bundle."));
            Write("bundles/alpha/topics/index.md", "<!-- generated by okf -->\n\n# Concept\n\n* [Widgets](widgets.md) - Widgets.\n");
            Write(
                "bundles/alpha/topics/widgets.md",
                Concept("Widgets", "Widgets, and [gadgets](../../beta/gadgets.md) next door."));
            Write("bundles/beta/index.md", "<!-- generated by okf -->\n\n# Concept\n\n* [Gadgets](gadgets.md) - Gadgets.\n");
            Write("bundles/beta/gadgets.md", Concept("Gadgets", "Gadgets."));

            Write("okf.json", "{ \"lint\": { \"severities\": { \"OKF0301\": \"error\" } } }");
            Write("README.md", "# Vault readme.\n");
            Write("custodian/recipe.json", "{}");
            Write("raw/manifest.json", "{ \"manifestVersion\": 1, \"captures\": [] }");
        }

        /// <summary>The home directory, holding no okf configuration.</summary>
        public string Home { get; }

        /// <summary>The project root, which the vault sits inside.</summary>
        public string Project { get; }

        /// <summary>The vault root.</summary>
        public string Root { get; }

        /// <summary>A directory for distributions, outside the vault.</summary>
        public string Output { get; }

        public void Write(string relativePath, string content) =>
            this.tree.Write(
                Path.Combine("project", OkfDiscovery.VaultDirectoryName, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                content);

        public void Dispose() => this.tree.Dispose();

        private static string Concept(string title, string body) =>
            $"---\ntype: Concept\ntitle: {title}\ndescription: A fixture concept.\n---\n\n{body}\n";
    }
}
