namespace Okf.Core.Tests;

/// <summary>
/// The order a reader sees. PRD ACC-7 requires two runs over an unchanged tree to emit
/// byte-identical output, which needs a TOTAL order: every pair of diagnostics has to
/// compare non-zero unless they are the same finding.
/// </summary>
public class OkfDiagnosticTests
{
    private static OkfDiagnostic Diagnostic(string path, int? line, string ruleId, string message) =>
        new(ruleId, OkfSeverity.Warning, message, path, line);

    [Fact]
    public void OrderIsPathThenLineThenRuleThenMessage()
    {
        // Each pair differs in exactly one field and agrees on everything ahead of it,
        // and the input order is the opposite of the expected one, so a comparison that
        // stops early or returns zero leaves the list where it started.
        var diagnostics = new List<OkfDiagnostic>
        {
            Diagnostic("/b/z.md", 1, "OKF0001", "m"),
            Diagnostic("/b/a.md", 5, "OKF0001", "m"),
            Diagnostic("/b/a.md", 2, "OKF0002", "m"),
            Diagnostic("/b/a.md", 2, "OKF0001", "z"),
            Diagnostic("/b/a.md", 2, "OKF0001", "a"),
        };

        diagnostics.Sort();

        Assert.Equal(
            [
                ("/b/a.md", 2, "OKF0001", "a"),
                ("/b/a.md", 2, "OKF0001", "z"),
                ("/b/a.md", 2, "OKF0002", "m"),
                ("/b/a.md", 5, "OKF0001", "m"),
                ("/b/z.md", 1, "OKF0001", "m"),
            ],
            diagnostics.Select(d => (d.Path, d.Line, d.RuleId, d.Message)));
    }

    /// <summary>
    /// A <c>raw/</c> artifact keeps whatever name it was captured under, so OKF0310 can
    /// report on <c>notes</c> and <c>notes.pdf</c> in one run: one path is a prefix of the
    /// other, and only the length decides between them.
    /// </summary>
    [Fact]
    public void APathThatIsAPrefixOfAnotherSortsFirst()
    {
        var diagnostics = new List<OkfDiagnostic>
        {
            Diagnostic("/v/raw/notes.pdf", null, "OKF0310", "m"),
            Diagnostic("/v/raw/notes", null, "OKF0310", "m"),
        };

        diagnostics.Sort();

        Assert.Equal(["/v/raw/notes", "/v/raw/notes.pdf"], diagnostics.Select(d => d.Path));
    }

    /// <summary><see cref="IComparable{T}" />: everything sorts after a null.</summary>
    [Fact]
    public void ADiagnosticSortsAfterNothingAtAll() =>
        Assert.True(Diagnostic("/b/a.md", 1, "OKF0001", "m").CompareTo(null) > 0);

    [Fact]
    public void TheRenderedFormNamesTheLineOnlyWhenThereIsOne()
    {
        Assert.Equal(
            "/b/a.md:4: error OKF0001: Missing `type`.",
            new OkfDiagnostic("OKF0001", OkfSeverity.Error, "Missing `type`.", "/b/a.md", 4).ToString());

        Assert.Equal(
            "/b/a.md: warning OKF0301: No tags.",
            new OkfDiagnostic("OKF0301", OkfSeverity.Warning, "No tags.", "/b/a.md").ToString());
    }
}
