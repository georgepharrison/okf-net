using System.Text;
using System.Text.Json;

namespace Okf.Core.Bundle;

/// <summary>One file a distribution carries, and the digest recorded for it.</summary>
public sealed class OkfDistributionFile
{
    /// <summary>Initializes a recorded file.</summary>
    /// <param name="path">The distribution-relative path, with <c>/</c> separators.</param>
    /// <param name="sha256">The SHA-256 of the bytes shipped, 64 lowercase hex digits.</param>
    public OkfDistributionFile(string path, string sha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        Path = path;
        Sha256 = sha256;
    }

    /// <summary>The distribution-relative path, e.g. <c>bundles/okf-net/index.md</c>.</summary>
    public string Path { get; }

    /// <summary>The SHA-256 of the shipped bytes — what <c>okf bundle --verify</c> re-computes.</summary>
    public string Sha256 { get; }
}

/// <summary>
/// A link that leaves its bundle and lands on something this distribution does not carry:
/// a concept in a bundle the caller left out, or anything outside <c>bundles/</c> at all
/// (<c>raw/</c>, the custodian directory, the repository around the vault).
/// </summary>
/// <remarks>
/// Recorded rather than repaired. Spec §6.1 makes a broken link legal and obliges every
/// consumer to tolerate one, so the bundler ships the link as written and says, in the
/// distribution manifest and on stderr, exactly which links a consumer will find dangling
/// (decisions.md, the bundler milestone).
/// </remarks>
public sealed class OkfExternalLink
{
    /// <summary>Initializes an external link.</summary>
    /// <param name="from">The distribution-relative path of the document holding the link.</param>
    /// <param name="to">The link target exactly as written.</param>
    /// <param name="bundle">The vault bundle the target lands in, or <see langword="null" /> when it lands outside <c>bundles/</c>.</param>
    /// <param name="line">The 1-based line the link sits on, for the warning; not part of the manifest.</param>
    public OkfExternalLink(string from, string to, string? bundle, int line = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
        Bundle = bundle;
        Line = line;
    }

    /// <summary>The distribution-relative path of the document holding the link.</summary>
    public string From { get; }

    /// <summary>The link target exactly as written.</summary>
    public string To { get; }

    /// <summary>
    /// The vault bundle the target lands in, or <see langword="null" /> when the target
    /// leaves <c>bundles/</c> entirely.
    /// </summary>
    public string? Bundle { get; }

    /// <summary>
    /// The 1-based line the link sits on. Carried for the stderr warning and deliberately
    /// not written to the manifest: the manifest states that a link dangles, which is a
    /// property of the distribution, while a line number is a diagnostic about a file.
    /// </summary>
    public int Line { get; }
}

/// <summary>What <c>okf bundle --verify</c> found wrong with one path.</summary>
public enum OkfDistributionIssue
{
    /// <summary>The manifest itself is absent or does not parse, so nothing can be checked.</summary>
    Unreadable = 0,

    /// <summary>The manifest lists the file and the distribution does not carry it.</summary>
    Missing,

    /// <summary>The file is there and its bytes no longer hash to what the manifest recorded.</summary>
    Modified,

    /// <summary>The distribution carries a file the manifest never listed.</summary>
    Unlisted,
}

/// <summary>One thing <c>okf bundle --verify</c> found.</summary>
/// <param name="Issue">What is wrong.</param>
/// <param name="Path">The distribution-relative path it is wrong about.</param>
/// <param name="Detail">A sentence naming the digests, or the reason the manifest could not be read.</param>
public sealed record OkfDistributionFinding(OkfDistributionIssue Issue, string Path, string Detail);

