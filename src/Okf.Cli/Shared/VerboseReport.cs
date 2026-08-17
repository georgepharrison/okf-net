using Okf.Core;

namespace Okf.Cli.Shared;

/// <summary>
/// The lines a <c>--verbose</c> run writes about what it is looking at, before it does
/// anything with it.
/// </summary>
internal static class VerboseReport
{
    /// <summary>
    /// Reports the scope in force and the configuration layer that set it. PRD CLI-4 asks
    /// <c>--verbose</c> for the effective value of anything that changed behaviour, and
    /// MCP-3 makes <c>okf search</c> and <c>okf mcp</c> agree down to the layer they read
    /// it from — which is a claim about one sentence, written once.
    /// </summary>
    /// <param name="error">Where verbose output goes.</param>
    /// <param name="scope">The resolved scope and its layer.</param>
    public static void Scope(TextWriter error, ScopeSettings scope) =>
        error.WriteLine($"okf: scope {scope.Scope.ToScopeString()} (from {scope.Layer})");

    /// <summary>
    /// Reports the resolved working set: the sentence discovery or scope resolution
    /// produced, then one line per bundle in it. PRD CLI-4 asks every command to say what
    /// it resolved, and CLI-1 makes that answer the same one across commands — so it is
    /// written here rather than spelled out at each of them.
    /// </summary>
    /// <param name="error">Where verbose output goes; never stdout, which carries the result.</param>
    /// <param name="workingSet">The resolved working set.</param>
    /// <param name="notes">
    /// What resolution had to say for itself — a registry entry that resolved to nothing, a
    /// missing project vault. They sit between the sentence and the bundles because they
    /// explain the sentence.
    /// </param>
    public static void WorkingSet(
        TextWriter error,
        OkfWorkingSet workingSet,
        IReadOnlyList<string>? notes = null)
    {
        error.WriteLine($"okf: resolved {workingSet.Resolution}");
        foreach (var note in notes ?? [])
        {
            error.WriteLine($"okf: {note}");
        }

        foreach (var bundle in workingSet.Bundles)
        {
            error.WriteLine($"okf: bundle {bundle.Root}");
        }
    }
}
