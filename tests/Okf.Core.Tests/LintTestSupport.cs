namespace Okf.Core.Tests;

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
