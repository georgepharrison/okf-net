using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// <c>okf mcp [path]</c> — runs the MCP server over stdio from the same binary
/// (PRD MCP-1; decisions.md §4's kcmd precedent). No separate install, no port, no network:
/// the server is an adapter over <c>Okf.Core</c>, launched as a subcommand like every other.
/// </summary>
internal static class McpCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>mcp</c>.</param>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="input">The JSON-RPC request stream (stdin).</param>
    /// <param name="output">The JSON-RPC response stream (stdout); nothing else is written to it.</param>
    /// <param name="error">Where startup failures and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code: 0 on clean shutdown, 2 on startup failure.</returns>
    public static int Run(
        string[] args,
        OkfEnvironment environment,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        string? path = null;
        var verbose = false;

        foreach (var argument in args)
        {
            switch (argument)
            {
                case "--help" or "-h":
                    WriteUsage(output);
                    return CliApplication.ExitSuccess;

                case "--verbose" or "-v":
                    verbose = true;
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        error.WriteLine($"okf: error: Unknown option '{argument}'.");
                        error.WriteLine("Run `okf mcp --help` for usage.");
                        return CliApplication.ExitUsage;
                    }

                    if (path is not null)
                    {
                        error.WriteLine($"okf: error: `okf mcp` takes at most one path; got '{path}' and '{argument}'.");
                        error.WriteLine("Run `okf mcp --help` for usage.");
                        return CliApplication.ExitUsage;
                    }

                    path = argument;
                    break;
            }
        }

        var tools = new McpToolset(environment, path);

        try
        {
            // Resolution is checked before a single byte of protocol is spoken: a server that
            // cannot say which bundles it serves is a startup failure (exit 2, PRD §3's CLI
            // surface table), not a run of tools that all fail the same way.
            var workingSet = tools.Resolve();
            if (verbose)
            {
                error.WriteLine($"okf: resolved {workingSet.Resolution}");
                foreach (var bundle in workingSet.Bundles)
                {
                    error.WriteLine($"okf: bundle {bundle.Root}");
                }

                error.WriteLine("okf: mcp server ready on stdio");
            }
        }
        catch (McpProtocolException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }

        return new McpServer(tools, input, output).Run();
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf mcp [path] [options]

            Runs okf's MCP server over stdio: a read-only adapter that lets an agent orient in
            the resolved bundles (okf_list), search them (okf_search), and open one concept at
            a time (okf_read). Requests are JSON-RPC 2.0, one message per line on stdin;
            responses go to stdout and nothing else does. The server shuts down cleanly when
            stdin closes.

            There is no network transport and no write tool: stamping (`okf verify`) and index
            generation (`okf index`) stay CLI operations.

            Arguments:
              path                          A bundle root, a vault (a directory holding
                                            bundles/), or a project root (holding
                                            okf/bundles/). Defaults to the vault found by
                                            walking up from the working directory, then to
                                            the personal vault (OKF_HOME, else ~/okf).

            Options:
              --verbose, -v                 Report vault resolution on stderr at startup
              --help, -h                    Show this help

            Configure a client to run it, e.g.:

              { "command": "okf", "args": ["mcp", "/path/to/project"] }

            Exit codes:
              0  clean shutdown (stdin closed)
              2  startup failure (bad arguments, unresolvable vault)
            """);
    }
}
