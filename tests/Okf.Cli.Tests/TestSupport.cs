using System.Text.Json;
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
    public string[] DiagnosticLines => [.. OutputLines.Where(line => !IsSummary(line))];

    /// <summary>
    /// The summary line: <c>okf lint</c>'s and <c>okf index --check</c>'s <c>Checked …</c>
    /// line, <c>okf index</c>'s <c>Generated …</c> line, or <c>okf search</c>'s
    /// <c>Found …</c> line.
    /// </summary>
    public string Summary => OutputLines.LastOrDefault(IsSummary) ?? string.Empty;

    /// <summary>The rule ids reported, in output order.</summary>
    public string[] RuleIds =>
        [.. DiagnosticLines
            .Select(line => line.Split(' ').FirstOrDefault(token => token.StartsWith("OKF", StringComparison.Ordinal)))
            .Where(id => id is not null)
            .Select(id => id!.TrimEnd(':'))];

    private static bool IsSummary(string line) =>
        line.StartsWith("Checked ", StringComparison.Ordinal)
        || line.StartsWith("Generated ", StringComparison.Ordinal)
        || line.StartsWith("Found ", StringComparison.Ordinal);
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

            // Windows reads LOCALAPPDATA rather than HOME for the data directory, and an
            // unset one falls back to the real profile — which would put `okf skills
            // install` in the developer's own %LOCALAPPDATA%. Pointed at the same place
            // the XDG fallback lands so one expected path holds on both platforms;
            // XDG_DATA_HOME is deliberately left unset, so the `~/.local/share` fallback
            // is what these tests exercise.
            new("LOCALAPPDATA", Path.Combine(home, ".local", "share")),
        };

        if (okfHome is not null)
        {
            variables.Add(new KeyValuePair<string, string>(OkfEnvironment.HomeVariable, okfHome));
        }

        return new OkfEnvironment(workingDirectory, variables);
    }
}

/// <summary>What one <c>okf mcp</c> session produced.</summary>
/// <param name="ExitCode">The process exit code the invocation would have returned.</param>
/// <param name="Output">Everything written to stdout — JSON-RPC and nothing else.</param>
/// <param name="Error">Everything written to stderr.</param>
internal sealed record McpRun(int ExitCode, string Output, string Error)
{
    /// <summary>The response messages, one per line, in the order the server wrote them.</summary>
    public string[] Responses =>
        [.. Output.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];

    /// <summary>Parses one response.</summary>
    /// <param name="index">Its zero-based position in <see cref="Responses" />.</param>
    /// <returns>The parsed message; the caller disposes it.</returns>
    public JsonDocument Response(int index) => JsonDocument.Parse(Responses[index]);

    /// <summary>Parses the <c>result</c> of one response, failing the test when it is an error.</summary>
    /// <param name="index">Its zero-based position in <see cref="Responses" />.</param>
    /// <returns>The parsed message, whose root is the result; the caller disposes it.</returns>
    public JsonDocument Result(int index)
    {
        using var message = Response(index);
        Assert.False(
            message.RootElement.TryGetProperty("error", out var failure),
            $"expected a result, got error {failure}");
        return JsonDocument.Parse(message.RootElement.GetProperty("result").GetRawText());
    }

    /// <summary>
    /// The text content of a <c>tools/call</c> response, with whether the call reported a
    /// tool-level failure.
    /// </summary>
    /// <param name="index">Its zero-based position in <see cref="Responses" />.</param>
    /// <returns>The text block and the <c>isError</c> flag.</returns>
    public (string Text, bool IsError) Content(int index)
    {
        using var result = Result(index);
        var content = result.RootElement.GetProperty("content");
        return (content[0].GetProperty("text").GetString()!, result.RootElement.GetProperty("isError").GetBoolean());
    }

    /// <summary>The error code of one response.</summary>
    /// <param name="index">Its zero-based position in <see cref="Responses" />.</param>
    /// <returns>The JSON-RPC error code.</returns>
    public int ErrorCode(int index)
    {
        using var message = Response(index);
        return message.RootElement.GetProperty("error").GetProperty("code").GetInt32();
    }

    /// <summary>The error message of one response.</summary>
    /// <param name="index">Its zero-based position in <see cref="Responses" />.</param>
    /// <returns>The JSON-RPC error message.</returns>
    public string ErrorMessage(int index)
    {
        using var message = Response(index);
        return message.RootElement.GetProperty("error").GetProperty("message").GetString()!;
    }
}

