namespace Okf.Core.Tests.Vault;

/// <summary>Vault and bundle discovery (PRD CORE-13, CLI-1).</summary>
public sealed class OkfDiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());

    /// <summary>Builds a project vault with two bundles and a stray directory beside it.</summary>
    public OkfDiscoveryTests()
    {
        Directory.CreateDirectory(Path.Combine(this.root, "project", "okf", "bundles", "alpha", "tables"));
        Directory.CreateDirectory(Path.Combine(this.root, "project", "okf", "bundles", "beta"));
        Directory.CreateDirectory(Path.Combine(this.root, "project", "src"));
        Directory.CreateDirectory(Path.Combine(this.root, "foreign-bundle"));
    }

    [Fact]
    public void WalksUpFromTheWorkingDirectoryToTheNearestVault()
    {
        var environment = Environment(Path.Combine(this.root, "project", "src"));
        var resolved = OkfDiscovery.Resolve(null, environment);

        Assert.Equal(Path.Combine(this.root, "project", "okf"), resolved.VaultRoot);
        Assert.Equal(["alpha", "beta"], resolved.Bundles.Select(bundle => bundle.Name));
        Assert.Equal(Path.Combine(this.root, "project", "okf", "okf.json"), resolved.ProjectConfigPath);
    }

    [Fact]
    public void WorksFromInsideTheVaultItself()
    {
        var environment = Environment(Path.Combine(this.root, "project", "okf", "bundles", "alpha", "tables"));

        Assert.Equal(Path.Combine(this.root, "project", "okf"), OkfDiscovery.Resolve(null, environment).VaultRoot);
    }

    /// <summary>
    /// The name <c>okf</c> is not enough on its own: a directory called that which holds no
    /// <c>bundles/</c> is somebody's notes folder, and the walk goes past it to the real
    /// vault above rather than stopping on the name.
    /// </summary>
    [Fact]
    public void ADirectoryCalledOkfWithNoBundlesIsWalkedPast()
    {
        var stray = Path.Combine(this.root, "project", "src", "okf");
        Directory.CreateDirectory(stray);

        var resolved = OkfDiscovery.Resolve(null, Environment(stray));

        Assert.Equal(Path.Combine(this.root, "project", "okf"), resolved.VaultRoot);
        Assert.Equal(["alpha", "beta"], resolved.Bundles.Select(bundle => bundle.Name));
    }

    [Fact]
    public void AnExplicitProjectRootExpandsToItsVault()
    {
        var resolved = OkfDiscovery.Resolve(Path.Combine(this.root, "project"), Environment(this.root));

        Assert.Equal(Path.Combine(this.root, "project", "okf"), resolved.VaultRoot);
        Assert.Equal(2, resolved.Bundles.Count);
    }

    [Fact]
    public void AnExplicitVaultExpandsToEveryBundleInIt()
    {
        var resolved = OkfDiscovery.Resolve(Path.Combine(this.root, "project", "okf"), Environment(this.root));

        Assert.Equal(["alpha", "beta"], resolved.Bundles.Select(bundle => bundle.Name));
    }

    [Fact]
    public void AnExplicitBundleInsideAVaultStillPicksUpTheProjectConfig()
    {
        var resolved = OkfDiscovery.Resolve(
            Path.Combine(this.root, "project", "okf", "bundles", "alpha"),
            Environment(this.root));

        Assert.Equal("alpha", Assert.Single(resolved.Bundles).Name);
        Assert.Equal(Path.Combine(this.root, "project", "okf", "okf.json"), resolved.ProjectConfigPath);
    }

    [Fact]
    public void AForeignDirectoryIsABundleRootWithNoProjectConfig()
    {
        var resolved = OkfDiscovery.Resolve(Path.Combine(this.root, "foreign-bundle"), Environment(this.root));

        Assert.Equal("foreign-bundle", Assert.Single(resolved.Bundles).Name);
        Assert.Null(resolved.VaultRoot);
        Assert.Null(resolved.ProjectConfigPath);
    }

    [Fact]
    public void ARelativePathResolvesAgainstTheWorkingDirectory()
    {
        var environment = Environment(Path.Combine(this.root, "project"));
        var resolved = OkfDiscovery.Resolve(Path.Combine("okf", "bundles", "beta"), environment);

        Assert.Equal("beta", Assert.Single(resolved.Bundles).Name);
    }

    [Fact]
    public void ThePersonalVaultIsTheFallback()
    {
        var personal = Path.Combine(this.root, "personal");
        Directory.CreateDirectory(Path.Combine(personal, "bundles", "notes"));

        var environment = new OkfEnvironment(
            Path.Combine(this.root, "foreign-bundle"),
            [new KeyValuePair<string, string>("HOME", this.root), new(OkfEnvironment.HomeVariable, personal)]);

        var resolved = OkfDiscovery.Resolve(null, environment);

        Assert.Equal(personal, resolved.VaultRoot);
        Assert.Equal("notes", Assert.Single(resolved.Bundles).Name);
    }

    [Fact]
    public void OkfHomeMovesTheVaultAndNothingElse()
    {
        var environment = new OkfEnvironment(
            this.root,
            [new KeyValuePair<string, string>("HOME", this.root), new(OkfEnvironment.HomeVariable, "/somewhere/else")]);

        Assert.Equal("/somewhere/else", environment.PersonalVault);
        Assert.Equal(Path.Combine(this.root, ".config", "okf", "okf.json"), environment.GlobalConfigPath);
    }

    [Fact]
    public void XdgConfigHomeMovesTheGlobalConfig()
    {
        var environment = new OkfEnvironment(
            this.root,
            [new KeyValuePair<string, string>("HOME", this.root), new("XDG_CONFIG_HOME", "/xdg")]);

        Assert.Equal(Path.Combine("/xdg", "okf", "okf.json"), environment.GlobalConfigPath);
    }

    [Fact]
    public void NothingToLintIsAnError()
    {
        var environment = Environment(Path.Combine(this.root, "foreign-bundle"));

        Assert.Throws<OkfDiscoveryException>(() => OkfDiscovery.Resolve(null, environment));
        Assert.Throws<OkfDiscoveryException>(
            () => OkfDiscovery.Resolve(Path.Combine(this.root, "missing"), environment));
    }

    [Fact]
    public void AnEmptyBundlesDirectoryIsAnError()
    {
        var vault = Path.Combine(this.root, "empty-vault");
        Directory.CreateDirectory(Path.Combine(vault, "bundles"));

        Assert.Throws<OkfDiscoveryException>(() => OkfDiscovery.Resolve(vault, Environment(this.root)));
    }

    /// <summary>
    /// Bundle discovery passes over the same names the walk inside a bundle does, and
    /// nothing else: a dot-prefixed directory in <c>bundles/</c> is a bundle (work item
    /// #42), while named tool state is not.
    /// </summary>
    [Fact]
    public void ADotPrefixedBundleIsDiscoveredAndOnlyNamedToolStateIsPassedOver()
    {
        var bundles = Path.Combine(this.root, "project", "okf", "bundles");
        Directory.CreateDirectory(Path.Combine(bundles, ".drafts"));
        Directory.CreateDirectory(Path.Combine(bundles, ".git"));
        Directory.CreateDirectory(Path.Combine(bundles, ".obsidian"));

        var resolved = OkfDiscovery.Resolve(Path.Combine(this.root, "project", "okf"), Environment(this.root));

        Assert.Equal([".drafts", "alpha", "beta"], resolved.Bundles.Select(bundle => bundle.Name));
    }

    /// <summary>Removes the temporary tree.</summary>
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

    private OkfEnvironment Environment(string workingDirectory) =>
        new(workingDirectory, [new KeyValuePair<string, string>("HOME", this.root)]);
}