/// <summary>The outcome of re-hashing a distribution against its own manifest.</summary>
public sealed class OkfDistributionVerification
{
    /// <summary>Initializes a verification result.</summary>
    /// <param name="source">The archive or directory that was verified.</param>
    /// <param name="manifest">The manifest read from it, or <see langword="null" /> when it could not be read.</param>
    /// <param name="findings">Everything wrong, in deterministic order.</param>
    /// <param name="checkedCount">How many recorded files were re-hashed.</param>
    public OkfDistributionVerification(
        string source,
        OkfDistributionManifest? manifest,
        IReadOnlyList<OkfDistributionFinding> findings,
        int checkedCount)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(findings);
        Source = source;
        Manifest = manifest;
        Findings = findings;
        Checked = checkedCount;
    }

    /// <summary>The archive or directory that was verified.</summary>
    public string Source { get; }

    /// <summary>The manifest, or <see langword="null" /> when it could not be read.</summary>
    public OkfDistributionManifest? Manifest { get; }

    /// <summary>Everything wrong, in deterministic order.</summary>
    public IReadOnlyList<OkfDistributionFinding> Findings { get; }

    /// <summary>How many recorded files were re-hashed.</summary>
    public int Checked { get; }

    /// <summary>Whether the distribution is exactly what its manifest says it is.</summary>
    public bool IsValid => Findings.Count == 0;
}

/// <summary>
/// The distribution manifest, <c>okf-bundle.json</c> at the root of everything the bundler
/// writes: what was packaged, by which version of okf, out of which vault, and the
/// SHA-256 of every file shipped.
/// </summary>
/// <remarks>
/// <para>It sits at the distribution root, <em>outside</em> every bundle root, for the
/// same reason <c>okf.json</c> does: it describes the distribution rather than any one
/// bundle, and what a consumer receives has to stay exactly the bundle the producer had.
/// A consumer that ignores the manifest, or deletes it, still has a complete
/// <c>cat</c>-readable bundle — which is the promise §1 makes and the manifest must not
/// quietly withdraw. Note that conformance is <em>not</em> the reason: §11.1 is scoped to
/// "every non-reserved <c>.md</c> file", so a <c>.json</c> in a bundle root lints clean,
/// as the <c>.py</c> attesters §6.3 blesses already do.</para>
/// <para>The manifest is the one file it does not hash. It cannot record its own digest
/// without a fixed point, and a self-attesting manifest proves nothing anyway: the
/// integrity claim it carries is only as good as the channel the manifest itself arrived
/// through.</para>
/// <para>Keys are camelCase, like the capture manifest (<see cref="OkfCaptureManifest" />)
/// and unlike frontmatter's <c>okf_version</c>: both files are machine-maintained JSON
/// read by the same reader, and YAML's snake_case belongs to the documents.</para>
/// </remarks>
public sealed class OkfDistributionManifest
{
    /// <summary>The manifest's filename at the distribution root.</summary>
    public const string FileName = "okf-bundle.json";

    /// <summary>The manifest schema version this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The OKF specification version the packaged bundles are written against.</summary>
    public const string SpecVersion = "0.2";

    /// <summary>Initializes a manifest.</summary>
    /// <param name="generator">The §7 actor that packaged it, e.g. <c>okf/1.0.0-rc.15</c>.</param>
    /// <param name="sourceVault">The producing vault's name, or <see langword="null" /> when a lone bundle was packaged.</param>
    /// <param name="generatedAt">When it was packaged, in the canonical <c>Z</c> form.</param>
    /// <param name="bundles">The bundle names included, ordered.</param>
    /// <param name="externalLinks">The links that dangle in this distribution.</param>
    /// <param name="files">Every file shipped, with its digest, ordered by path.</param>
    /// <param name="manifestVersion">The schema version, defaulting to <see cref="CurrentVersion" />.</param>
    /// <param name="okfVersion">The spec version, defaulting to <see cref="SpecVersion" />.</param>
    public OkfDistributionManifest(
        string generator,
        string? sourceVault,
        string generatedAt,
        IReadOnlyList<string> bundles,
        IReadOnlyList<OkfExternalLink> externalLinks,
        IReadOnlyList<OkfDistributionFile> files,
        int manifestVersion = CurrentVersion,
        string okfVersion = SpecVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(generator);
        ArgumentException.ThrowIfNullOrEmpty(generatedAt);
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(externalLinks);
        ArgumentNullException.ThrowIfNull(files);
        Generator = generator;
        SourceVault = sourceVault;
        GeneratedAt = generatedAt;
        Bundles = bundles;
        ExternalLinks = externalLinks;
        Files = files;
        ManifestVersion = manifestVersion;
        OkfVersion = okfVersion;
    }

    /// <summary>The schema version of this manifest.</summary>
    public int ManifestVersion { get; }

    /// <summary>The OKF specification version the packaged bundles are written against.</summary>
    public string OkfVersion { get; }

