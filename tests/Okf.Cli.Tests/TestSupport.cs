using Okf.Core;

namespace Okf.Cli.Tests;

/// <summary>What one in-process <c>okf</c> invocation produced.</summary>
/// <param name="ExitCode">The process exit code the invocation would have returned.</param>
/// <param name="Output">Everything written to stdout.</param>
/// <param name="Error">Everything written to stderr.</param>
internal sealed record CliRun(int ExitCode, string Output, string Error)
{
    /// <summary>The stdout lines, with the trailing blank removed.</summary>
    public string[] OutputLines =>
        Output.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();

    /// <summary>The diagnostic lines only — stdout minus the summary line.</summary>
    public string[] DiagnosticLines =>
        [.. OutputLines.Where(line => !line.StartsWith("Checked ", StringComparison.Ordinal))];

    /// <summary>The summary line.</summary>
    public string Summary =>
        OutputLines.LastOrDefault(line => line.StartsWith("Checked ", StringComparison.Ordinal)) ?? string.Empty;

    /// <summary>The rule ids reported, in output order.</summary>
    public string[] RuleIds =>
        [.. DiagnosticLines
            .Select(line => line.Split(' ').FirstOrDefault(token => token.StartsWith("OKF", StringComparison.Ordinal)))
            .Where(id => id is not null)
            .Select(id => id!.TrimEnd(':'))];
}

/// <summary>Runs the CLI in process, so tests are hermetic and fast.</summary>
internal static class Cli
{
    /// <summary>Invokes <c>okf</c>.</summary>
    /// <param name="environment">The environment to resolve vaults and configuration against.</param>
    /// <param name="args">The command line, without the executable name.</param>
    /// <returns>The exit code and captured output.</returns>
    public static CliRun Run(OkfEnvironment environment, params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exitCode = CliApplication.Run(args, environment, output, error);
        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Invokes <c>okf</c> from a working directory, with a home directory that holds no
    /// okf configuration unless the test put one there.
    /// </summary>
    /// <param name="workingDirectory">The directory the command resolves from.</param>
    /// <param name="home">The home directory, which also fixes where the global config is looked for.</param>
    /// <param name="args">The command line, without the executable name.</param>
    /// <returns>The exit code and captured output.</returns>
    public static CliRun RunIn(string workingDirectory, string home, params string[] args) =>
        Run(Environment(workingDirectory, home), args);

    /// <summary>Builds a hermetic environment: the real one never leaks into a test.</summary>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="home">The home directory, also used for <c>XDG_CONFIG_HOME</c>.</param>
    /// <param name="okfHome">An optional <c>OKF_HOME</c> override.</param>
    /// <returns>The environment.</returns>
    public static OkfEnvironment Environment(string workingDirectory, string home, string? okfHome = null)
    {
        var variables = new List<KeyValuePair<string, string>>
        {
            new("HOME", home),
            new("XDG_CONFIG_HOME", Path.Combine(home, ".config")),
        };

        if (okfHome is not null)
        {
            variables.Add(new KeyValuePair<string, string>(OkfEnvironment.HomeVariable, okfHome));
        }

        return new OkfEnvironment(workingDirectory, variables);
    }
}

/// <summary>The fixture bundles copied beside the test assembly.</summary>
internal static class Fixtures
{
    /// <summary>The directory holding every fixture bundle.</summary>
    public static string Root => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>The path of one fixture bundle.</summary>
    /// <param name="name">The bundle directory's name.</param>
    /// <returns>The absolute path.</returns>
    public static string Bundle(string name) => Path.Combine(Root, name);
}

/// <summary>A throwaway directory tree, removed when the test finishes.</summary>
internal sealed class TempTree : IDisposable
{
    /// <summary>Creates an empty temporary tree.</summary>
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(Root);
    }

    /// <summary>The tree's root directory.</summary>
    public string Root { get; }

    /// <summary>Creates a directory inside the tree.</summary>
    /// <param name="relativePath">The path relative to the tree root.</param>
    /// <returns>The absolute path of the directory.</returns>
    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes a file inside the tree, creating its directory.</summary>
    /// <param name="relativePath">The path relative to the tree root.</param>
    /// <param name="content">The file's content.</param>
    /// <returns>The absolute path of the file.</returns>
    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Copies a fixture bundle into the tree.</summary>
    /// <param name="fixture">The fixture bundle's name.</param>
    /// <param name="relativePath">Where to put it, relative to the tree root.</param>
    /// <returns>The absolute path of the copied bundle.</returns>
    public string CopyFixture(string fixture, string relativePath)
    {
        var target = CreateDirectory(relativePath);
        var source = Fixtures.Bundle(fixture);
        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        return target;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
