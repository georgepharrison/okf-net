namespace Okf.Cli.Tests.Lint;

/// <summary>
/// The <c>okf lint</c> sentences that carry the rule counts, built from
/// <see cref="OkfRules" /> rather than spelled out (issue #73). A test that hardcodes the
/// catalog size fails when an unrelated feature adds a rule, which trains everyone to bump
/// a number nobody read.
/// </summary>
/// <remarks>
/// The expected sentence is written out here rather than produced by calling
/// <c>DiagnosticWriter</c> or <c>LintCommand</c>: a test that renders its expectation with
/// the renderer under test can never fail. That makes this a second copy of the rendering,
/// which is the point — change the shape of either sentence in <c>src</c> and these
/// assertions go red. The counts themselves are resolved through
/// <see cref="OkfSeverityResolver" />, the catalog's own reading, not read back off
/// <c>OkfLintResult</c>.
/// </remarks>
internal static class LintSummary
{
    /// <summary>
    /// The rules a run reports as live, and the ones it computed at hidden severity, for a
    /// run carrying the given <c>--severity</c> overrides.
    /// </summary>
    /// <param name="overrides">Severity overrides, each <c>OKF####=level</c>.</param>
    public static (int Active, int Hidden) LiveRules(params string[] overrides) => LiveRulesFor([.. overrides]);

    /// <summary>The <c>19 rules: 17 active, 2 hidden</c> clause, for a run carrying the given overrides.</summary>
    /// <param name="overrides">Severity overrides, each <c>OKF####=level</c>.</param>
    public static string RuleCountClause(params string[] overrides) => RuleCountClause(LiveRules(overrides));

    /// <summary>The <c>19 rules: 17 active, 2 hidden</c> clause.</summary>
    /// <param name="counts">The live and hidden rule counts.</param>
    public static string RuleCountClause((int Active, int Hidden) counts) =>
        $"{Plural(OkfRules.All.Count, "rule")}: {counts.Active} active, {counts.Hidden} hidden";

    /// <summary>The line <c>okf lint</c> ends its human-readable report with.</summary>
    /// <param name="files">Files checked.</param>
    /// <param name="bundles">Bundles checked.</param>
    /// <param name="counts">The live and hidden rule counts.</param>
    /// <param name="errors">Diagnostics at error severity.</param>
    /// <param name="warnings">Diagnostics at warning severity.</param>
    /// <param name="infos">Diagnostics at info severity.</param>
    public static string SummaryLine(
        int files,
        int bundles,
        (int Active, int Hidden) counts,
        int errors,
        int warnings,
        int infos) =>
        $"Checked {Plural(files, "file")} in {Plural(bundles, "bundle")} ({RuleCountClause(counts)}): " +
        $"{Plural(errors, "error")}, {Plural(warnings, "warning")}, {Plural(infos, "info", "infos")}.";

    /// <summary>
    /// The rule-count line <c>--verbose</c> writes to standard error, for a run carrying the
    /// given overrides.
    /// </summary>
    /// <param name="overrides">Severity overrides, each <c>OKF####=level</c>.</param>
    public static string VerboseRuleCountLine(params string[] overrides) =>
        $"okf: {RuleCountClause(LiveRules(overrides))}";

    /// <summary>
    /// Resolves the live and hidden counts through the precedence the CLI applies: the
    /// built-in defaults, then the command line. Config-file layers are absent because no
    /// test here overrides a severity through one.
    /// </summary>
    /// <param name="overrides">Severity overrides, each <c>OKF####=level</c>.</param>
    private static (int Active, int Hidden) LiveRulesFor(string[] overrides)
    {
        var commandLine = new OkfSeverityLayer("command line");
        foreach (var setting in overrides)
        {
            int separator = setting.IndexOf('=', StringComparison.Ordinal);
            commandLine.Severities[setting[..separator]] =
                OkfSeverityExtensions.TryParse(setting[(separator + 1)..], out OkfSeverity severity)
                    ? severity
                    : throw new ArgumentException($"Unknown severity in '{setting}'.");
        }

        var severities = new OkfSeverityResolver([commandLine]);
        int active = OkfRules.All.Count(rule => severities.Resolve(rule.Id) != OkfSeverity.Hidden);
        return (active, OkfRules.All.Count - active);
    }

    private static string Plural(int count, string singular, string? plural = null) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";
}

