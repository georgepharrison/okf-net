using System.Security.Cryptography;
using System.Text.Json;

namespace Okf.Core.Capture;

/// <summary>
/// One file recorded by a capture entry: its path relative to <c>raw/</c> and the
/// SHA-256 the capture recorded for it.
/// </summary>
public sealed class OkfCaptureFile
{
    /// <summary>Initializes a recorded file.</summary>
    /// <param name="path">The path relative to <c>raw/</c>, with <c>/</c> separators.</param>
    /// <param name="sha256">The recorded SHA-256, 64 lowercase hex digits.</param>
    public OkfCaptureFile(string path, string sha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        Path = path;
        Sha256 = sha256;
    }

    /// <summary>The path relative to <c>raw/</c>.</summary>
    public string Path { get; }

    /// <summary>The SHA-256 the capture recorded — what "unchanged" is measured against.</summary>
    public string Sha256 { get; }
}

/// <summary>
/// One capture: an artifact (or packet of them) dropped into <c>raw/</c>, and whether it
/// has been ingested into a bundle yet.
/// </summary>
public sealed class OkfCaptureEntry
{
    /// <summary>Initializes an entry.</summary>
    /// <param name="id">The capture id, <c>&lt;YYYY-MM-DD&gt;-&lt;slug&gt;</c>.</param>
    /// <param name="files">The files the capture recorded.</param>
    /// <param name="isIngested">Whether the entry's <c>ingestion</c> is closed (non-null).</param>
    public OkfCaptureEntry(string id, IReadOnlyList<OkfCaptureFile> files, bool isIngested)
    {
        ArgumentNullException.ThrowIfNull(files);
        Id = id ?? string.Empty;
        Files = files;
        IsIngested = isIngested;
    }

    /// <summary>The capture id, or the empty string when the entry carries none.</summary>
    public string Id { get; }

    /// <summary>The files the capture recorded.</summary>
    public IReadOnlyList<OkfCaptureFile> Files { get; }

    /// <summary>
    /// Whether the entry is closed: its <c>ingestion</c> is an object rather than
    /// <see langword="null" />. Immutability starts here — an open entry is still the
    /// custodian's work queue (decisions.md Q3).
    /// </summary>
    public bool IsIngested { get; }
}

/// <summary>
/// The capture manifest, <c>&lt;vault&gt;/raw/manifest.json</c>: the immutability record
/// for everything the capture skill drops into <c>raw/</c> (decisions.md, the capture and
/// custodian skills milestone).
/// </summary>
/// <remarks>
/// <para>This reader is deliberately partial. It answers one question — which recorded
/// files belong to which entries, and which of those entries are closed — because that is
/// what <c>OKF0310</c> needs. Everything else the record has to satisfy (the id grammar,
/// the timestamp forms, the flat/packet layout, unclaimed files in <c>raw/</c>, ingestion
/// pointers resolving to concepts that exist) is checked by
/// <c>okf/custodian/check-manifest.py</c>, which specified those invariants first and
/// remains the belt-and-braces gate beside <c>okf lint</c>.</para>
/// <para>Parsing goes through <see cref="JsonDocument" /> rather than a deserializer, so
/// no reflection is involved and the type stays NativeAOT-safe, exactly as
/// <see cref="OkfConfig" /> does. A manifest that does not parse yields
/// <see langword="null" />: reporting it is the script's job, and repairing it is nobody's
/// — a tool that rewrites the immutability record is how the record is lost.</para>
/// </remarks>
public sealed class OkfCaptureManifest
{
    /// <summary>The vault subdirectory captures are dropped into (decisions.md Q3).</summary>
    public const string RawDirectoryName = "raw";

    /// <summary>The manifest's filename inside <see cref="RawDirectoryName" />.</summary>
    public const string FileName = "manifest.json";

    private OkfCaptureManifest(string path, IReadOnlyList<OkfCaptureEntry> captures)
    {
        Path = path;
        Captures = captures;
    }

    /// <summary>The manifest file's absolute path.</summary>
    public string Path { get; }

    /// <summary>The capture entries, in file order.</summary>
    public IReadOnlyList<OkfCaptureEntry> Captures { get; }

