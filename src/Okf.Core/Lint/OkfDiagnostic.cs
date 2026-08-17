namespace Okf.Core.Lint;

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
    /// <remarks>
    /// The path comparison ranks <c>\</c> as <c>/</c>, which is what keeps this order the
    /// same on Windows as on Linux. It has to be done HERE and not upstream:
    /// <see cref="OkfLinter" /> re-sorts every diagnostic before returning, so the bundle
    /// walk's order — already the bundle-relative one since work item #36 — is discarded by
    /// the time output is written, and this comparator is what a reader actually sees. Left
    /// ordinal it compares a native absolute path, and <c>\</c> (0x5C) sorts above the
    /// letters where <c>/</c> (0x2F) sorts below them: a diagnostic in
    /// <c>topics/deep/widgets.md</c> would come out on the opposite side of one in
    /// <c>topicsZ.md</c> depending on which machine ran the lint.
    /// </remarks>
    /// <param name="other">The diagnostic to compare against.</param>
    /// <returns>A signed ordering value.</returns>
    public int CompareTo(OkfDiagnostic? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byPath = ComparePaths(Path, other.Path);
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
        if (byRule != 0)
        {
            return byRule;
        }

        var byMessage = string.CompareOrdinal(Message, other.Message);

        // The raw ordinal tiebreak keeps this a TOTAL order. Ranking '\' as '/' makes two
        // spellings of one path compare equal, and two diagnostics that compared equal
        // would have their order decided by an unstable sort rather than by this method.
        return byMessage != 0 ? byMessage : string.CompareOrdinal(Path, other.Path);
    }

    /// <summary>
    /// Ordinal path comparison in which <c>\</c> ranks as <c>/</c>, so the order is the
    /// spec's separator rather than the host's. Allocation-free: this runs inside a sort.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns>A signed ordering value.</returns>
    private static int ComparePaths(string left, string right)
    {
        var shared = Math.Min(left.Length, right.Length);
        for (var index = 0; index < shared; index++)
        {
            int leftChar = Rank(left[index]);
            int rightChar = Rank(right[index]);
            if (leftChar != rightChar)
            {
                return leftChar - rightChar;
            }
        }

        return left.Length - right.Length;
    }

    /// <summary>The ordering rank of one path character.</summary>
    /// <param name="value">The character to rank.</param>
    /// <returns><c>/</c> for either separator, the character itself otherwise.</returns>
    private static char Rank(char value) => value == '\\' ? '/' : value;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Path}{(Line is null ? string.Empty : ":" + Line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}: " +
        $"{Severity.ToConfigString()} {RuleId}: {Message}";
}
