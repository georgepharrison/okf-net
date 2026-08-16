using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Okf.Core;

namespace Okf.Core.Tests;

/// <summary>
/// Resolving, verifying and installing an upgrade (work item #23).
/// </summary>
/// <remarks>
/// <b>No test in this file touches the network.</b> Every one supplies its own
/// <see cref="OkfUpgrade.Fetch" />, which is the reason that seam exists: AD-7's exception
/// is confined to one file, and a suite that reached the artifact host would make the
/// toolset's gates depend on a host being up. The one test that does speak HTTP
/// (<see cref="OkfUpgradeHttpTests" />) serves itself over loopback.
/// </remarks>
public sealed class OkfUpgradeTests
{
    private const string OldBytes = "the binary that is running";
    private const string NewBytes = "the binary the release manifest names";

    // ---------------------------------------------------------------------
    // Which asset this machine gets — install.sh's `case` block, in C#.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("linux", Architecture.X64, "okf-linux-x64")]
    [InlineData("osx", Architecture.Arm64, "okf-osx-arm64")]
    [InlineData("windows", Architecture.X64, "okf-win-x64.exe")]
    public void SelectsTheAssetForThePlatform(string platform, Architecture architecture, string expected) =>
        Assert.Equal(expected, OkfUpgrade.AssetName(Platform(platform), architecture));

    /// <summary>
    /// Refused BY NAME, which is the whole point of deciding the platform first: an upgrade
    /// that fetched a linux-x64 binary onto an arm64 machine and then failed with "cannot
    /// execute binary file" would have told the user nothing about why.
    /// </summary>
    [Theory]
    [InlineData("linux", Architecture.Arm64, "linux-x86_64")]
    [InlineData("linux", Architecture.X86, "linux-x86_64")]
    [InlineData("windows", Architecture.Arm64, "win-x64")]
    public void RefusesAnArchitectureWithNoBuild(string platform, Architecture architecture, string names)
    {
        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgrade.AssetName(Platform(platform), architecture));

