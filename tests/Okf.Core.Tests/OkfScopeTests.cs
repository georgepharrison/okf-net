namespace Okf.Core.Tests;

/// <summary>Scope resolution: which vaults a command looks at (PRD CLI-2, CLI-3).</summary>
public class OkfScopeTests : IDisposable
{
    private static readonly DateTimeOffset Registered = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());

    /// <summary>Builds a project vault, a personal vault, a second vault, and a bare bundle.</summary>
    public OkfScopeTests()
    {
        Directory.CreateDirectory(Path.Combine(this.root, "project", "okf", "bundles", "project-bundle"));
        Directory.CreateDirectory(Path.Combine(this.root, "project", "src"));
        Directory.CreateDirectory(Path.Combine(this.root, "home", "okf", "bundles", "personal-bundle"));
        Directory.CreateDirectory(Path.Combine(this.root, "other", "okf", "bundles", "other-bundle"));
        Directory.CreateDirectory(Path.Combine(this.root, "loose-bundle"));
    }

    /// <summary>
    /// The default is unchanged by everything else in this file: it is byte-for-byte the
    /// CLI-1 resolution, so a registry nobody opted into cannot change what a team's CI
    /// searches.
    /// </summary>
    [Fact]
    public void ProjectScopeIsExactlyTodaysDiscovery()
    {
        var environment = Environment(Path.Combine(this.root, "project", "src"));
        var registry = Registry(Path.Combine(this.root, "other"), Path.Combine(this.root, "loose-bundle"));

        var resolved = OkfScope.Resolve(OkfScopeKind.Project, environment, registry).WorkingSet;
        var discovered = OkfDiscovery.Resolve(null, environment);

        Assert.Equal(discovered.Bundles.Select(bundle => bundle.Root), resolved.Bundles.Select(bundle => bundle.Root));
        Assert.Equal(discovered.VaultRoot, resolved.VaultRoot);
        Assert.Equal(discovered.Resolution, resolved.Resolution);
    }

    /// <summary>
    /// The personal vault is resolvable without the registry — it is <c>OKF_HOME</c> or
    /// <c>~/okf</c> and nowhere else — so `--scope personal` works on a machine that has
    /// never run `okf register`. Registering it is still an ordinary entry with no
    /// privileges; this is discovery knowing where home is, not the registry making an
    /// exception.
    /// </summary>
    [Fact]
    public void PersonalScopeNeedsNoRegistryEntry()
    {
        var resolution = OkfScope.Resolve(
            OkfScopeKind.Personal,
            Environment(Path.Combine(this.root, "project", "src")),
            OkfRegistry.Empty());

        Assert.Equal(["personal-bundle"], resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));
        Assert.Equal(Path.Combine(this.root, "home", "okf"), resolution.WorkingSet.VaultRoot);
        Assert.Empty(resolution.Notes);
    }

    [Fact]
    public void PersonalScopeHonoursOkfHome()
    {
        var environment = new OkfEnvironment(
            this.root,
            [
                new KeyValuePair<string, string>("HOME", this.root),
                new KeyValuePair<string, string>(OkfEnvironment.HomeVariable, Path.Combine(this.root, "other", "okf")),
            ]);

        var resolution = OkfScope.Resolve(OkfScopeKind.Personal, environment, OkfRegistry.Empty());

        Assert.Equal(["other-bundle"], resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));
    }

    [Fact]
    public void RegisteredScopeIsEveryEntryAndExcludesTheProjectVaultNobodyRegistered()
    {
        var registry = Registry(Path.Combine(this.root, "other"), Path.Combine(this.root, "loose-bundle"));

        var resolution = OkfScope.Resolve(
            OkfScopeKind.Registered,
            Environment(Path.Combine(this.root, "project", "src")),
            registry);

        Assert.Equal(
            ["loose-bundle", "other-bundle"],
            resolution.WorkingSet.Bundles.Select(bundle => bundle.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("project-bundle", resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));
        Assert.Empty(resolution.Notes);
    }

    /// <summary>
    /// A path that has gone away is reported and skipped, never raised: a laptop whose
    /// checkout moved must still answer a query (PRD CLI-2).
    /// </summary>
    [Fact]
    public void AMissingRegisteredPathIsANoteNotAnError()
    {
        var registry = Registry(Path.Combine(this.root, "other"), Path.Combine(this.root, "loose-bundle"));
        Directory.Delete(Path.Combine(this.root, "loose-bundle"));

        var resolution = OkfScope.Resolve(
            OkfScopeKind.Registered,
            Environment(Path.Combine(this.root, "project", "src")),
            registry);

        Assert.Equal(["other-bundle"], resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));
        var note = Assert.Single(resolution.Notes);
        Assert.Contains("loose-bundle", note, StringComparison.Ordinal);
        Assert.Contains("missing", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRegistryIsAnActionableFailureRatherThanAnEmptyResultSet()
    {
        var exception = Assert.Throws<OkfDiscoveryException>(() => OkfScope.Resolve(
            OkfScopeKind.Registered,
            Environment(Path.Combine(this.root, "project", "src")),
            OkfRegistry.Empty()));

        Assert.Contains("okf register", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllIsTheProjectPlusTheRegistry()
    {
        var registry = Registry(Path.Combine(this.root, "other"), Path.Combine(this.root, "loose-bundle"));

        var resolution = OkfScope.Resolve(
            OkfScopeKind.All,
            Environment(Path.Combine(this.root, "project", "src")),
            registry);

        Assert.Equal(
            ["project-bundle", "loose-bundle", "other-bundle"],
            resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));

        // The project vault still names the config layer a team committed (AD-31).
        Assert.Equal(Path.Combine(this.root, "project", "okf"), resolution.WorkingSet.VaultRoot);
    }

    /// <summary>
    /// The same directory reached twice is one bundle. Counting it twice would double every
    /// collection statistic BM25 computes over the corpus (AD-26).
    /// </summary>
    [Fact]
    public void AVaultRegisteredTwiceUnderTwoNamesContributesOneBundle()
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Path.Combine(this.root, "project"), Registered);
        registry.Register(Path.Combine(this.root, "project", "okf"), Registered);

        var resolution = OkfScope.Resolve(
            OkfScopeKind.All,
            Environment(Path.Combine(this.root, "project", "src")),
            registry);

        Assert.Equal(["project-bundle"], resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));
        Assert.Single(registry.Entries);
    }

    [SkippableFact]
    public void ASymlinkedVaultIsNotASecondCopyOfTheSameBundles()
    {
        var link = Path.Combine(this.root, "mirror");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(this.root, "other", "okf"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SkipException($"This platform will not create directory symlinks: {exception.Message}");
        }

        var registry = OkfRegistry.Empty();
        registry.Register(Path.Combine(this.root, "other"), Registered);
        registry.Register(link, Registered);

        var resolution = OkfScope.Resolve(
            OkfScopeKind.Registered,
            Environment(Path.Combine(this.root, "project", "src")),
            registry);

        Assert.Equal(2, registry.Entries.Count);
        Assert.Equal(["other-bundle"], resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));
    }

    [Fact]
    public void AllWithoutAProjectVaultFallsBackToTheRegistryAndSaysSo()
    {
        var registry = Registry(Path.Combine(this.root, "other"));
        var elsewhere = Directory.CreateDirectory(Path.Combine(this.root, "no-vault", "deep")).FullName;

        var resolution = OkfScope.Resolve(
            OkfScopeKind.All,
            new OkfEnvironment(elsewhere, [new KeyValuePair<string, string>("HOME", Path.Combine(this.root, "no-home"))]),
            registry);

        Assert.Equal(["other-bundle"], resolution.WorkingSet.Bundles.Select(bundle => bundle.Name));
        Assert.Contains(resolution.Notes, note => note.Contains("no project vault", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every scope name a <c>--scope</c> flag and a <c>search.scope</c> setting accept round
    /// trips, so the two spellings can never mean different things.
    /// </summary>
    [Fact]
    public void EveryScopeNameRoundTrips()
    {
        foreach (var name in OkfScopeKindExtensions.Names)
        {
            Assert.True(OkfScopeKindExtensions.TryParse(name, out var parsed));
            Assert.Equal(name, parsed.ToScopeString());
        }

        Assert.False(OkfScopeKindExtensions.TryParse("global", out _));
        Assert.False(OkfScopeKindExtensions.TryParse(null, out _));
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

        GC.SuppressFinalize(this);
    }

    private OkfRegistry Registry(params string[] paths)
    {
        var registry = OkfRegistry.Empty();
        foreach (var path in paths)
        {
            registry.Register(path, Registered);
        }

        return registry;
    }

    private OkfEnvironment Environment(string workingDirectory) =>
        new(workingDirectory, [new KeyValuePair<string, string>("HOME", Path.Combine(this.root, "home"))]);
}
