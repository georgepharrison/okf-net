namespace Okf.Cli.Tests;

/// <summary>
/// The command surface and its exit-code contract (PRD §3, CLI-14): usage failures are
/// exit 2, and they never masquerade as lint failures.
/// </summary>
public class CliSurfaceTests
{
    [Fact]
    public void HelpExitsZeroAndDescribesLint()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf lint", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void NoArgumentsIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf lint", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionIsReported()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "version");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        // Version-shaped, not merely non-empty: an unstamped or mis-stamped build that
        // printed an empty string or a bare commit sha would pass `NotEmpty`.
        Assert.Matches(@"^\d+\.\d+\.\d+", run.Output.Trim());
    }

    /// <summary>
    /// A published binary is stamped `-p:Version=<c>X.Y.Z-rc.N</c>` plus
    /// `-p:SourceRevisionId=<c>&lt;short-sha&gt;</c>`, which the SDK joins into
    /// `X.Y.Z-rc.N+sha` (issue #10). `okf version` shows the whole thing — the sha is
    /// the half that identifies which build of a rebuildable tag is in front of you.
    /// </summary>
    [Theory]
    [InlineData("1.0.0-rc.14+abc1234", "1.0.0-rc.14+abc1234")]
    [InlineData("1.0.0-rc.14", "1.0.0-rc.14")]
    [InlineData("0.0.0-dev", "0.0.0-dev")]
    [InlineData(null, CliApplication.UnknownVersion)]
    [InlineData("", CliApplication.UnknownVersion)]
    public void DescribeKeepsBuildMetadata(string? informationalVersion, string expected) =>
        Assert.Equal(expected, CliApplication.Describe(informationalVersion));

    /// <summary>
    /// MCP's `serverInfo.version` is a value a client may parse or compare, so it stays a
    /// bare semantic version: build metadata is not part of precedence (semver §10).
    /// </summary>
    [Theory]
    [InlineData("1.0.0-rc.14+abc1234", "1.0.0-rc.14")]
    [InlineData("1.0.0+abc1234", "1.0.0")]
    [InlineData("1.0.0-rc.14", "1.0.0-rc.14")]
    [InlineData(null, CliApplication.UnknownVersion)]
    public void SemanticVersionDropsBuildMetadata(string? informationalVersion, string expected) =>
        Assert.Equal(expected, CliApplication.SemanticVersion(informationalVersion));

    [Fact]
    public void ListRulesCoversEveryShippedRule()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", "--list-rules");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        foreach (var rule in Okf.Core.OkfRules.All)
        {
            Assert.Contains(rule.Id, run.Output, StringComparison.Ordinal);
            Assert.Contains(rule.Nickname, run.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("Lint")]
    public void AnUnknownCommandIsAUsageFailure(string command)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, command);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("unknown command", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "lint", "--nope" }, "Unknown option")]
    [InlineData(new[] { "lint", "--format" }, "requires a value")]
    [InlineData(new[] { "lint", "--format", "xml" }, "Unknown --format value")]
    [InlineData(new[] { "lint", "--severity", "OKF0301" }, "--severity expects")]
    [InlineData(new[] { "lint", "--severity", "OKF0301=loud" }, "--severity expects")]
    [InlineData(new[] { "lint", "one", "two" }, "at most one path")]
    public void MalformedArgumentsAreUsageFailures(string[] args, string expected)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, args);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains(expected, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownRuleIdOnTheCommandLineIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(
            home.Root, home.Root, "lint", Fixtures.Bundle("conformant"), "--severity", "OKF4242=error");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("OKF4242", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingPathIsAnEnvironmentFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "lint", Path.Combine(home.Root, "nowhere"));

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("No such directory", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void NoVaultAnywhereIsAnEnvironmentFailure()
    {
        using var tree = new TempTree();
        var workingDirectory = tree.CreateDirectory("empty");

        var run = Cli.RunIn(workingDirectory, tree.Root, "lint");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("No project vault found", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePersonalVaultIsTheFallbackTarget()
    {
        using var tree = new TempTree();
        tree.CopyFixture("warnings-only", Path.Combine("personal", "bundles", "notes"));
        var workingDirectory = tree.CreateDirectory("empty");

        var run = Cli.Run(
            Cli.Environment(workingDirectory, tree.Root, okfHome: Path.Combine(tree.Root, "personal")),
            "lint",
            "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("personal vault", run.Error, StringComparison.Ordinal);
        Assert.Contains("warning OKF0301", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AVaultPathLintsEveryBundleInIt()
    {
        using var tree = new TempTree();
        tree.CopyFixture("conformant", Path.Combine("okf", "bundles", "clean"));
        tree.CopyFixture("warnings-only", Path.Combine("okf", "bundles", "noisy"));

        var run = Cli.RunIn(tree.Root, tree.Root, "lint", Path.Combine(tree.Root, "okf"));

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("Checked 7 files in 2 bundles", run.Output, StringComparison.Ordinal);
    }
}