        Assert.Contains(names, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(architecture.ToString().ToLowerInvariant(), refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An Intel Mac is a different refusal from an unsupported architecture, and install.sh
    /// makes the same distinction: Rosetta translates x86_64 to arm64, not the other way, so
    /// there is nothing to fall back to and the owner needs somewhere to ask instead.
    /// </summary>
    [Fact]
    public void RefusesAnIntelMacWithSomewhereToAsk()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgrade.AssetName(OSPlatform.OSX, Architecture.X64));

        Assert.Contains("no Intel-Mac build", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("issues/36", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectsAnAssetForTheMachineRunningTheSuite()
    {
        // Whatever CI or a workstation is, a release ships for it — otherwise the suite
        // could not be running this binary in the first place.
        Assert.Contains(
            OkfUpgrade.AssetName(),
            (string[])[OkfUpgrade.LinuxAsset, OkfUpgrade.MacAsset, OkfUpgrade.WindowsAsset]);
    }

    // ---------------------------------------------------------------------
    // The base URL and the URLs built from it.
    // ---------------------------------------------------------------------

    [Fact]
    public void RefusesACleartextBaseUrl()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.BaseUri("http://example.invalid"));

        Assert.Contains("refusing to upgrade over http", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("loopback only", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://example.invalid")]
    [InlineData("file:///tmp/www")]
    public void RefusesABaseUrlThatIsNotHttp(string baseUrl) =>
        Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.BaseUri(baseUrl));

    [Fact]
    public void RefusesSomethingThatIsNotAUrl() =>
        Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.BaseUri("get.okf.tychostation.dev"));

    /// <summary>
    /// Cleartext to loopback is allowed, and only there: it is what lets the acceptance
    /// path serve a fixture release from `python3 -m http.server` instead of only ever
    /// being testable against the real host.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://[::1]:8080")]
    public void AllowsCleartextToLoopback(string baseUrl) =>
        Assert.Equal("http", OkfUpgrade.BaseUri(baseUrl).Scheme);

    /// <summary>
    /// EVERY trailing slash, not one: `https://host//latest.json` is a different URL to most
    /// caches and some servers, and a base URL ending `//` is an ordinary copy-paste.
    /// install.sh loops for the same reason and install.ps1 uses TrimEnd; the three read the
    /// same environment variable and may not disagree about what it means.
    /// </summary>
    [Theory]
    [InlineData("https://get.okf.tychostation.dev")]
    [InlineData("https://get.okf.tychostation.dev/")]
    [InlineData("https://get.okf.tychostation.dev///")]
    public void TrimsEveryTrailingSlashFromTheBaseUrl(string baseUrl) =>
        Assert.Equal(
            "https://get.okf.tychostation.dev/latest.json",
            OkfUpgrade.ManifestUri(OkfUpgrade.BaseUri(baseUrl), null).ToString());

    /// <summary>
    /// The host's layout, verified against the host itself: the newest release sits at the
    /// root and every published release keeps its own copy under `v&lt;version&gt;/`.
    /// </summary>
    [Theory]
    [InlineData(null, "https://get.okf.tychostation.dev/latest.json")]
    [InlineData("1.0.0", "https://get.okf.tychostation.dev/v1.0.0/latest.json")]
    [InlineData("1.1.0-rc.1", "https://get.okf.tychostation.dev/v1.1.0-rc.1/latest.json")]
    [InlineData("v1.0.0", "https://get.okf.tychostation.dev/v1.0.0/latest.json")]
    public void BuildsTheManifestUrlTheHostServes(string? version, string expected) =>
        Assert.Equal(
            expected,
            OkfUpgrade.ManifestUri(OkfUpgrade.BaseUri(OkfUpgradeOptions.DefaultBaseUrl), version).ToString());

    [Fact]
    public void ResolvesAnAssetAgainstTheBaseUrlAndNotTheRegistry()
    {
        var asset = new OkfUpgradeAsset(
            "okf-linux-x64",
            "v1.0.0/okf-linux-x64",
            new string('a', 64),
            1,
            "https://gitlab.tychostation.dev/api/v4/projects/12/packages/generic/okf/1.0.0/okf-linux-x64");

        var uri = OkfUpgrade.AssetUri(OkfUpgrade.BaseUri("https://mirror.example/okf/"), asset);

        Assert.Equal("https://mirror.example/okf/v1.0.0/okf-linux-x64", uri.ToString());
        Assert.DoesNotContain("gitlab", uri.ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Redirects — curl's `--proto-redir '=https'`, by hand.
    // ---------------------------------------------------------------------

    [Fact]
    public void RefusesARedirectFromHttpsToHttp()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.ResolveRedirect(
            new Uri("https://get.okf.tychostation.dev/latest.json"),
            new Uri("http://evil.example/latest.json"),
            httpsOnly: true));

        Assert.Contains("refusing a redirect from https to http", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FollowsARedirectFromHttpsToHttps() =>
        Assert.Equal(
            "https://mirror.example/latest.json",
            OkfUpgrade.ResolveRedirect(
                new Uri("https://get.okf.tychostation.dev/latest.json"),
                new Uri("https://mirror.example/latest.json"),
                httpsOnly: true).ToString());

    /// <summary>
    /// A relative Location is legal and ordinary, and it must be resolved against the URL
    /// that answered — otherwise a hop within the same host looks like no hop at all.
    /// </summary>
    [Fact]
    public void ResolvesARelativeLocationAgainstTheRequest() =>
        Assert.Equal(
            "https://get.okf.tychostation.dev/v1.0.0/latest.json",
            OkfUpgrade.ResolveRedirect(
                new Uri("https://get.okf.tychostation.dev/latest.json"),
                new Uri("/v1.0.0/latest.json", UriKind.Relative),
                httpsOnly: true).ToString());

    /// <summary>
    /// A request that started on http may stay on http. Pinning the scheme to whatever the
    /// caller asked for is the honest rule — no silent upgrade, and no silent downgrade.
    /// </summary>
    [Fact]
    public void LeavesAnHttpRequestOnHttp() =>
        Assert.Equal(
            "http://127.0.0.1:9/b",
            OkfUpgrade.ResolveRedirect(
                new Uri("http://127.0.0.1:9/a"),
                new Uri("http://127.0.0.1:9/b"),
                httpsOnly: false).ToString());

    [Fact]
    public void RefusesARedirectWithNoLocation()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.ResolveRedirect(
            new Uri("https://get.okf.tychostation.dev/latest.json"), null, httpsOnly: true));

        Assert.Contains("no Location", refusal.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // The swap. The Windows half is unit-tested through the planner because this
    // repository has no Windows runner (AD-42's own caveat about install.ps1).
    // ---------------------------------------------------------------------

    [Fact]
    public void SwapsInOneRenameWhereARunningBinaryCanBeOverwritten()
    {
        var steps = OkfUpgrade.PlanSwap("/home/x/.local/bin/okf", "/home/x/.local/bin/.okf.upgrade.ab", windows: false);

        var step = Assert.Single(steps);
        Assert.Equal(OkfUpgradeStepKind.Install, step.Kind);
        Assert.Equal("/home/x/.local/bin/.okf.upgrade.ab", step.Source);
        Assert.Equal("/home/x/.local/bin/okf", step.Destination);
    }

    /// <summary>
    /// Windows locks a loaded image against overwrite but permits it to be renamed, so the
    /// running binary is moved aside FIRST and the new one takes its name after. Getting
    /// the order wrong is an "access denied" on the one platform CI cannot run.
    /// </summary>
    [Fact]
    public void RetiresTheRunningBinaryFirstWhereItCannotBeOverwritten()
    {
        var steps = OkfUpgrade.PlanSwap(@"C:\okf\bin\okf.exe", @"C:\okf\bin\.okf.upgrade.ab", windows: true);

        Assert.Equal(2, steps.Count);
        Assert.Equal(OkfUpgradeStepKind.Retire, steps[0].Kind);
        Assert.Equal(@"C:\okf\bin\okf.exe", steps[0].Source);
        Assert.Equal(@"C:\okf\bin\okf.exe.old", steps[0].Destination);
        Assert.Equal(OkfUpgradeStepKind.Install, steps[1].Kind);
        Assert.Equal(@"C:\okf\bin\.okf.upgrade.ab", steps[1].Source);
        Assert.Equal(@"C:\okf\bin\okf.exe", steps[1].Destination);
    }

    [Fact]
    public void NamesTheRetiredBinaryAfterTheOneItReplaces()
    {
        Assert.Equal(@"C:\okf\bin\okf.exe.old", OkfUpgrade.RetiredPath(@"C:\okf\bin\okf.exe"));
        Assert.Equal("/home/x/.local/bin/okf.old", OkfUpgrade.RetiredPath("/home/x/.local/bin/okf"));
    }

    [Fact]
    public void DeletesARetiredBinaryLeftByAPreviousUpgrade()
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "okf");
        File.WriteAllText(OkfUpgrade.RetiredPath(target), "yesterday's okf");

        Assert.True(OkfUpgrade.RemoveRetired(target));
        Assert.False(File.Exists(OkfUpgrade.RetiredPath(target)));

        // Idempotent: the ordinary case is that there is nothing to clean up.
        Assert.False(OkfUpgrade.RemoveRetired(target));
    }

    // ---------------------------------------------------------------------
    // Resolve.
    // ---------------------------------------------------------------------

    [Fact]
    public void ResolvesTheNewestReleaseWithoutDownloadingIt()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var downloads = 0;
        var host = new FakeHost(NewBytes) { OnAsset = () => downloads++ };

        var plan = OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch);

        Assert.Equal("1.1.0-rc.1", plan.AvailableVersion);
        Assert.Equal("1.0.0", plan.CurrentVersion);
        Assert.Equal(OkfUpgrade.AssetName(), plan.Asset.Name);
        Assert.False(plan.IsUpToDate);
        Assert.Equal(0, downloads);
        Assert.Equal(OldBytes, File.ReadAllText(target));
    }

