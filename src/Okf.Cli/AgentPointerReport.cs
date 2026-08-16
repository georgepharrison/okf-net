using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// Writes the <c>AGENTS.md</c> / <c>CLAUDE.md</c> context pointer at a project root and
/// reports each file, for the two verbs that plant one — <c>okf init</c> and
/// <c>okf skills install --scope project</c> (work item #56). One place, so both verbs
/// report the same word for the same outcome.
/// </summary>
internal static class AgentPointerReport
{
    /// <summary>Writes both pointer files and reports what became of each.</summary>
    /// <param name="projectRoot">The project root — the vault's parent, or the working directory.</param>
    /// <param name="environment">The environment paths are displayed relative to.</param>
    /// <param name="output">Where the report goes.</param>
    /// <exception cref="IOException">A file could not be written.</exception>
    public static void Write(string projectRoot, OkfEnvironment environment, TextWriter output)
    {
        foreach (var file in OkfAgentPointer.Write(projectRoot))
        {
            output.WriteLine(
                $"{DiagnosticWriter.Display(file.Path, environment.CurrentDirectory)}: {Verb(file.Status)}");
        }
    }

    private static string Verb(OkfAgentPointerStatus status) => status switch
    {
        OkfAgentPointerStatus.Created => "created",
        OkfAgentPointerStatus.Updated => "updated",
        OkfAgentPointerStatus.Unchanged => "unchanged",
        OkfAgentPointerStatus.Refused => "left as found (more than one okf fence; remove the extras and re-run)",
        _ => "skipped (exists)",
    };
}
