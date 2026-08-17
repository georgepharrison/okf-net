using Okf.Core;

namespace Okf.Core.Tests.Upgrade;

/// <summary>
/// Reading a release manifest (work item #23).
/// </summary>
/// <remarks>
/// The first test's oracle is the producer, not this repository's idea of the producer:
/// <c>fixtures/latest.json</c> is a byte copy of what
/// <c>https://get.okf.tychostation.dev/latest.json</c> was serving, so the assertions below
/// describe a file the `publish` job actually wrote. That is what makes them worth
/// anything — a fixture written to match the reader would agree with it forever.
/// </remarks>
public sealed class OkfUpgradeManifestTests
{
    private static string PublishedManifest =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "latest.json"));

    [Fact]
    public void ReadsTheManifestTheArtifactHostServes()
    {
        var manifest = OkfUpgradeManifest.Parse(PublishedManifest);

        Assert.Equal("1.1.0-rc.1", manifest.Version);
        Assert.Equal("v1.1.0-rc.1", manifest.Tag);
        Assert.Equal("2026-08-16T04:27:30Z", manifest.GeneratedAt);

        // AD-42's eight assets under one package version, less `latest.json`, which does
        // not list itself.
        Assert.Equal(7, manifest.Assets.Count);
        Assert.Contains("okf-linux-x64", manifest.Assets.Keys);
        Assert.Contains("okf-osx-arm64", manifest.Assets.Keys);
        Assert.Contains("okf-win-x64.exe", manifest.Assets.Keys);
        Assert.Contains("install.sh", manifest.Assets.Keys);
    }

    [Fact]
    public void ReadsAnAssetsPathDigestAndSize()
    {
        var asset = OkfUpgradeManifest.Parse(PublishedManifest).Find("okf-linux-x64");

        Assert.NotNull(asset);
        Assert.Equal("v1.1.0-rc.1/okf-linux-x64", asset.Path);
        Assert.Equal("0bb07817b2f1159920a631346c0626d1156d11ea89842ad49551bae03fe2f759", asset.Sha256);
        Assert.Equal(6532216, asset.Size);

        // AD-19: 64 lowercase hex digits, one convention across the repository.
        Assert.Equal(64, asset.Sha256.Length);
        Assert.All(asset.Sha256, character => Assert.Contains(character, "0123456789abcdef"));
    }

    /// <summary>
    /// The digest read for one asset must be that asset's. A reader that anchors on a
    /// greedy match answers with the LAST asset in the map — the failure mode install.sh's
    /// sed reader is written against, and the reason its fixture puts the two selectable
    /// binaries first.
    /// </summary>
    [Fact]
    public void KeepsEachAssetsDigestWithThatAsset()
    {
        var manifest = OkfUpgradeManifest.Parse(PublishedManifest);

        Assert.Equal(
            "02da3afb9804cb773cd6dc25d8f7ba28e7ed9268cd8e10f0215bf6f5b9720b47",
            manifest.Find("okf-osx-arm64")!.Sha256);
        Assert.Equal(
            "ab742001f4cf21c728e2d74f26d0cbc8f7f9eb90b1c77b329e43a5cdc6958c8c",
            manifest.Find("okf-win-x64.exe")!.Sha256);
        Assert.Equal(
            "a7237d3810b5a9591aee3d592356243626c5bd0bd093edf9804ae84964e05068",
            manifest.Find("install.sh")!.Sha256);
    }

    /// <summary>
    /// AD-42 keeps the absolute registry URL in the manifest for the artifact host's
    /// `sync.sh`, and okf must resolve the RELATIVE path instead — the installer and the
    /// upgrader must not know GitLab exists.
    /// </summary>
    [Fact]
    public void CarriesTheRegistryUrlWithoutMakingItThePath()
    {
        var asset = OkfUpgradeManifest.Parse(PublishedManifest).Find("okf-linux-x64")!;

        Assert.StartsWith("https://gitlab.tychostation.dev/", asset.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("gitlab", asset.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesTextThatIsNotAManifest()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgradeManifest.Parse("<!DOCTYPE html>"));
        Assert.Contains("not valid JSON", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAManifestWithNoVersion()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(
            () => OkfUpgradeManifest.Parse("""{"assets": {"okf-linux-x64": {"path": "p", "sha256": "d"}}}"""));
        Assert.Contains("names no version", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAManifestThatIsNotAnObject()
    {
        var refusal = Assert.Throws<OkfUpgradeException>(() => OkfUpgradeManifest.Parse("[]"));
        Assert.Contains("not a JSON object", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An asset that cannot be verified is not an asset. Keeping it with a null digest would
    /// move the failure from "this release has no binary for you" to a comparison against
    /// nothing, several steps after the download.
    /// </summary>
    [Theory]
    [InlineData("""{"version":"1.0.0","assets":{"okf-linux-x64":{"path":"v1.0.0/okf-linux-x64"}}}""")]
    [InlineData("""{"version":"1.0.0","assets":{"okf-linux-x64":{"sha256":"abc"}}}""")]
    [InlineData("""{"version":"1.0.0","assets":{"okf-linux-x64":"v1.0.0/okf-linux-x64"}}""")]
    public void DropsAnAssetThatCannotBeLocatedOrVerified(string json)
    {
        var manifest = OkfUpgradeManifest.Parse(json);

        Assert.Equal("1.0.0", manifest.Version);
        Assert.Null(manifest.Find("okf-linux-x64"));
    }

    /// <summary>
    /// The asset map grows — three binaries and a skills archive were all added after it
    /// shipped (AD-42). A reader that refused an unknown key would break on the release that
    /// adds the ninth asset, so an unknown key and a missing optional field are both fine.
    /// An optional field carrying the wrong JSON type reads as absent for the same reason:
    /// the manifest is downloaded, so its shape is somebody else's to get wrong.
    /// </summary>
    [Fact]
    public void ToleratesUnknownKeysAndMissingOptionalFields()
    {
        var manifest = OkfUpgradeManifest.Parse(
            """
            {
              "version": "2.0.0",
              "channel": "stable",
              "tag": 20,
              "generatedAt": null,
              "assets": {
                "okf-linux-x64": {
                  "path": "v2.0.0/okf-linux-x64",
                  "sha256": "ab",
                  "size": "large",
                  "signature": "x"
                }
              }
            }
            """);

        var asset = manifest.Find("okf-linux-x64")!;
        Assert.Null(manifest.Tag);
        Assert.Null(manifest.GeneratedAt);
        Assert.Null(asset.Url);
        Assert.Equal(0, asset.Size);
        Assert.Equal("v2.0.0/okf-linux-x64", asset.Path);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("true")]
    public void AManifestWithAMistypedAssetsMapTreatsItAsEmpty(string assets)
    {
        OkfUpgradeManifest manifest = OkfUpgradeManifest.Parse(
            $$"""{ "version": "2.0.0", "assets": {{assets}} }""");

        Assert.Empty(manifest.Assets);
    }
}
