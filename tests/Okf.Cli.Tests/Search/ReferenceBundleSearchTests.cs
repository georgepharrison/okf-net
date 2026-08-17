using System.Text.Json;

namespace Okf.Cli.Tests.Search;

/// <summary>
/// <c>okf search</c> against Google's four reference bundles — the same foreign-bundle acid
/// test PRD ACC-1 applies to <c>okf lint</c>, applied to search: a bundle okf-net did not
/// produce must be searchable with no in-bundle cooperation (PRD §1.1).
/// </summary>
/// <remarks>
/// The clone is read as-is and never written to; where it is absent these tests skip
/// visibly rather than passing vacuously. <c>OKF_REFERENCE_BUNDLES</c> relocates it.
/// </remarks>
public class ReferenceBundleSearchTests
{
    [SkippableFact]
    public void SearchingTheReferenceBundlesRanksTheConceptTheQueryNames()
    {
        var vault = Vault();
        using var home = new TempTree();

        var run = CliHarness.RunIn(home.Root, home.Root, "search", "revenue recognition", vault, "--json", "--limit", "3");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        var results = document.RootElement.EnumerateArray().ToList();

        Assert.NotEmpty(results);

        // The bundle's revenue-recognition policy is the concept that query names; it
        // outranks the concepts that merely cite it.
        Assert.EndsWith(
            "acme_retail/policies/revenue-recognition.md",
            results[0].GetProperty("absolutePath").GetString()!.Replace('\\', '/'),
            StringComparison.Ordinal);
        Assert.Equal("all", results[0].GetProperty("matchMode").GetString());

        Assert.All(results, result =>
        {
            Assert.True(result.GetProperty("score").GetDouble() > 0);
            Assert.NotEmpty(result.GetProperty("snippet").GetString()!);
            Assert.Contains(
                result.GetProperty("trustTier").GetString(),
                (string[])["unverified", "machine-confirmed", "human-reviewed"]);
        });
    }

    [SkippableFact]
    public void FiltersWorkOnBundlesOkfNetDidNotProduce()
    {
        var vault = Vault();
        using var home = new TempTree();

        var run = CliHarness.RunIn(home.Root, home.Root, "search", "type:Metric", vault, "--json", "--limit", "50");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        var results = document.RootElement.EnumerateArray().ToList();

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal("Metric", result.GetProperty("type").GetString()));
        Assert.All(results, result => Assert.Equal("filter", result.GetProperty("matchMode").GetString()));
    }

    [SkippableFact]
    public void SearchingTheCloneIsRepeatableAndWritesNothingToIt()
    {
        var vault = Vault();
        var bundles = ReferenceBundles.Root;
        using var home = new TempTree();

        var before = ReferenceBundles.Snapshot(bundles);
        var first = CliHarness.RunIn(home.Root, home.Root, "search", "metric definition", vault);
        var second = CliHarness.RunIn(home.Root, home.Root, "search", "metric definition", vault);

        Assert.Equal(CliApplication.ExitSuccess, first.ExitCode);
        Assert.Contains("across 4 bundles", first.Summary, StringComparison.Ordinal);
        Assert.Equal(first.Output, second.Output);

        // Search is a read: the upstream clone is byte-for-byte where it was.
        Assert.Equal(before, ReferenceBundles.Snapshot(bundles));
    }

    /// <summary>
    /// The vault holding the four bundle roots — the parent of <c>bundles/</c>, which is
    /// how one invocation searches all four (PRD CLI-1).
    /// </summary>
    private static string Vault()
    {
        var bundles = ReferenceBundles.Root;
        Skip.IfNot(Directory.Exists(bundles), $"Reference bundles directory '{bundles}' is not present.");
        return Path.GetDirectoryName(bundles)!;
    }
}
