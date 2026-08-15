using Okf.Core;

namespace Okf.Cli.Tests;

/// <summary>
/// End-to-end <c>okf verify</c> behavior: the identity chain (PRD CLI-13, decisions.md
/// Q6), the self-verification refusal (§5.3), what the stamp leaves alone, and the
/// exit-code contract (CLI-14).
/// </summary>
public class VerifyCommandTests
{
    [Fact]
    public void HelpExitsZeroAndSaysWhichGitConfigIsRead()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "verify", "--help");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("okf verify <concept-path>...", run.Output, StringComparison.Ordinal);
        Assert.Contains("GLOBAL git config only", run.Output, StringComparison.Ordinal);
        Assert.Contains("--dry-run", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandIsReachableFromTheTopLevelUsage()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf verify <concept>...", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void NamingNoConceptIsAUsageFailure()
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "verify");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no concept named", run.Error, StringComparison.Ordinal);
        Assert.Empty(run.Output);
    }

    [Theory]
    [InlineData("--nope")]
    [InlineData("--by")]
    public void AMalformedCommandLineIsAUsageFailure(string argument)
    {
        using var home = new TempTree();
        var run = Cli.RunIn(home.Root, home.Root, "verify", "a.md", argument);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf: error:", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConfiguredActorIsStampedWithTheHumanPrefix()
    {
        using var tree = new Vault(actor: "ringo");
        var run = tree.Verify("bundles/b/widgets.md");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("verified by human:ringo at", run.Output, StringComparison.Ordinal);
        Assert.Contains("Verified 1 concept as human:ringo.", run.Output, StringComparison.Ordinal);

        var stamped = tree.Read("bundles/b/widgets.md");
        Assert.Contains("""  - { by: "human:ringo", at: """, stamped, StringComparison.Ordinal);
        Assert.Equal(
            OkfTrustTier.HumanReviewed,
            OkfDocument.TrustTier(OkfDocument.Parse(stamped).Frontmatter));
    }

    [Fact]
    public void TheStampClearsTheConceptFromTheInbox()
    {
        // PRD CLI-13: after stamping, the concept no longer appears in `okf inbox`. This is
        // the loop the two commands exist to close.
        using var tree = new Vault(actor: "ringo");

        var before = tree.Run("inbox", tree.Path("bundles/b"), "--fail-if-any");
        tree.Verify("bundles/b/widgets.md");
        var partial = tree.Run("inbox", tree.Path("bundles/b"), "--fail-if-any");
        tree.Verify("bundles/b/gadgets.md");
        var after = tree.Run("inbox", tree.Path("bundles/b"), "--fail-if-any");

        Assert.Equal(CliApplication.ExitDiagnostics, before.ExitCode);
        Assert.Contains("Unacknowledged (2)", before.Output, StringComparison.Ordinal);

        // One stamp clears one concept and leaves the other exactly where it was.
        Assert.Equal(CliApplication.ExitDiagnostics, partial.ExitCode);
        Assert.Contains("Unacknowledged (1)", partial.Output, StringComparison.Ordinal);
        Assert.Contains("gadgets.md", partial.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("widgets.md", partial.Output, StringComparison.Ordinal);

        Assert.Equal(CliApplication.ExitSuccess, after.ExitCode);
        Assert.Contains("nothing needs attention", after.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedTheBodyAndEveryOtherLineSurviveTheStamp()
    {
        using var tree = new Vault(actor: "ringo");
        var before = tree.Read("bundles/b/widgets.md");

        tree.Verify("bundles/b/widgets.md");
        var after = tree.Read("bundles/b/widgets.md");

        var added = after.Split('\n').Except(before.Split('\n'), StringComparer.Ordinal).ToArray();
        Assert.Equal(2, added.Length);
        Assert.Equal("verified:", added[0]);
        Assert.StartsWith("""  - { by: "human:ringo", at: """, added[1], StringComparison.Ordinal);
        Assert.Empty(before.Split('\n').Except(after.Split('\n'), StringComparer.Ordinal));
    }

    [Fact]
    public void DryRunReportsTheStampAndWritesNothing()
    {
        using var tree = new Vault(actor: "ringo");
        var before = tree.Read("bundles/b/widgets.md");

        var run = tree.Run("verify", tree.Path("bundles/b/widgets.md"), "--dry-run");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("""would append verified: - { by: "human:ringo", at: """, run.Output, StringComparison.Ordinal);
        Assert.Contains("--dry-run: nothing written", run.Output, StringComparison.Ordinal);
        Assert.Equal(before, tree.Read("bundles/b/widgets.md"));
    }

    [Fact]
    public void ByOverridesTheChainForASecondActorsMachineConfirmation()
    {
        using var tree = new Vault(actor: "ringo");

        var run = tree.Run("verify", tree.Path("bundles/b/widgets.md"), "--by", "process:nightly-schema-check");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        var frontmatter = OkfDocument.Parse(tree.Read("bundles/b/widgets.md")).Frontmatter;

        // A non-human verifier is machine-confirmed, not human-reviewed (§5.3).
        Assert.Equal(OkfTrustTier.MachineConfirmed, OkfDocument.TrustTier(frontmatter));
    }

    [Theory]
    [InlineData("ringo")]
    [InlineData("human:")]
    [InlineData("""human:x", at: 2020-01-01 }""")]
    public void ByRefusesAnythingThatIsNotASpecActor(string actor)
    {
        using var tree = new Vault(actor: "ringo");
        var before = tree.Read("bundles/b/widgets.md");

        var run = tree.Run("verify", tree.Path("bundles/b/widgets.md"), "--by", actor);

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("is not an actor", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, tree.Read("bundles/b/widgets.md"));
    }

    [Fact]
    public void SelfVerificationIsRefusedAndNothingIsWritten()
    {
        // decisions.md §7 and OKF0201: an actor may verify, but never its own output.
        using var tree = new Vault(actor: "ringo");
        var before = tree.Read("bundles/b/gadgets.md");

        // gadgets.md carries `generated: { by: claude-fable/5 }`.
        var run = tree.Run("verify", tree.Path("bundles/b/gadgets.md"), "--by", "claude-fable/5");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("must not verify itself", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, tree.Read("bundles/b/gadgets.md"));

        // A different actor on the same concept is fine: verification is a second actor's act.
        Assert.Equal(
            CliApplication.ExitSuccess,
            tree.Run("verify", tree.Path("bundles/b/gadgets.md"), "--by", "claude-opus/4").ExitCode);
    }

    [Fact]
    public void ARunThatRefusesOneConceptStampsNoneOfThem()
    {
        using var tree = new Vault(actor: "ringo");
        var first = tree.Read("bundles/b/widgets.md");
        var second = tree.Read("bundles/b/gadgets.md");

        var run = tree.Run(
            "verify",
            tree.Path("bundles/b/widgets.md"),
            tree.Path("bundles/b/gadgets.md"),
            "--by",
            "claude-fable/5");

        // gadgets.md is the one generated by claude-fable/5; a half-applied acknowledgment
        // would be worse than none, so widgets.md is left alone too.
        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Equal(first, tree.Read("bundles/b/widgets.md"));
        Assert.Equal(second, tree.Read("bundles/b/gadgets.md"));
    }

    [Fact]
    public void AMissingConceptIsAnEnvironmentFailureBeforeAnythingIsWritten()
    {
        using var tree = new Vault(actor: "ringo");
        var before = tree.Read("bundles/b/widgets.md");

        var run = tree.Run("verify", tree.Path("bundles/b/widgets.md"), tree.Path("bundles/b/nope.md"));

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no such concept", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, tree.Read("bundles/b/widgets.md"));
    }

    [Fact]
    public void AnUnparseableConceptIsRefusedWithAPointerToLint()
    {
        using var tree = new Vault(actor: "ringo");
        tree.Write("bundles/b/broken.md", "---\nkey: [unterminated\n---\n\nBody.\n");

        var run = tree.Run("verify", tree.Path("bundles/b/broken.md"));

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("okf lint", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoIdentityAnywhereTheCommandRefusesAndSaysWhatToSet()
    {
        using var tree = new Vault(actor: null);
        var before = tree.Read("bundles/b/widgets.md");

        var run = tree.Verify("bundles/b/widgets.md");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("verify.actor", run.Error, StringComparison.Ordinal);
        Assert.Contains("git config --global user.email", run.Error, StringComparison.Ordinal);
        Assert.Equal(before, tree.Read("bundles/b/widgets.md"));
    }

    [Fact]
    public void TheGlobalGitEmailIsTheFallbackAndTheRepositoryLocalOneIsNot()
    {
        // Q6's whole point, through the CLI and against real git: this repository's local
        // `user.email` is an agent address, and a human stamp must never be one.
        using var tree = new Vault(actor: null, gitGlobalEmail: "ringo@example.org", gitLocalEmail: "agent@example.org");

        var run = tree.Verify("bundles/b/widgets.md", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("human:ringo@example.org", tree.Read("bundles/b/widgets.md"), StringComparison.Ordinal);
        Assert.DoesNotContain("agent@example.org", tree.Read("bundles/b/widgets.md"), StringComparison.Ordinal);
        Assert.Contains(
            "okf: stamping as human:ringo@example.org (from git config --global user.email)",
            run.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AGlobalGitConfigWithNoEmailFallsThroughToTheRefusal()
    {
        using var tree = new Vault(actor: null, gitGlobalEmail: null, gitLocalEmail: "agent@example.org");

        var run = tree.Verify("bundles/b/widgets.md");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("no verifying identity", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigWinsOverTheGitEmail()
    {
        using var tree = new Vault(actor: "ringo", gitGlobalEmail: "someone-else@example.org");

        tree.Verify("bundles/b/widgets.md");

        Assert.Contains("human:ringo", tree.Read("bundles/b/widgets.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitConfigFileIsReadInsteadOfTheVaults()
    {
        using var tree = new Vault(actor: "ringo");
        tree.Write("elsewhere.json", """{ "verify": { "actor": "human:someone" } }""");

        tree.Run("verify", tree.Path("bundles/b/widgets.md"), "--config", tree.Path("elsewhere.json"));

        Assert.Contains("human:someone", tree.Read("bundles/b/widgets.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingExplicitConfigFileIsAUsageFailure()
    {
        using var tree = new Vault(actor: "ringo");

        var run = tree.Run("verify", tree.Path("bundles/b/widgets.md"), "--config", tree.Path("nope.json"));

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("No such config file", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfiguredActorThatIsNotAPersonIsAConfigurationFailure()
    {
        using var tree = new Vault(actor: "process:nightly");

        var run = tree.Verify("bundles/b/widgets.md");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("stamps human review", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A throwaway vault holding two concepts, an <c>okf.json</c>, and — where a test asks
    /// for one — a git installation pointed at temporary global and repository-local
    /// identities, so the Q6 fallback is exercised against real git without reading the
    /// operator's own configuration.
    /// </summary>
    private sealed class Vault : IDisposable
    {
        private readonly TempTree tree = new();
        private readonly List<KeyValuePair<string, string>> variables;

        public Vault(string? actor, string? gitGlobalEmail = null, string? gitLocalEmail = null)
        {
            this.tree.CreateDirectory("okf/bundles/b");
            Write(
                "bundles/b/widgets.md",
                """
                ---
                type: Concept
                title: Widgets
                okf_version: "0.2"
                generated: { by: claude-opus/4, at: 2026-08-14T20:41:50-05:00 }
                sources:
                  - id: spec
                    resource: https://example.org/spec
                    last_modified: 2020-01-01
                ---

                Body, with a `[^spec]` footnote.

                """.ReplaceLineEndings("\n"));
            Write(
                "bundles/b/gadgets.md",
                "---\ntype: Concept\ntitle: Gadgets\ngenerated: { by: claude-fable/5, at: 2026-08-14T20:41:50-05:00 }\n---\n\nBody.\n");

            if (actor is not null)
            {
                Write("okf.json", $$"""{ "verify": { "actor": "{{actor}}" } }""");
            }

            var home = this.tree.CreateDirectory("home");
            this.variables =
            [
                new KeyValuePair<string, string>("HOME", home),
                new KeyValuePair<string, string>("XDG_CONFIG_HOME", System.IO.Path.Combine(home, ".config")),
                new KeyValuePair<string, string>("GIT_CONFIG_NOSYSTEM", "1"),
            ];

            // No GIT_CONFIG_GLOBAL at all would let the operator's ~/.gitconfig decide a
            // test's outcome; an empty file is the hermetic "git has nothing to say".
            var gitConfig = this.tree.Write(
                "home/gitconfig",
                gitGlobalEmail is null ? "[user]\n\tname = Nobody\n" : $"[user]\n\temail = {gitGlobalEmail}\n");
            this.variables.Add(new KeyValuePair<string, string>("GIT_CONFIG_GLOBAL", gitConfig));

            if (gitLocalEmail is not null)
            {
                Git("init", "--quiet");
                Git("config", "--local", "user.email", gitLocalEmail);
            }
        }

        public string Path(string relativePath) => System.IO.Path.Combine(this.tree.Root, "okf", relativePath);

        public string Read(string relativePath) => File.ReadAllText(Path(relativePath));

        public string Write(string relativePath, string content) =>
            this.tree.Write(System.IO.Path.Combine("okf", relativePath), content);

        public CliRun Run(params string[] args) =>
            Cli.Run(new OkfEnvironment(System.IO.Path.Combine(this.tree.Root, "okf"), this.variables), args);

        public CliRun Verify(string relativePath, params string[] options) =>
            Run(["verify", Path(relativePath), .. options]);

        public void Dispose() => this.tree.Dispose();

        private void Git(params string[] args)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = System.IO.Path.Combine(this.tree.Root, "okf"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (var argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var variable in this.variables)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }

            using var process = System.Diagnostics.Process.Start(startInfo)!;
            process.WaitForExit();
        }
    }
}