    /// <summary>The manifest's path for a vault root.</summary>
    /// <param name="vaultRoot">The vault root, <c>&lt;project&gt;/okf</c>.</param>
    /// <returns>The absolute path of <c>raw/manifest.json</c>.</returns>
    public static string PathFor(string vaultRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultRoot);
        return System.IO.Path.Combine(vaultRoot, RawDirectoryName, FileName);
    }

    /// <summary>
    /// Reads a vault's capture manifest, or returns <see langword="null" /> when there is
    /// none or it does not parse.
    /// </summary>
    /// <param name="vaultRoot">The vault root, <c>&lt;project&gt;/okf</c>.</param>
    /// <param name="text">The manifest's text when one was read, for locating lines.</param>
    /// <returns>The manifest, or <see langword="null" />.</returns>
    /// <exception cref="IOException">The file exists but could not be read.</exception>
    public static OkfCaptureManifest? TryLoad(string vaultRoot, out string? text)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultRoot);

        text = null;
        string path = PathFor(vaultRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        text = File.ReadAllText(path);
        return Parse(text, path);
    }

    /// <summary>Parses manifest text.</summary>
    /// <param name="json">The manifest's JSON text.</param>
    /// <param name="path">The path to report as the manifest's own.</param>
    /// <returns>The manifest, or <see langword="null" /> when the text does not parse as one.</returns>
    public static OkfCaptureManifest? Parse(string json, string path)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(path);

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
            return ParseDocument(document.RootElement, path);
        }
    }

    /// <summary>The SHA-256 of a file, as 64 lowercase hex digits.</summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static string Sha256Of(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using FileStream stream = File.OpenRead(path);
        return Sha256Of(stream);
    }

    /// <summary>
    /// The SHA-256 of a stream, as 64 lowercase hex digits. AD-19's single implementation
    /// of the convention: the path form above opens a file and hands it here, and the
    /// archive verifier, which holds a stream and no path, calls it directly — so the two
    /// digests are comparable because they are the same code, not because two one-liners
    /// agree today.
    /// </summary>
    /// <param name="stream">The stream, read from its current position to the end.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="IOException">The stream could not be read.</exception>
    internal static string Sha256Of(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    /// <summary>
    /// A digest as a message shows it: the first twelve hex digits and an ellipsis. Enough
    /// to tell two digests apart in a diagnostic, short enough to leave the sentence
    /// readable — and a digest recorded at some other length by a producer okf did not
    /// write for (AD-4) is shown as it was found rather than indexed off the end of.
    /// </summary>
    /// <param name="sha256">The digest, at whatever length it was recorded.</param>
    /// <returns>The abbreviated form.</returns>
    internal static string Short(string sha256) => sha256.Length > 12 ? sha256[..12] + "…" : sha256;

    private static OkfCaptureManifest? ParseDocument(JsonElement root, string path)
    {
        if (!TryReadCaptures(root, out JsonElement captures))
        {
            return null;
        }

        return new OkfCaptureManifest(path, ReadEntries(captures));
    }

    private static bool TryReadCaptures(JsonElement root, out JsonElement captures)
    {
        captures = default;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("captures", out captures)
            && captures.ValueKind == JsonValueKind.Array;
    }

    private static List<OkfCaptureEntry> ReadEntries(JsonElement captures)
    {
        List<OkfCaptureEntry> entries = new List<OkfCaptureEntry>();
        foreach (JsonElement capture in captures.EnumerateArray())
        {
            if (capture.ValueKind == JsonValueKind.Object)
            {
                entries.Add(ReadEntry(capture));
            }
        }

        return entries;
    }

    private static OkfCaptureEntry ReadEntry(JsonElement capture) =>
        new(ReadId(capture), ReadFiles(capture), IsIngested(capture));

    private static string ReadId(JsonElement capture) => StrictJson.String(capture, "id") ?? string.Empty;

    private static bool IsIngested(JsonElement capture) =>
        capture.TryGetProperty("ingestion", out JsonElement ingestion)
        && ingestion.ValueKind == JsonValueKind.Object;

    private static List<OkfCaptureFile> ReadFiles(JsonElement capture)
    {
        List<OkfCaptureFile> files = new List<OkfCaptureFile>();
        if (!capture.TryGetProperty("files", out JsonElement recorded) || recorded.ValueKind != JsonValueKind.Array)
        {
            return files;
        }

        foreach (JsonElement file in recorded.EnumerateArray())
        {
            if (ReadFile(file) is { } recordedFile)
            {
                files.Add(recordedFile);
            }
        }

        return files;
    }

    private static OkfCaptureFile? ReadFile(JsonElement file)
    {
        if (file.ValueKind != JsonValueKind.Object
            || StrictJson.String(file, "path") is not { Length: > 0 } path
            || StrictJson.String(file, "sha256") is not { Length: > 0 } sha256)
        {
            return null;
        }

        return new OkfCaptureFile(path, sha256);
    }
}
