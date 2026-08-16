namespace Okf.Core.Tests;

/// <summary>Scope resolution: which vaults a command looks at (PRD CLI-2, CLI-3).</summary>
public sealed class OkfScopeTests : IDisposable
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

        // Two roots, so there is no single project config to apply (AD-31), and the
        // sentence says so in the plural.
        Assert.Null(resolution.WorkingSet.VaultRoot);
        Assert.Equal(
            $"--scope registered: 2 bundles from 2 registered roots in '{OkfRegistry.PathFor(Environment(this.root))}'",
            resolution.WorkingSet.Resolution);
    }

    /// <summary>
    /// CLI-1 has every command able to say what it looked at, so the sentence is a contract:
    /// it counts bundles and roots, and pluralizes <c>root</c>. It does NOT pluralize
    /// <c>bundles</c> — "1 bundles" is what the code emits today, and the literal below says
    /// so rather than describing a nicer sentence nobody wrote. One root is also the case
    /// where the working set can name a vault, which is what makes a registered vault's own
    /// <c>okf.json</c> apply.
    /// </summary>
    [Fact]
    public void OneRegisteredRootIsNamedInTheSingularAndBecomesTheVault()
    {
        var registry = Registry(Path.Combine(this.root, "other"));

        var resolution = OkfScope.Resolve(
            OkfScopeKind.Registered,
            Environment(Path.Combine(this.root, "project", "src")),
            registry);

        Assert.Equal(Path.Combine(this.root, "other", "okf"), resolution.WorkingSet.VaultRoot);
        Assert.Equal(
            $"--scope registered: 1 bundles from 1 registered root in '{OkfRegistry.PathFor(Environment(this.root))}'",
            resolution.WorkingSet.Resolution);
    }

    /// <summary>
    /// A registry with entries in it that resolved to nothing is a different problem from an
    /// empty one, and the two refusals name the different commands that fix them.
    /// </summary>
    [Fact]
    public void AnEmptyRegistryAndAStaleOneAreDifferentRefusals()
    {
        var registry = Registry(Path.Combine(this.root, "loose-bundle"));
        Directory.Delete(Path.Combine(this.root, "loose-bundle"));
        var environment = Environment(Path.Combine(this.root, "project", "src"));

        var stale = Assert.Throws<OkfDiscoveryException>(
            () => OkfScope.Resolve(OkfScopeKind.Registered, environment, registry));
        var empty = Assert.Throws<OkfDiscoveryException>(
            () => OkfScope.Resolve(OkfScopeKind.Registered, environment, OkfRegistry.Empty()));

        Assert.Contains("okf registry prune", stale.Message, StringComparison.Ordinal);
        Assert.Contains("is empty", empty.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("is empty", stale.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("okf registry prune", empty.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--scope personal</c> names the way out rather than reporting a missing directory:
    /// AD-50 puts the personal vault where the home variable points, so the two fixes are
    /// creating it or pointing somewhere else.
    /// </summary>
    [Fact]
    public void APersonalVaultWithNoBundlesDirectoryIsRefusedWithBothWaysOut()
    {
        var environment = new OkfEnvironment(
            Path.Combine(this.root, "project"),
            [new KeyValuePair<string, string>("HOME", Path.Combine(this.root, "no-home"))]);

        var exception = Assert.Throws<OkfDiscoveryException>(
            () => OkfScope.Resolve(OkfScopeKind.Personal, environment, OkfRegistry.Empty()));

        Assert.Contains("okf init --personal", exception.Message, StringComparison.Ordinal);
        Assert.Contains(OkfEnvironment.HomeVariable, exception.Message, StringComparison.Ordinal);
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

        // The project vault still names the config layer a team committed (AD-31), and the
        // sentence is the project's own with the registry appended rather than the
        // registry-only count.
        Assert.Equal(Path.Combine(this.root, "project", "okf"), resolution.WorkingSet.VaultRoot);
        Assert.EndsWith(
            ", plus the registry (--scope all)",
            resolution.WorkingSet.Resolution,
            StringComparison.Ordinal);
        Assert.Contains(
            Path.Combine(this.root, "project", "okf"),
            resolution.WorkingSet.Resolution,
            StringComparison.Ordinal);
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

        // With no project to take one from, the vault is the registry's single root and the
        // sentence is the registry-only form.
        Assert.Equal(Path.Combine(this.root, "other", "okf"), resolution.WorkingSet.VaultRoot);
        Assert.StartsWith("--scope all: ", resolution.WorkingSet.Resolution, StringComparison.Ordinal);
    }

    /// <summary>
    /// `--scope all` with neither a project vault nor a usable registry is the one case
    /// where it has nothing to search, and it names the command that fixes that.
    /// </summary>
    [Fact]
    public void AllWithNothingAnywhereIsRefusedRatherThanEmpty()
    {
        var elsewhere = Directory.CreateDirectory(Path.Combine(this.root, "no-vault", "deep")).FullName;

        var exception = Assert.Throws<OkfDiscoveryException>(() => OkfScope.Resolve(
            OkfScopeKind.All,
            new OkfEnvironment(elsewhere, [new KeyValuePair<string, string>("HOME", Path.Combine(this.root, "no-home"))]),
            OkfRegistry.Empty()));

        Assert.Contains("--scope all", exception.Message, StringComparison.Ordinal);
        Assert.Contains("okf register [path]", exception.Message, StringComparison.Ordinal);
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

        // A name that is not one leaves the caller holding the default rather than a cast of
        // -1, which is no scope at all.
        Assert.False(OkfScopeKindExtensions.TryParse("global", out var unknown));
        Assert.Equal(OkfScopeKind.Project, unknown);
        Assert.False(OkfScopeKindExtensions.TryParse(null, out var absent));
        Assert.Equal(OkfScopeKind.Project, absent);
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
