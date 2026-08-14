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

        return CliApplication.Run(args, OkfEnvironment.FromProcess(), Console.Out, Console.Error);
    }
}
