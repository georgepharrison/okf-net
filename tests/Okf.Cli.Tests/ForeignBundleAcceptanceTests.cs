namespace Okf.Cli.Tests;

/// <summary>
/// PRD ACC-1, the foreign-bundle acid test: <c>okf lint</c> reports Google's four
/// reference bundles as conformant, exiting 0 under default severity. Warnings are
/// permitted and expected; any <em>error</em> on these bundles is a defect in okf-net, not
/// in the bundles.
/// </summary>
/// <remarks>
/// The bundles are read as-is from their upstream clone. The location is
/// <c>~/code/knowledge-catalog/okf/bundles</c> by default and can be pointed elsewhere
/// with <c>OKF_REFERENCE_BUNDLES</c>; where the clone is absent (a CI runner without it)
/// the test skips rather than passing vacuously.
/// </remarks>
public class ForeignBundleAcceptanceTests
{
    [SkippableTheory]
    [InlineData("acme_retail")]
    [InlineData("ga4")]
    [InlineData("stackoverflow")]
    [InlineData("crypto_bitcoin")]
    public void ReferenceBundleLintsWithoutErrors(string name)
    {
        var bundle = Path.Combine(ReferenceBundles(), name);
        Skip.IfNot(Directory.Exists(bundle), $"Reference bundle '{bundle}' is not present.");

        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", bundle);

        var errors = run.DiagnosticLines
            .Where(line => line.Contains(": error OKF", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(errors);
        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);

        // Anchored on the separator: a bare "0 errors" is also a substring of "10 errors".
        Assert.Contains(": 0 errors,", run.Summary, StringComparison.Ordinal);

        // The bundle was actually read. Without this the assertions above are satisfied
        // by an empty directory, which is how a silently mis-resolved path would look.
        Assert.DoesNotContain("Checked 0 files", run.Summary, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void EveryReferenceBundleLintsAtOnceWithoutErrors()
    {
        var bundles = ReferenceBundles();
        Skip.IfNot(Directory.Exists(bundles), $"Reference bundles directory '{bundles}' is not present.");

        using var home = new TempTree();

        // The parent of the four bundle roots is `okf/`, a vault: one invocation lints
        // all four, the shape a hook or CI job would use.
        var run = Cli.RunIn(home.Root, home.Root, "lint", Path.GetDirectoryName(bundles)!);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.DoesNotContain(": error OKF", run.Output, StringComparison.Ordinal);
        Assert.Contains("in 4 bundles", run.Summary, StringComparison.Ordinal);
        Assert.Contains(": 0 errors,", run.Summary, StringComparison.Ordinal);
    }

    private static string ReferenceBundles() =>
        Environment.GetEnvironmentVariable("OKF_REFERENCE_BUNDLES")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "code",
            "knowledge-catalog",
            "okf",
            "bundles");
}