    [Fact]
    public void ReportsBeingUpToDateWhenTheManifestNamesTheRunningVersion() =>
        Assert.True(WithFakeHost("1.1.0-rc.1").IsUpToDate);

    [Fact]
    public void ReportsAnUnstampedBuildAsUpgradable()
    {
        var plan = WithFakeHost("0.0.0-dev+9765a305");

        Assert.True(plan.IsDevelopmentBuild);
        Assert.False(plan.IsUpToDate);
    }

    [Fact]
    public void RefusesWhenTheReleaseDoesNotDescribeThePinnedVersion()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes);

        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgrade.Resolve(Options(target, "1.0.0", pinned: "9.9.9"), host.Fetch));

        Assert.Contains("asked for 9.9.9", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAReleaseWithNoAssetForThisPlatform()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes) { Manifest = """{"version":"2.0.0","assets":{}}""" };

        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch));

        Assert.Contains(OkfUpgrade.AssetName(), refusal.Message, StringComparison.Ordinal);
        Assert.Contains("2.0.0", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAManifestThatCannotBeRead()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes) { Manifest = "<html>404</html>" };

        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch));

        Assert.Contains("latest.json", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("not valid JSON", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A path that climbs out of the base URL is refused rather than fetched. `Uri` collapses
    /// `..` as it parses, so `../../evil` under `https://mirror.example/okf` resolves to
    /// `https://mirror.example/evil` — still the right host, because the URL is built by
    /// appending, but no longer the subtree the operator pointed `OKF_INSTALL_URL` at. On a
    /// mirror that serves anything besides okf, that is the difference between "the release
    /// directory" and "anywhere on this host".
    /// </summary>
    [Theory]
    [InlineData("../../evil")]
    [InlineData("/../evil")]
    [InlineData("v1.0.0/../../../evil")]
    public void RefusesAnAssetPathThatClimbsOutOfTheBaseUrl(string path)
    {
        var asset = new OkfUpgradeAsset("okf-linux-x64", path, new string('a', 64), 1, null);

        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgrade.AssetUri(OkfUpgrade.BaseUri("https://mirror.example/okf"), asset));

        Assert.Contains("outside https://mirror.example/okf", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPinnedVersionThatClimbsOutOfTheBaseUrl()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgrade.ManifestUri(OkfUpgrade.BaseUri("https://mirror.example/okf"), "1.0.0/../../.."));

        Assert.Contains("outside https://mirror.example/okf", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A manifest is a few kilobytes of JSON, and it is read into a string. Without a bound
    /// a host answering `latest.json` with an endless body turns that read into an
    /// out-of-memory, which is a failure mode a self-replacing binary should not have.
    /// </summary>
    [Fact]
    public void RefusesAManifestBiggerThanAnyManifest()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);

        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Resolve(
            Options(target, "1.0.0"),
            _ => new EndlessStream()));

        Assert.Contains("bigger than", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("a few kilobytes", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(OldBytes, File.ReadAllText(target));
    }

    /// <summary>
    /// The cap is a cap and not a fence one byte inside it: a manifest of exactly
    /// <see cref="OkfUpgrade.MaximumManifestBytes" /> is read, and the byte after that is
    /// where the refusal starts.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void TheManifestSizeCapIsRefusedOnlyOnceItIsPassed(int overBy, bool expectRead)
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes)
        {
            Manifest = PaddedManifest(OkfUpgrade.MaximumManifestBytes + overBy),
        };

        if (expectRead)
        {
            Assert.Equal("1.1.0-rc.1", OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch).AvailableVersion);
            return;
        }

        Assert.Contains(
            "bigger than",
            Assert.Throws<OkfUpgradeException>(
                () => OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The staging file is a sibling of the target, so a target with no directory to be a
    /// sibling in is refused before anything is fetched.
    /// </summary>
    [Fact]
    public void ATargetWithNoDirectoryIsRefusedBeforeAnythingIsFetched()
    {
        var host = new FakeHost(NewBytes);
        var plan = new OkfUpgradePlan(
            "1.0.0",
            "1.1.0-rc.1",
            new OkfUpgradeAsset("okf-linux-x64", "v1.1.0-rc.1/okf-linux-x64", Digest(NewBytes), 1, null),
            new Uri($"{FakeHost.BaseUrl}/latest.json"),
            new Uri($"{FakeHost.BaseUrl}/v1.1.0-rc.1/okf-linux-x64"),
            "okf");
        var fetched = 0;

        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Apply(
            plan,
            uri =>
            {
                fetched++;
                return host.Fetch(uri);
            }));

        Assert.Contains("has no directory to stage a download in", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, fetched);
    }

    /// <summary>A valid manifest padded with one string property to an exact byte count.</summary>
    private static string PaddedManifest(int bytes)
    {
        const string anchor = "\"version\":";
        var manifest = new FakeHost(NewBytes).Manifest.Replace(
            anchor,
            "\"note\": \"\",\n  " + anchor,
            StringComparison.Ordinal);

        return manifest.Replace(
            "\"note\": \"\"",
            "\"note\": \"" + new string('x', bytes - Encoding.UTF8.GetByteCount(manifest)) + "\"",
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Apply.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The staging file is written into the directory the user's binary lives in, so an
    /// unbounded copy hands a hostile or broken host that filesystem: an endless body was
    /// measured writing 7.4 GB in five seconds before anything was verified. The download is
    /// abandoned at <see cref="OkfUpgrade.MaximumAssetBytes" /> instead, the target is
    /// untouched, and no staging file survives.
    /// </summary>
    [Fact]
    public void AbandonsADownloadBiggerThanAnyRelease()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes);
        var plan = OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch);

        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Apply(
            plan,
            uri => uri.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal)
                ? host.Fetch(uri)
                : new EndlessStream()));

        Assert.Contains($"bigger than {OkfUpgrade.MaximumAssetBytes / (1024 * 1024)} MB", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(OldBytes, File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(tree.Root, ".okf.upgrade.*"));
    }

    /// <summary>
    /// AD-19 writes a digest as 64 lowercase hex digits and the `publish` job obeys it, but
    /// the comparison is the last gate before a binary is renamed over the running one — so
    /// a manifest that shouted its digest must still verify rather than silently refuse a
    /// release that is in fact the right one.
    /// </summary>
    [Fact]
    public void VerifiesADigestTheManifestWroteInUppercase()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes)
        {
            Manifest = FakeHost.ManifestFor("1.1.0-rc.1", Digest(NewBytes).ToUpperInvariant()),
        };

        OkfUpgrade.Apply(OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch), host.Fetch);

        Assert.Equal(NewBytes, File.ReadAllText(target));
    }

    [Fact]
    public void ReplacesTheRunningBinaryWithTheVerifiedBytes()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes);

        var result = OkfUpgrade.Apply(OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch), host.Fetch);

        Assert.Equal(NewBytes, File.ReadAllText(target));
        Assert.Equal("1.1.0-rc.1", result.Version);
        Assert.Null(result.RetiredPath);
        Assert.Empty(Directory.GetFiles(tree.Root, ".okf.upgrade.*"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
                File.GetUnixFileMode(target));
        }
    }

    /// <summary>
    /// A mismatch is fatal, prints both digests, and leaves the target byte-identical to
    /// what it was. The two hashes are what tells a truncated download apart from the wrong
    /// file; `sha256sum -c` would say only FAILED.
    /// </summary>
    [Fact]
    public void RefusesBytesThatDoNotMatchTheManifestAndLeavesTheTargetAlone()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var before = File.ReadAllBytes(target);
        var host = new FakeHost(NewBytes) { ServeInstead = "something else entirely" };

        var plan = OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch);
        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Apply(plan, host.Fetch));

        Assert.Contains("sha256 mismatch", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Digest(NewBytes), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Digest("something else entirely"), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.Empty(Directory.GetFiles(tree.Root, ".okf.upgrade.*"));
    }

    /// <summary>
    /// A download that dies part-way leaves the target holding the OLD bytes and no staging
    /// file behind — the property the stage-then-rename order exists for.
    /// </summary>
    [Fact]
    public void LeavesTheOldBytesAndNoStagingFileWhenTheDownloadFails()
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var before = File.ReadAllBytes(target);
        var host = new FakeHost(NewBytes) { OnAsset = () => throw new IOException("connection reset") };

        var plan = OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch);
        Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Apply(plan, host.Fetch));

        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.Empty(Directory.GetFiles(tree.Root, ".okf.upgrade.*"));
    }

    /// <summary>
    /// Launched as `dotnet okf.dll` the running process is the .NET host, and
    /// `Environment.ProcessPath` names it — verified empirically during #23, where this
    /// refusal is what stopped a verified okf being renamed over the machine's `dotnet`.
    /// The target's NAME is therefore checked before anything is downloaded.
    /// </summary>
    [Fact]
    public void RefusesToReplaceSomethingThatIsNotOkf()
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "dotnet");
        File.WriteAllText(target, OldBytes);
        var host = new FakeHost(NewBytes);
        var plan = OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch);

        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Apply(plan, host.Fetch));

        Assert.Contains("is 'dotnet', not 'okf'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet okf.dll", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(OldBytes, File.ReadAllText(target));
    }

    /// <summary>
    /// The upgrade writes into the directory the binary already lives in, so a directory it
    /// cannot write is a refusal that names the way out — reinstalling somewhere the user
    /// owns — rather than a bare "access denied".
    /// </summary>
    [SkippableFact]
    public void RefusesAnInstallDirectoryItCannotWrite()
    {
        Skip.If(OperatingSystem.IsWindows(), "POSIX permissions decide this; Windows ACLs do not.");
        Skip.If(Environment.UserName == "root", "root writes to a mode-0555 directory regardless.");

        // Restated for the platform-compatibility analyzer, which cannot read `Skip.If`.
        // The call above has already ended the test on Windows.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var tree = new TempTree();
        var directory = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "okf");
        File.WriteAllText(target, OldBytes);
        var host = new FakeHost(NewBytes);
        var plan = OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch);

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        try
        {
            var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgrade.Apply(plan, host.Fetch));

            Assert.Contains("cannot write to", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("install.sh", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(OldBytes, File.ReadAllText(target));
        }
        finally
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Nothing outside the running binary's own directory is written — not the working
    /// directory, not a temp directory, not a sibling of either.
    /// </summary>
    [Fact]
    public void WritesNothingOutsideTheBinarysOwnDirectory()
    {
        using var tree = new TempTree();
        var directory = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "okf");
        File.WriteAllText(target, OldBytes);
        var host = new FakeHost(NewBytes);

        OkfUpgrade.Apply(OkfUpgrade.Resolve(Options(target, "1.0.0"), host.Fetch), host.Fetch);

        Assert.Equal(
            (string[])[target],
            Directory.GetFiles(tree.Root, "*", SearchOption.AllDirectories));
    }

    // ---------------------------------------------------------------------

    private static OSPlatform Platform(string name) => name switch
    {
        "linux" => OSPlatform.Linux,
        "osx" => OSPlatform.OSX,
        _ => OSPlatform.Windows,
    };

    private static string Install(TempTree tree, string bytes)
    {
        var target = Path.Combine(tree.Root, OperatingSystem.IsWindows() ? "okf.exe" : "okf");
        File.WriteAllText(target, bytes);
        return target;
    }

    private static OkfUpgradeOptions Options(string target, string current, string? pinned = null) => new()
    {
        BaseUrl = FakeHost.BaseUrl,
        CurrentVersion = current,
        ExecutablePath = target,
        Version = pinned,
    };

    private static OkfUpgradePlan WithFakeHost(string currentVersion)
    {
        using var tree = new TempTree();
        var target = Install(tree, OldBytes);
        var host = new FakeHost(NewBytes);
        return OkfUpgrade.Resolve(Options(target, currentVersion), host.Fetch);
    }

    internal static string Digest(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// A body that never ends — what a hostile or broken artifact host answers with. Reads
    /// succeed forever and cost nothing, so what the test measures is whether okf stops.
    /// </summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => count;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A release host in memory: one manifest naming one asset per platform, and the bytes
    /// to serve for it. No socket, no port, no host — the seam AD-7's exception is confined
    /// behind.
    /// </summary>
    private sealed class FakeHost
    {
        public const string BaseUrl = "https://fixture.example/okf";

        private readonly string assetBytes;

        public FakeHost(string assetBytes)
        {
            this.assetBytes = assetBytes;
            Manifest = ManifestFor("1.1.0-rc.1", Digest(assetBytes));
        }

        /// <summary>What `latest.json` answers with.</summary>
        public string Manifest { get; init; }

        /// <summary>Bytes to serve instead of the ones the manifest describes.</summary>
        public string? ServeInstead { get; init; }

        /// <summary>Runs when the asset is requested — a counter, or a failure to inject.</summary>
        public Action? OnAsset { get; init; }

        public Stream Fetch(Uri uri)
        {
            if (uri.AbsolutePath.EndsWith("latest.json", StringComparison.Ordinal))
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(Manifest));
            }

            OnAsset?.Invoke();
            return new MemoryStream(Encoding.UTF8.GetBytes(ServeInstead ?? this.assetBytes));
        }

        public static string ManifestFor(string version, string digest) =>
            $$"""
            {
              "version": "{{version}}",
              "tag": "v{{version}}",
              "generatedAt": "2026-08-16T04:27:30Z",
              "assets": {
                "okf-linux-x64":   { "path": "v{{version}}/okf-linux-x64",   "size": 1, "sha256": "{{digest}}" },
                "okf-osx-arm64":   { "path": "v{{version}}/okf-osx-arm64",   "size": 1, "sha256": "{{digest}}" },
                "okf-win-x64.exe": { "path": "v{{version}}/okf-win-x64.exe", "size": 1, "sha256": "{{digest}}" }
              }
            }
            """;
    }
}
