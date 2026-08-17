using System.Globalization;
using Okf.Core;

namespace Okf.Cli.Shared;

/// <summary>
/// Renders diagnostics. Human output is one line per diagnostic, stable enough to grep;
/// JSON output is the contract CI annotations and other tooling read (PRD CLI-15).
/// </summary>
internal static class DiagnosticWriter
{
    /// <summary>Writes the human-readable report and its summary.</summary>
    /// <param name="diagnostics">The diagnostics to report, already ordered.</param>
    /// <param name="result">The run the diagnostics came from, for the summary counts.</param>
    /// <param name="baseDirectory">Paths are printed relative to this directory when they sit under it.</param>
    /// <param name="output">Where to write.</param>
    public static void WriteText(
        IReadOnlyList<OkfDiagnostic> diagnostics,
        OkfLintResult result,
        string baseDirectory,
        TextWriter output)
    {
        foreach (OkfDiagnostic diagnostic in diagnostics)
        {
            string location = diagnostic.Line is { } line
                ? $"{Display(diagnostic.Path, baseDirectory)}:{line.ToString(CultureInfo.InvariantCulture)}"
                : Display(diagnostic.Path, baseDirectory);
            output.WriteLine(
                $"{location}: {diagnostic.Severity.ToConfigString()} {diagnostic.RuleId}: {diagnostic.Message}");
        }

        int errors = result.Count(OkfSeverity.Error);
        int warnings = result.Count(OkfSeverity.Warning);
        int infos = result.Count(OkfSeverity.Info);

        // What ran is part of the result: "0 errors" from a run with every rule hidden
        // reads exactly like "0 errors" from a run with all of them live, and a
        // misconfiguration that silences the gate should not look like a passing gate.
        output.WriteLine(
            $"Checked {Plural(result.FileCount, "file")} in {Plural(result.Bundles.Count, "bundle")} " +
            $"({Plural(OkfRules.All.Count, "rule")}: {result.ActiveRuleCount.ToString(CultureInfo.InvariantCulture)} " +
            $"active, {result.HiddenRuleCount.ToString(CultureInfo.InvariantCulture)} hidden): " +
            $"{Plural(errors, "error")}, {Plural(warnings, "warning")}, {Plural(infos, "info", "infos")}.");
    }

    /// <summary>Renders the diagnostics as the stable JSON array.</summary>
    /// <param name="diagnostics">The diagnostics to report, already ordered.</param>
    /// <param name="baseDirectory">Paths are reported relative to this directory when they sit under it.</param>
    /// <returns>The JSON text, newline-terminated.</returns>
    public static string ToJson(IReadOnlyList<OkfDiagnostic> diagnostics, string baseDirectory)
    {
        return JsonOutput.Write(writer =>
        {
            writer.WriteStartArray();
            foreach (OkfDiagnostic diagnostic in diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("id", diagnostic.RuleId);
                writer.WriteString("rule", OkfRules.Get(diagnostic.RuleId).Nickname);
                writer.WriteString("severity", diagnostic.Severity.ToConfigString());
                writer.WriteString("path", Display(diagnostic.Path, baseDirectory));
                writer.WriteString("absolutePath", diagnostic.Path);
                if (diagnostic.Line is { } line)
                {
                    writer.WriteNumber("line", line);
                }
                else
                {
                    writer.WriteNull("line");
                }

                writer.WriteString("bundle", diagnostic.BundleRoot ?? string.Empty);
                writer.WriteString("message", diagnostic.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }) + Environment.NewLine;
    }

    /// <summary>
    /// Renders a path for output: relative to the working directory when it sits under
    /// it, absolute otherwise, always with <c>/</c> separators.
    /// </summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="baseDirectory">The directory to report relative to.</param>
    /// <returns>The display form.</returns>
    public static string Display(string path, string baseDirectory)
    {
        string relative = Path.GetRelativePath(baseDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Renders a count with its noun, pluralized.</summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The singular noun.</param>
    /// <param name="plural">The plural noun, when it is not the singular plus <c>s</c>.</param>
    /// <returns>The rendered phrase, e.g. <c>3 files</c>.</returns>
    public static string Plural(int count, string singular, string? plural = null) =>
        count == 1
            ? $"1 {singular}"
            : $"{count.ToString(CultureInfo.InvariantCulture)} {plural ?? singular + "s"}";
}
