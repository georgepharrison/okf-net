using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Okf.Core;

namespace Okf.Cli.Tests.Upgrade;

/// <summary>
/// <c>okf upgrade</c>'s rendering and exit codes (work item #23).
/// </summary>
/// <remarks>
/// Every case supplies its own fetch and its own executable path, so no test reaches the
/// artifact host and none of them can replace the test runner. What Core already decides —
/// asset selection, digests, the swap — is asserted in <c>OkfUpgradeTests</c>; this file
/// asserts what the adapter owes (AD-6): the exit code, the JSON contract, and the lines a
/// person reads.
/// </remarks>
public sealed class UpgradeCommandTests
{
    private const string NewBytes = "the binary the release manifest names";
    private const string BaseUrl = "https://fixture.example/okf";

    [Fact]
    public void CheckExitsZeroWhenTheRunningVersionIsTheAvailableOne()
    {
        var run = Upgrade("1.1.0-rc.1", "--check", available: "1.1.0-rc.1");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf is up to date", run.Output, StringComparison.Ordinal);
        Assert.Contains("available: 1.1.0-rc.1", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("an upgrade is available", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// AD-5: exit 1 is a mechanical gate, so a shell can branch on "an upgrade is available"
    /// without parsing a word of output.
    /// </summary>
    [Fact]
    public void CheckExitsOneWhenAnUpgradeIsAvailable()
    {
        var run = Upgrade("1.0.0", "--check", available: "1.1.0-rc.1");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("current:   1.0.0", run.Output, StringComparison.Ordinal);
        Assert.Contains("available: 1.1.0-rc.1", run.Output, StringComparison.Ordinal);
        Assert.Contains("an upgrade is available", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A release candidate is NOT an upgrade over the release it precedes (semver §11), so
    /// `--check` on a stable binary against an rc manifest is exit 0.
    /// </summary>
    [Fact]
    public void CheckDoesNotOfferAPrereleaseAsAnUpgradeOverItsRelease()
    {
        var run = Upgrade("1.1.0", "--check", available: "1.1.0-rc.1");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf is up to date", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckSaysWhenTheRunningBuildIsUnstamped()
    {
        var run = Upgrade("0.0.0-dev+9765a305", "--check", available: "1.1.0-rc.1");

        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        Assert.Contains("unstamped local build", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckDownloadsNothingAndLeavesTheBinaryAlone()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            ["--check"], Environment(), output, error, Self(host, target));

        Assert.Equal(CliApplication.ExitDiagnostics, exitCode);
        Assert.Equal(0, host.AssetRequests);
        Assert.Equal("the binary that is running", File.ReadAllText(target));
    }

    [Fact]
    public void DryRunResolvesAndWritesNothing()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            ["--dry-run"], Environment(), output, error, Self(host, target));

        Assert.Equal(CliApplication.ExitSuccess, exitCode);
        Assert.Contains("--dry-run: nothing was downloaded or written", output.ToString(), StringComparison.Ordinal);
        Assert.Contains($"{BaseUrl}/latest.json", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(target, output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, host.AssetRequests);
        Assert.Equal("the binary that is running", File.ReadAllText(target));
    }

    [Fact]
    public void UpgradeReplacesTheBinaryAndSaysWhatItDid()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            [], Environment(), output, error, Self(host, target));

        Assert.Equal(CliApplication.ExitSuccess, exitCode);
        Assert.Equal(NewBytes, File.ReadAllText(target));
        Assert.Contains("sha256 verified", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("==> upgraded", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("-> 1.1.0-rc.1", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeDownloadsNothingWhenItIsAlreadyTheNewest()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            [],
            Environment(),
            output,
            error,
            new UpgradeRuntime { Fetch = host.Fetch, ExecutablePath = target, Version = "1.1.0-rc.1" });

        Assert.Equal(CliApplication.ExitSuccess, exitCode);
        Assert.Equal(0, host.AssetRequests);
        Assert.Contains("nothing to do", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("the binary that is running", File.ReadAllText(target));
    }

    /// <summary>
    /// `--version` is a re-install as much as an upgrade — pinning back to an older release
    /// is how a tester bisects one — so it downloads even when the version already matches.
    /// </summary>
    [Fact]
    public void APinnedVersionInstallsEvenWhenItIsTheRunningOne()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.0.0");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            ["--version", "v1.0.0"], Environment(), output, error, Self(host, target));

        Assert.Equal(CliApplication.ExitSuccess, exitCode);
        Assert.Equal(1, host.AssetRequests);
        Assert.Equal($"{BaseUrl}/v1.0.0/latest.json", host.ManifestRequest);
        Assert.Equal(NewBytes, File.ReadAllText(target));
    }

    [Fact]
    public void JsonReportsTheContract()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            ["--check", "--json"], Environment(), output, error, Self(host, target));

        Assert.Equal(CliApplication.ExitDiagnostics, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal(CliApplication.Version, root.GetProperty("current").GetString());
        Assert.Equal("1.1.0-rc.1", root.GetProperty("available").GetString());
        Assert.Equal(OkfUpgrade.AssetName(), root.GetProperty("asset").GetString());
        Assert.Equal(
            $"{BaseUrl}/v1.1.0-rc.1/{OkfUpgrade.AssetName()}",
            root.GetProperty("url").GetString());
        Assert.Equal(FakeHost.Digest(NewBytes), root.GetProperty("sha256").GetString());
        Assert.False(root.GetProperty("upToDate").GetBoolean());
    }

    /// <summary>
    /// A cleartext base URL is refused before a request is made, not after: a digest
    /// fetched over the same channel as the bytes it describes proves nothing.
    /// </summary>
    [Fact]
    public void RefusesACleartextBaseUrl()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            ["--check"],
            Environment("http://artifacts.example"),
            output,
            error,
            Self(host, target));

        Assert.Equal(CliApplication.ExitUsage, exitCode);
        Assert.Contains("refusing to upgrade over http", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, host.ManifestRequests);
    }

    /// <summary>
    /// The same variable install.sh and install.ps1 read, so a machine installed from a
    /// mirror upgrades from the same mirror rather than silently from the default host.
    /// </summary>
    [Fact]
    public void ReadsTheBaseUrlFromTheInstallersEnvironmentVariable()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        UpgradeCommand.Run(
            ["--check"],
            Environment("https://mirror.example/okf/"),
            output,
            error,
            Self(host, target));

        Assert.Equal("https://mirror.example/okf/latest.json", host.ManifestRequest);
    }

    [Fact]
    public void RefusesBytesThatDoNotMatchTheManifest()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1") { ServeInstead = "something else entirely" };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run([], Environment(), output, error, Self(host, target));

        Assert.Equal(CliApplication.ExitUsage, exitCode);
        Assert.Contains("sha256 mismatch", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("the binary that is running", File.ReadAllText(target));
    }

    /// <summary>
    /// The channel is declared and its second URL is not, so `rc` says so instead of
    /// inventing a path the host would answer with a 404.
    /// </summary>
    [Fact]
    public void SaysThatTheRcChannelIsReserved()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = UpgradeCommand.Run(
            ["--check", "--channel", "rc"], Environment(), output, error, Self(host, target));

        Assert.Equal(CliApplication.ExitDiagnostics, exitCode);
        Assert.Contains("reserved", error.ToString(), StringComparison.Ordinal);
        Assert.Equal($"{BaseUrl}/latest.json", host.ManifestRequest);
    }

