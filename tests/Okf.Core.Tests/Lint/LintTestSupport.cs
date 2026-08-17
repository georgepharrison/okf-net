namespace Okf.Core.Tests.Lint;

/// <summary>A throwaway bundle on disk, built file by file and removed with the test.</summary>
internal sealed class TempBundle : IDisposable
{
    /// <summary>Creates an empty bundle in a temporary directory.</summary>
    /// <param name="name">The bundle directory's name.</param>
    public TempBundle(string name = "bundle")
    {
        Root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName(), name);
        Directory.CreateDirectory(Root);
    }

    /// <summary>The bundle root.</summary>
    public string Root { get; }

    /// <summary>The bundle, as the library sees it.</summary>
    public OkfBundle Bundle => new(Root);

    /// <summary>The date lint runs judge staleness against, fixed so tests are deterministic.</summary>
    public static DateOnly Today => new(2026, 6, 1);

    /// <summary>Writes a file into the bundle.</summary>
    /// <param name="relativePath">The bundle-relative path.</param>
    /// <param name="content">The file's content.</param>
    /// <returns>This bundle, for chaining.</returns>
    public TempBundle Add(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return this;
    }

    /// <summary>Lints the bundle.</summary>
    /// <param name="options">The lint options; defaults to built-in severities and a fixed date.</param>
    /// <returns>The diagnostics.</returns>
    public IReadOnlyList<OkfDiagnostic> Lint(OkfLintOptions? options = null) =>
        new OkfLinter(options ?? new OkfLintOptions { Today = Today }).Lint(Bundle).Diagnostics;

    /// <summary>Lints the bundle and returns the rule ids that fired.</summary>
    /// <param name="options">The lint options; defaults to built-in severities and a fixed date.</param>
    /// <returns>The rule ids, in diagnostic order.</returns>
    public string[] LintIds(OkfLintOptions? options = null) =>
        [.. Lint(options).Select(diagnostic => diagnostic.RuleId)];

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}

/// <summary>
/// A throwaway vault on disk — <c>bundles/</c> beside <c>raw/</c> — for the one rule that
/// is scoped to the vault rather than to a bundle root (<c>OKF0310</c>).
/// </summary>
internal sealed class TempVault : IDisposable
{
    private readonly string _parent;

    /// <summary>Creates an empty vault with one empty bundle in it.</summary>
    /// <param name="bundleName">The bundle directory's name.</param>
    public TempVault(string bundleName = "bundle")
    {
        _parent = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());
        Root = Path.Combine(_parent, "okf");
        BundleRoot = Path.Combine(Root, "bundles", bundleName);
        Directory.CreateDirectory(BundleRoot);
        Directory.CreateDirectory(RawDirectory);
    }

    /// <summary>The vault root, <c>&lt;project&gt;/okf</c>.</summary>
    public string Root { get; }

    /// <summary>The one bundle root inside the vault.</summary>
    public string BundleRoot { get; }

    /// <summary>The capture drop zone, <c>&lt;vault&gt;/raw</c>.</summary>
    public string RawDirectory => Path.Combine(Root, "raw");

    /// <summary>The capture manifest's path.</summary>
    public string ManifestPath => Path.Combine(RawDirectory, "manifest.json");

    /// <summary>Writes a captured artifact into <c>raw/</c>.</summary>
    /// <param name="relativePath">The path relative to <c>raw/</c>.</param>
    /// <param name="content">The artifact's bytes, as text.</param>
    /// <returns>The artifact's SHA-256, lowercase hex — what a manifest entry records.</returns>
    public string WriteRaw(string relativePath, string content)
    {
        var path = Path.Combine(RawDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    }

    /// <summary>Writes the capture manifest verbatim.</summary>
    /// <param name="json">The manifest text.</param>
    /// <returns>This vault, for chaining.</returns>
    public TempVault WriteManifest(string json)
    {
        File.WriteAllText(ManifestPath, json);
        return this;
    }

    /// <summary>
    /// Writes a manifest holding one capture of one file, closed by an <c>ingestion</c>
    /// object unless <paramref name="ingested" /> says otherwise.
    /// </summary>
    /// <param name="id">The capture id.</param>
    /// <param name="file">The file's path relative to <c>raw/</c>.</param>
    /// <param name="sha256">The sha256 to record.</param>
    /// <param name="ingested">Whether the entry is closed (an <c>ingestion</c> object) or open (null).</param>
    /// <returns>This vault, for chaining.</returns>
    public TempVault WriteCapture(string id, string file, string sha256, bool ingested = true)
    {
        var ingestion = ingested
            ? """{ "at": "2026-06-01T00:00:00Z", "by": "tests", "concepts": ["bundles/bundle/x.md"] }"""
            : "null";

        return WriteManifest($$"""
            {
              "manifestVersion": 1,
              "captures": [
                {
                  "id": "{{id}}",
                  "form": "flat",
                  "files": [{ "path": "{{file}}", "sha256": "{{sha256}}" }],
                  "capturedAt": "2026-06-01T00:00:00Z",
                  "capturedBy": "tests",
                  "ingestion": {{ingestion}}
                }
              ]
            }
            """);
    }

    /// <summary>Lints the vault: its bundles plus the vault-scoped rules.</summary>
    /// <param name="options">The lint options; the vault root is filled in.</param>
    /// <returns>The diagnostics.</returns>
    public IReadOnlyList<OkfDiagnostic> Lint(OkfLintOptions? options = null)
    {
        options ??= new OkfLintOptions { Today = TempBundle.Today };
        options.VaultRoot = Root;
        return new OkfLinter(options).Lint([new OkfBundle(BundleRoot)]).Diagnostics;
    }

    /// <summary>Lints the vault and returns the rule ids that fired.</summary>
    /// <param name="options">The lint options; the vault root is filled in.</param>
    /// <returns>The rule ids, in diagnostic order.</returns>
    public string[] LintIds(OkfLintOptions? options = null) =>
        [.. Lint(options).Select(diagnostic => diagnostic.RuleId)];

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_parent, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
