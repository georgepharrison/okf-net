namespace Okf.Core.Tests;

/// <summary>
/// The identity chain behind <c>okf verify</c> (PRD CLI-13, decisions.md Q6):
/// <c>verify.actor</c>, then the GLOBAL git email, then a refusal.
/// </summary>
public class OkfVerifyIdentityTests
{
    [Fact]
    public void ProjectConfigWinsOverGlobalConfigAndOverGit()
    {
        var resolution = OkfVerifyIdentity.Resolve(
            Config("""{ "verify": { "actor": "ringo" } }""", "project"),
            Config("""{ "verify": { "actor": "someone-else" } }""", "global"),
            () => "git@example.org");

        Assert.Equal("human:ringo", resolution.Actor);
        Assert.Contains("project", resolution.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalConfigWinsOverGit()
    {
        var resolution = OkfVerifyIdentity.Resolve(
            null,
            Config("""{ "verify": { "actor": "ringo" } }""", "global"),
            () => "git@example.org");

        Assert.Equal("human:ringo", resolution.Actor);
    }

    [Fact]
    public void AConfiguredActorMayAlreadyCarryTheHumanPrefix()
    {
        var resolution = OkfVerifyIdentity.Resolve(
            Config("""{ "verify": { "actor": "human:ringo" } }""", "project"),
            null,
            () => null);

        // Not `human:human:ringo`.
        Assert.Equal("human:ringo", resolution.Actor);
    }

    [Theory]
    [InlineData("process:nightly-check")]
    [InlineData("claude-fable/5")]
    public void AConfiguredActorThatIsNotAPersonIsAConfigurationError(string actor)
    {
        // CLI-13 stamps human review. A `process:` or `<producer>/<version>` identity behind
        // that command would be a machine confirmation wearing a person's prefix; `--by` is
        // the surface for those.
        var exception = Assert.Throws<OkfConfigException>(() => OkfVerifyIdentity.Resolve(
            Config($$"""{ "verify": { "actor": "{{actor}}" } }""", "project"),
            null,
            () => null));

        Assert.Contains(actor, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGitEmailBecomesAHumanActor()
    {
        var resolution = OkfVerifyIdentity.Resolve(null, null, () => "ringo.harrison@gmail.com");

        Assert.Equal("human:ringo.harrison@gmail.com", resolution.Actor);
        Assert.Equal("git config --global user.email", resolution.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithNoConfigAndNoGitEmailTheChainRefuses(string? email)
    {
        var resolution = OkfVerifyIdentity.Resolve(null, null, () => email);

        Assert.False(resolution.IsResolved);
        Assert.Null(resolution.Actor);
        Assert.Contains("verify.actor", resolution.Problem!, StringComparison.Ordinal);
        Assert.Contains("git config --global user.email", resolution.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGitReadTakesTheGlobalConfigAndNotTheRepositoryLocalOne()
    {
        // Q6's whole point, exercised against real git rather than a stub: this repository's
        // own local `user.email` is an agent address, and stamping it as a person would
        // manufacture the one signal §5.3 exists to carry.
        using var tree = new TempGitConfig();
        tree.WriteGlobal("ringo@example.org");
        var repository = tree.CreateRepositoryWithLocalEmail("agent@example.org");

        var email = OkfVerifyIdentity.GlobalUserEmail(tree.Environment(repository));

        Assert.Equal("ringo@example.org", email);
    }

    [Fact]
    public void AnUnsetGlobalEmailReadsAsNothingEvenWhenTheRepositoryHasOne()
    {
        using var tree = new TempGitConfig();
        tree.WriteGlobal(email: null);
        var repository = tree.CreateRepositoryWithLocalEmail("agent@example.org");

        Assert.Null(OkfVerifyIdentity.GlobalUserEmail(tree.Environment(repository)));
    }

    [Fact]
    public void TheRealGitReadFeedsTheChain()
    {
        using var tree = new TempGitConfig();
        tree.WriteGlobal("ringo@example.org");
        var environment = tree.Environment(tree.CreateRepositoryWithLocalEmail("agent@example.org"));

        var resolution = OkfVerifyIdentity.Resolve(null, null, () => OkfVerifyIdentity.GlobalUserEmail(environment));

        Assert.Equal("human:ringo@example.org", resolution.Actor);
    }

    private static OkfConfig Config(string json, string source) => OkfConfig.Parse(json, source);
}

/// <summary>
/// A throwaway git environment: a global config file, a repository with its own local
/// identity, and an <see cref="OkfEnvironment" /> that points a real <c>git</c> at both.
/// </summary>
internal sealed class TempGitConfig : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());

    public TempGitConfig() => Directory.CreateDirectory(this.root);

    /// <summary>The global config file the tests point <c>GIT_CONFIG_GLOBAL</c> at.</summary>
    public string GlobalConfigPath => Path.Combine(this.root, "gitconfig");

    /// <summary>Writes the global git config, with or without a <c>user.email</c>.</summary>
    /// <param name="email">The email to record, or null to write a config that sets none.</param>
    public void WriteGlobal(string? email) =>
        File.WriteAllText(GlobalConfigPath, email is null ? "[user]\n\tname = Nobody\n" : $"[user]\n\temail = {email}\n");

    /// <summary>Creates a git repository whose LOCAL <c>user.email</c> is an agent address.</summary>
    /// <param name="email">The repository-local email.</param>
    /// <returns>The repository's path.</returns>
    public string CreateRepositoryWithLocalEmail(string email)
    {
        var repository = Path.Combine(this.root, "repo");
        Directory.CreateDirectory(repository);
        Run(repository, "init", "--quiet");
        Run(repository, "config", "--local", "user.email", email);
        return repository;
    }

    /// <summary>
    /// An environment that makes the git read hermetic: the global config is this tree's
    /// file, the system config is switched off, and HOME cannot leak the operator's
    /// <c>~/.gitconfig</c> in.
    /// </summary>
    /// <param name="workingDirectory">Where the git process runs.</param>
    /// <returns>The environment.</returns>
    public OkfEnvironment Environment(string workingDirectory) =>
        new(
            workingDirectory,
            [
                new KeyValuePair<string, string>("HOME", this.root),
                new KeyValuePair<string, string>("XDG_CONFIG_HOME", Path.Combine(this.root, ".config")),
                new KeyValuePair<string, string>("GIT_CONFIG_GLOBAL", GlobalConfigPath),
                new KeyValuePair<string, string>("GIT_CONFIG_NOSYSTEM", "1"),
            ]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static void Run(string workingDirectory, params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The setup commands are hermetic too: `git init` reads the global config for
        // init.defaultBranch, and a repository created under the operator's templates is
        // not the repository this test described.
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(workingDirectory, "..", "gitconfig");
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
    }
}