    [Fact]
    public void StableIsTheDefaultAndSaysNothing()
    {
        using var tree = new TempTree();
        var target = Install(tree);
        var host = new FakeHost("1.1.0-rc.1");
        using var output = new StringWriter();
        using var error = new StringWriter();

        UpgradeCommand.Run(["--check"], Environment(), output, error, Self(host, target));

        Assert.DoesNotContain("reserved", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--wat")]
    [InlineData("--channel")]
    [InlineData("--channel=nightly")]
    [InlineData("--version")]
    public void BadUsageExitsTwo(string argument)
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "upgrade", argument);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void APinnedVersionAndAChannelIsAContradiction()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "upgrade", "--version", "1.0.0", "--channel", "rc");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("give one or the other", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpIsReachableThroughTheVerbAndNeedsNoNetwork()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "upgrade", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf upgrade", run.Output, StringComparison.Ordinal);
        Assert.Contains("OKF_INSTALL_URL", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVerbIsListedInTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf upgrade", run.Output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------

    private static OkfEnvironment Environment(string baseUrl = BaseUrl) =>
        new(Path.GetTempPath(), [new KeyValuePair<string, string>(OkfUpgradeOptions.BaseUrlVariable, baseUrl)]);

    private static string Install(TempTree tree)
    {
        var target = Path.Combine(tree.Root, OperatingSystem.IsWindows() ? "okf.exe" : "okf");
        File.WriteAllText(target, "the binary that is running");
        return target;
    }

    private static UpgradeRuntime Self(FakeHost host, string target) =>
        new() { Fetch = host.Fetch, ExecutablePath = target };

    /// <summary>
    /// One <c>--check</c> against a described running binary. Both versions are supplied,
    /// because the exit code is decided by the PAIR: a suite whose own assembly is stamped
    /// <c>0.0.0-dev</c> could otherwise never reach the up-to-date branch.
    /// </summary>
    private static CliRun Upgrade(string current, string flag, string available)
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, OperatingSystem.IsWindows() ? "okf.exe" : "okf");
        File.WriteAllText(target, "the binary that is running");

        var host = new FakeHost(available);
        using var output = new StringWriter { NewLine = "\n" };
        using var error = new StringWriter { NewLine = "\n" };
        var exitCode = UpgradeCommand.Run(
            [flag],
            Environment(),
            output,
            error,
            new UpgradeRuntime { Fetch = host.Fetch, ExecutablePath = target, Version = current });

        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>A release host in memory. No socket, no port, no artifact host.</summary>
    private sealed class FakeHost
    {
        private readonly string version;

        public FakeHost(string version) => this.version = version;

        /// <summary>Bytes to serve instead of the ones the manifest describes.</summary>
        public string? ServeInstead { get; init; }

        /// <summary>How many times the asset was downloaded.</summary>
        public int AssetRequests { get; private set; }

        /// <summary>How many times a manifest was requested.</summary>
        public int ManifestRequests { get; private set; }

        /// <summary>The manifest URL last requested.</summary>
        public string? ManifestRequest { get; private set; }

        public Stream Fetch(Uri uri)
        {
            if (uri.AbsoluteUri.EndsWith("latest.json", StringComparison.Ordinal))
            {
                ManifestRequests++;
                ManifestRequest = uri.AbsoluteUri;
                return Text(Manifest());
            }

            AssetRequests++;
            return Text(ServeInstead ?? NewBytes);
        }

        public static string Digest(string content) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        private static Stream Text(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

        private string Manifest()
        {
            var digest = Digest(NewBytes);
            return $$"""
                {
                  "version": "{{this.version}}",
                  "tag": "v{{this.version}}",
                  "generatedAt": "2026-08-16T04:27:30Z",
                  "assets": {
                    "okf-linux-x64":   { "path": "v{{this.version}}/okf-linux-x64",   "sha256": "{{digest}}" },
                    "okf-osx-arm64":   { "path": "v{{this.version}}/okf-osx-arm64",   "sha256": "{{digest}}" },
                    "okf-win-x64.exe": { "path": "v{{this.version}}/okf-win-x64.exe", "sha256": "{{digest}}" }
                  }
                }
                """;
        }
    }
}
