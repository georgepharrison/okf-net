namespace Okf.Core;

/// <summary>
/// One finding: a stable rule identifier, its resolved severity, a message, the file it
/// concerns, and the line when locating one is cheap (PRD CLI-15).
/// </summary>
public sealed class OkfDiagnostic : IComparable<OkfDiagnostic>
{
    /// <summary>Initializes a diagnostic.</summary>
    /// <param name="ruleId">The <c>OKF####</c> rule identifier.</param>
    /// <param name="severity">The resolved severity.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="path">The absolute path of the file the diagnostic concerns.</param>
    /// <param name="line">The 1-based line, or <see langword="null" /> when the rule concerns the whole file.</param>
    /// <param name="bundleRoot">The absolute path of the bundle the file belongs to.</param>
    public OkfDiagnostic(
        string ruleId,
        OkfSeverity severity,
        string message,
        string path,
        int? line = null,
        string? bundleRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(ruleId);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(path);
        RuleId = ruleId;
        Severity = severity;
        Message = message;
        Path = path;
        Line = line;
        BundleRoot = bundleRoot;
    }

    /// <summary>The <c>OKF####</c> rule identifier.</summary>
    public string RuleId { get; }

    /// <summary>The severity after configuration was applied.</summary>
    public OkfSeverity Severity { get; }

    /// <summary>The human-readable message.</summary>
    public string Message { get; }

    /// <summary>The absolute path of the file the diagnostic concerns.</summary>
    public string Path { get; }

    /// <summary>The 1-based line, or <see langword="null" /> when the rule concerns the whole file.</summary>
    public int? Line { get; }

    /// <summary>The absolute path of the bundle root the file belongs to, when known.</summary>
    public string? BundleRoot { get; }

    /// <summary>
    /// Orders diagnostics deterministically by path, then line, then rule identifier, so
    /// two runs over an unchanged tree emit byte-identical output (PRD ACC-7).
    /// </summary>
    /// <param name="other">The diagnostic to compare against.</param>
    /// <returns>A signed ordering value.</returns>
    public int CompareTo(OkfDiagnostic? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byPath = string.CompareOrdinal(Path, other.Path);
        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = (Line ?? 0).CompareTo(other.Line ?? 0);
        if (byLine != 0)
        {
            return byLine;
        }

        var byRule = string.CompareOrdinal(RuleId, other.RuleId);
        return byRule != 0 ? byRule : string.CompareOrdinal(Message, other.Message);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Path}{(Line is null ? string.Empty : ":" + Line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}: " +
        $"{Severity.ToConfigString()} {RuleId}: {Message}";
}