    /// <summary>The §7 actor that packaged the distribution.</summary>
    public string Generator { get; }

    /// <summary>
    /// The producing vault's name — its directory name, or the project directory's name
    /// when the vault is the conventional <c>okf/</c>. A name, never a path: where the
    /// producer keeps their filesystem is not information a consumer asked for.
    /// </summary>
    public string? SourceVault { get; }

    /// <summary>When the distribution was packaged, in the canonical <c>Z</c> form.</summary>
    public string GeneratedAt { get; }

    /// <summary>The bundle names included.</summary>
    public IReadOnlyList<string> Bundles { get; }

    /// <summary>The links that leave their bundle and land on something not shipped here.</summary>
    public IReadOnlyList<OkfExternalLink> ExternalLinks { get; }

    /// <summary>Every file shipped, with its digest, ordered by path.</summary>
    public IReadOnlyList<OkfDistributionFile> Files { get; }

    /// <summary>Parses a manifest.</summary>
    /// <param name="json">The manifest's JSON text.</param>
    /// <returns>The manifest, or <see langword="null" /> when the text does not read as one.</returns>
    public static OkfDistributionManifest? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || StrictJson.String(root, "generatedAt") is not { Length: > 0 } generatedAt
                || StrictJson.String(root, "generator") is not { Length: > 0 } generator)
            {
                return null;
            }

            List<OkfDistributionFile> files = new List<OkfDistributionFile>();
            if (root.TryGetProperty("files", out JsonElement recorded) && recorded.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement file in recorded.EnumerateArray())
                {
                    if (file.ValueKind == JsonValueKind.Object
                        && StrictJson.String(file, "path") is { Length: > 0 } path
                        && StrictJson.String(file, "sha256") is { Length: > 0 } sha256)
                    {
                        files.Add(new OkfDistributionFile(path, sha256));
                    }
                }
            }

            List<OkfExternalLink> links = new List<OkfExternalLink>();
            if (root.TryGetProperty("externalLinks", out JsonElement external) && external.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement link in external.EnumerateArray())
                {
                    if (link.ValueKind == JsonValueKind.Object
                        && StrictJson.String(link, "from") is { Length: > 0 } from
                        && StrictJson.String(link, "to") is { } to)
                    {
                        links.Add(new OkfExternalLink(from, to, StrictJson.String(link, "bundle")));
                    }
                }
            }

            List<string> bundles = new List<string>();
            if (root.TryGetProperty("bundles", out JsonElement named) && named.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement bundle in named.EnumerateArray())
                {
                    if (bundle.ValueKind == JsonValueKind.String && bundle.GetString() is { Length: > 0 } name)
                    {
                        bundles.Add(name);
                    }
                }
            }

            return new OkfDistributionManifest(
                generator,
                StrictJson.String(root, "sourceVault"),
                generatedAt,
                bundles,
                links,
                files,
                root.TryGetProperty("manifestVersion", out JsonElement version) && version.ValueKind == JsonValueKind.Number
                    ? version.GetInt32()
                    : CurrentVersion,
                StrictJson.String(root, "okfVersion") ?? SpecVersion);
        }
    }

    /// <summary>Renders the manifest as the JSON the distribution carries.</summary>
    /// <returns>The JSON text, newline-terminated.</returns>
    public string ToJson()
    {
        using MemoryStream buffer = new MemoryStream();
        JsonWriterOptions options = new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("manifestVersion", ManifestVersion);
            writer.WriteString("okfVersion", OkfVersion);
            writer.WriteString("generator", Generator);
            if (SourceVault is null)
            {
                writer.WriteNull("sourceVault");
            }
            else
            {
                writer.WriteString("sourceVault", SourceVault);
            }

            writer.WriteString("generatedAt", GeneratedAt);

            writer.WriteStartArray("bundles");
            foreach (string bundle in Bundles)
            {
                writer.WriteStringValue(bundle);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("externalLinks");
            foreach (OkfExternalLink link in ExternalLinks)
            {
                writer.WriteStartObject();
                writer.WriteString("from", link.From);
                writer.WriteString("to", link.To);
                if (link.Bundle is null)
                {
                    writer.WriteNull("bundle");
                }
                else
                {
                    writer.WriteString("bundle", link.Bundle);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("files");
            foreach (OkfDistributionFile file in Files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
