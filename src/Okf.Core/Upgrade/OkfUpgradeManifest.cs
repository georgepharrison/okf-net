using System.Text.Json;

namespace Okf.Core.Upgrade;

/// <summary>
/// One asset in a release manifest: where to get it, how big it is, and what it must
/// hash to.
/// </summary>
public sealed class OkfUpgradeAsset
{
    /// <summary>Initializes an asset.</summary>
    /// <param name="name">The asset's name, which is its key in the manifest.</param>
    /// <param name="path">The release-relative path, resolved against the base URL.</param>
    /// <param name="sha256">The expected digest, 64 lowercase hex digits (AD-19).</param>
    /// <param name="size">The size in bytes, or <c>0</c> when the manifest carried none.</param>
    /// <param name="url">The absolute registry URL, which no consumer of this type fetches.</param>
    public OkfUpgradeAsset(string name, string path, string sha256, long size, string? url)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        Name = name;
        Path = path;
        Sha256 = sha256;
        Size = size;
        Url = url;
    }

    /// <summary>The asset's name — <c>okf-linux-x64</c>, <c>install.sh</c>, and so on.</summary>
    public string Name { get; }

    /// <summary>
    /// The path relative to the base URL the manifest was fetched from, e.g.
    /// <c>v1.1.0-rc.1/okf-linux-x64</c>. This is the one an installer and
    /// <c>okf upgrade</c> resolve: neither may know that GitLab exists (AD-42).
    /// </summary>
    public string Path { get; }

    /// <summary>The expected SHA-256, as written in the manifest.</summary>
    public string Sha256 { get; }

    /// <summary>The size in bytes as published, or <c>0</c> when absent.</summary>
    public long Size { get; }

    /// <summary>
    /// The absolute package-registry URL. Carried for the artifact host's <c>sync.sh</c>,
    /// which pulls with a token; okf-net itself never fetches it.
    /// </summary>
    public string? Url { get; }
}

/// <summary>
/// A release manifest — the <c>latest.json</c> the <c>publish</c> job writes and the
/// artifact host serves (AD-42).
/// </summary>
/// <remarks>
/// Reading is deliberately tolerant of everything except the fields that decide what is
/// written to disk. An unknown key, a missing <c>tag</c>, an asset with no <c>url</c>: all
/// fine, because the asset map gains entries over time and a reader that refused a release
/// for carrying a ninth asset would break on the release that added one. A missing
/// <c>version</c>, or an asset with no <c>path</c> or no <c>sha256</c>, is not fine — those
/// are what a download is located and verified by.
/// </remarks>
public sealed class OkfUpgradeManifest
{
    private readonly Dictionary<string, OkfUpgradeAsset> assets;

    private OkfUpgradeManifest(
        string version,
        string? tag,
        string? generatedAt,
        Dictionary<string, OkfUpgradeAsset> assets)
    {
        Version = version;
        Tag = tag;
        GeneratedAt = generatedAt;
        this.assets = assets;
    }

    /// <summary>The release's version, without a leading <c>v</c>.</summary>
    public string Version { get; }

    /// <summary>The git tag the release was cut from, when the manifest names one.</summary>
    public string? Tag { get; }

    /// <summary>The commit timestamp the manifest was stamped with (AD-24), when present.</summary>
    public string? GeneratedAt { get; }

    /// <summary>Every asset in the manifest, keyed by name.</summary>
    public IReadOnlyDictionary<string, OkfUpgradeAsset> Assets => this.assets;

    /// <summary>Finds one asset by name.</summary>
    /// <param name="name">The asset's name.</param>
    /// <returns>The asset, or <see langword="null" /> when this release has none by that name.</returns>
    public OkfUpgradeAsset? Find(string name) =>
        name is null ? null : this.assets.GetValueOrDefault(name);

    /// <summary>Reads a manifest from its JSON text.</summary>
    /// <param name="json">The manifest's bytes, as text.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="OkfUpgradeException">The text is not a release manifest.</exception>
    public static OkfUpgradeManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException exception)
        {
            throw new OkfUpgradeException($"the release manifest is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OkfUpgradeException("the release manifest is not a JSON object.");
            }

            if (StrictJson.String(root, "version") is not { Length: > 0 } version)
            {
                throw new OkfUpgradeException(
                    "the release manifest names no version — is it a release manifest?");
            }

            var assets = new Dictionary<string, OkfUpgradeAsset>(StringComparer.Ordinal);
            if (root.TryGetProperty("assets", out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in map.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var path = StrictJson.String(entry.Value, "path");
                    var sha256 = StrictJson.String(entry.Value, "sha256");
                    if (path is not { Length: > 0 } || sha256 is not { Length: > 0 })
                    {
                        // Left out of the map rather than kept with holes: an asset that
                        // cannot be located or cannot be verified is not selectable, and
                        // "this release has no asset for your platform" is then the honest
                        // answer rather than a null dereference three steps later.
                        continue;
                    }

                    var size = entry.Value.TryGetProperty("size", out var sizeValue)
                        && sizeValue.ValueKind == JsonValueKind.Number
                        && sizeValue.TryGetInt64(out var bytes)
                            ? bytes
                            : 0L;

                    assets[entry.Name] = new OkfUpgradeAsset(
                        entry.Name,
                        path,
                        sha256,
                        size,
                        StrictJson.String(entry.Value, "url"));
                }
            }

            return new OkfUpgradeManifest(
                version,
                StrictJson.String(root, "tag"),
                StrictJson.String(root, "generatedAt"),
                assets);
        }
    }
}