/// <summary>
/// Drives <c>okf mcp</c> in process over its real stdio path: the requests go in as the
/// text stdin would carry, the responses come back as the bytes stdout would.
/// </summary>
internal static class Mcp
{
    /// <summary>The protocol revision the tests speak.</summary>
    public const string ProtocolVersion = "2025-06-18";

    /// <summary>The <c>initialize</c> request every real client sends first.</summary>
    public static string Initialize(int id = 1, string version = ProtocolVersion) =>
        Request(
            id,
            "initialize",
            $$$"""{"protocolVersion":"{{{version}}}","capabilities":{},"clientInfo":{"name":"tests","version":"1"}}""");

    /// <summary>Builds a request message.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The <c>params</c> value as raw JSON, or null to omit it.</param>
    /// <returns>The message.</returns>
    public static string Request(int id, string method, string? parameters = null) =>
        parameters is null
            ? $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}"}"""
            : $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}""";

    /// <summary>Builds a <c>tools/call</c> request.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="tool">The tool's name.</param>
    /// <param name="arguments">The arguments object as raw JSON.</param>
    /// <returns>The message.</returns>
    public static string Call(int id, string tool, string arguments = "{}") =>
        Request(id, "tools/call", $$"""{"name":"{{tool}}","arguments":{{arguments}}}""");

    /// <summary>Runs a session against a vault.</summary>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="path">The path argument, or null to let the server discover one.</param>
    /// <param name="requests">The messages to write to stdin, one per line.</param>
    /// <returns>The exit code and the responses.</returns>
    public static McpRun Session(OkfEnvironment environment, string? path, params string[] requests) =>
        Run(environment, path is null ? [] : [path], requests);

    /// <summary>Runs a session with explicit command-line arguments.</summary>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="args">The arguments after <c>mcp</c>.</param>
    /// <param name="requests">The messages to write to stdin, one per line.</param>
    /// <returns>The exit code and the responses.</returns>
    public static McpRun Run(OkfEnvironment environment, string[] args, params string[] requests)
    {
        var input = new StringReader(string.Concat(requests.Select(request => request + "\n")));
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exitCode = CliApplication.Run(["mcp", .. args], environment, output, error, input);
        return new McpRun(exitCode, output.ToString(), error.ToString());
    }
}

/// <summary>
/// This repository, located by walking up for the solution file. It carries okf-net's own
/// dogfood bundle at <c>okf/</c> (PRD ACC-5), which the acceptance tests read as-is.
/// </summary>
internal static class Repository
{
    /// <summary>The repository root, or <see langword="null" /> when the tests run outside a checkout.</summary>
    public static string? Root
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Okf.sln")))
                {
                    return directory.FullName;
                }
            }

            return null;
        }
    }

    /// <summary>The dogfood vault, <c>okf/</c>.</summary>
    public static string? DogfoodVault => Root is { } root ? Path.Combine(root, "okf") : null;
}

/// <summary>
/// Google's four reference bundles, read as-is from their upstream clone (PRD ACC-1).
/// The location is <c>~/code/knowledge-catalog/okf/bundles</c> by default and can be
/// pointed elsewhere with <c>OKF_REFERENCE_BUNDLES</c>; where the clone is absent (a CI
/// runner without it) tests skip rather than passing vacuously.
/// </summary>
internal static class ReferenceBundles
{
    /// <summary>The environment variable that relocates the clone.</summary>
    public const string Variable = "OKF_REFERENCE_BUNDLES";

    /// <summary>The directory holding the four bundle roots.</summary>
    public static string Root =>
        Environment.GetEnvironmentVariable(Variable)
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "code",
            "knowledge-catalog",
            "okf",
            "bundles");

    /// <summary>The path of one reference bundle.</summary>
    /// <param name="name">The bundle directory's name.</param>
    /// <returns>The absolute path.</returns>
    public static string Bundle(string name) => Path.Combine(Root, name);

    /// <summary>
    /// A snapshot of every file in a tree — path, length, and last-write time — so a test
    /// can prove it left the clone untouched.
    /// </summary>
    /// <param name="root">The directory to snapshot.</param>
    /// <returns>The snapshot text.</returns>
    public static string Snapshot(string root) =>
        string.Join(
            "\n",
            new DirectoryInfo(root)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(file => file.FullName, StringComparer.Ordinal)
                .Select(file => $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc:O}"));
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
