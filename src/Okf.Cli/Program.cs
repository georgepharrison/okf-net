using Okf.Core;

namespace Okf.Cli;

/// <summary>The <c>okf</c> entry point. All behavior lives in <see cref="CliApplication" />.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            // Diagnostic messages cite spec sections (§) and use en dashes.
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console attached (redirected or detached); the default encoding stands.
        }

        // Console.In is passed rather than reached for inside the command: `okf mcp` reads
        // JSON-RPC requests from it, and a test must be able to drive the server without
        // touching the process's real streams.
        return CliApplication.Run(args, OkfEnvironment.FromProcess(), Console.Out, Console.Error, Console.In);
    }
}
