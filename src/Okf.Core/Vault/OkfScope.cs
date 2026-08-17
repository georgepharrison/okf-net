namespace Okf.Core.Vault;

/// <summary>
/// Which vaults a command looks at (PRD CLI-3). The default is
/// <see cref="Project" /> and stays there: a query must return the same results on every
/// machine and in CI, which is only true of the vault the repository carries.
/// </summary>
public enum OkfScopeKind
{
    /// <summary>
    /// The project vault found by walking up, falling back to the personal vault when
    /// there is none — the CLI-1 resolution every other command performs.
    /// </summary>
    Project,

    /// <summary>The personal vault: <c>OKF_HOME</c>, else <c>~/okf</c>.</summary>
    Personal,

    /// <summary>Every registry entry whose path still exists.</summary>
    Registered,

    /// <summary>The project scope plus every registry entry, de-duplicated.</summary>
    All,
}

/// <summary>Serialized spellings of <see cref="OkfScopeKind" />.</summary>
public static class OkfScopeKindExtensions
{
    /// <summary>Every spelling a <c>--scope</c> flag or a <c>search.scope</c> setting accepts.</summary>
    public static IReadOnlyList<string> Names { get; } = ["project", "personal", "registered", "all"];

    /// <summary>Renders a scope as it is written in configuration and on the command line.</summary>
    /// <param name="scope">The scope.</param>
    /// <returns>Its lowercase name.</returns>
    public static string ToScopeString(this OkfScopeKind scope) => Names[(int)scope];

    /// <summary>Parses a scope name.</summary>
    /// <param name="text">The value as written.</param>
    /// <param name="scope">The parsed scope.</param>
    /// <returns><see langword="true" /> when the value names a scope.</returns>
    public static bool TryParse(string? text, out OkfScopeKind scope)
    {
        int index = text is null ? -1 : Names.ToList().IndexOf(text);
        scope = index < 0 ? OkfScopeKind.Project : (OkfScopeKind)index;
        return index >= 0;
    }
}

/// <summary>
/// What a scope resolved to: the working set, plus the notes a caller reports once on
/// stderr — a registered path that has gone missing is worth saying and is never an error
/// (PRD CLI-2, CLI-3).
/// </summary>
/// <param name="WorkingSet">The bundles in scope.</param>
/// <param name="Notes">Human-readable notes about entries that did not contribute.</param>
public sealed record OkfScopeResolution(OkfWorkingSet WorkingSet, IReadOnlyList<string> Notes);

/// <summary>
/// Scope resolution: the one code path the CLI and the MCP server both take, so
/// <c>okf search --scope all</c> and an <c>okf mcp --scope all</c> server look at exactly
/// the same bundles (PRD MCP-3).
/// </summary>
/// <remarks>
/// The personal vault is resolvable without the registry — it is <c>OKF_HOME</c> or
/// <c>~/okf</c> and nowhere else — so <c>--scope personal</c> works whether or not it has
/// been registered. That is not a special case in the <em>registry</em>: registering the
/// personal vault produces an ordinary entry with no privileges (decisions.md §6). It is a
/// special case in <em>discovery</em>, which has always known where the personal vault is.
/// </remarks>
public static class OkfScope
{
    /// <summary>Resolves a scope's working set.</summary>
    /// <param name="scope">The scope to resolve.</param>
    /// <param name="environment">The environment to resolve against.</param>
    /// <param name="registry">The registry, already read; <see langword="null" /> reads it from the environment.</param>
    /// <returns>The working set and any notes.</returns>
    /// <exception cref="OkfDiscoveryException">The scope resolved to no bundles at all.</exception>
    public static OkfScopeResolution Resolve(
        OkfScopeKind scope,
        OkfEnvironment environment,
        OkfRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return scope switch
        {
            OkfScopeKind.Project => new OkfScopeResolution(OkfDiscovery.Resolve(null, environment), []),
            OkfScopeKind.Personal => Personal(environment),
            OkfScopeKind.Registered => Registered(environment, registry ?? OkfRegistry.Load(environment)),
            _ => All(environment, registry ?? OkfRegistry.Load(environment)),
        };
    }

    private static OkfScopeResolution Personal(OkfEnvironment environment)
    {
        string personal = environment.PersonalVault;
        if (!Directory.Exists(Path.Combine(personal, OkfDiscovery.BundlesDirectoryName)))
        {
            throw new OkfDiscoveryException(
                $"The personal vault '{personal}' has no '{OkfDiscovery.BundlesDirectoryName}/' directory. " +
                $"Create one with `okf init --personal`, or point {OkfEnvironment.HomeVariable} at an existing vault.");
        }

        OkfWorkingSet resolved = OkfDiscovery.Resolve(personal, environment);
        return new OkfScopeResolution(
            new OkfWorkingSet(resolved.Bundles, resolved.VaultRoot, $"personal vault '{personal}' (--scope personal)"),
            []);
    }

    private static OkfScopeResolution Registered(OkfEnvironment environment, OkfRegistry registry)
    {
        RegistryResolution registryResolution = EmptyRegistryResolution(environment);
        AddRegistryBundles(registry, registryResolution.Accumulator, environment);
        if (registryResolution.Accumulator.Bundles.Count == 0)
        {
            throw NoRegisteredBundles(environment, registry);
        }

        return new OkfScopeResolution(
            new OkfWorkingSet(
                registryResolution.Accumulator.Bundles,
                VaultOf(registryResolution.Accumulator.Bundles),
                Sentence("registered", registryResolution.Accumulator.Bundles, registryResolution.RegistryPath)),
            registryResolution.Accumulator.Notes);
    }

    private static OkfScopeResolution All(OkfEnvironment environment, OkfRegistry registry)
    {
        RegistryResolution registryResolution = EmptyRegistryResolution(environment);
        ProjectScope project = TryAddProjectScope(environment, registryResolution.Accumulator);
        AddRegistryBundles(registry, registryResolution.Accumulator, environment);
        if (registryResolution.Accumulator.Bundles.Count == 0)
        {
            throw NoAllScopeBundles(environment);
        }

        return new OkfScopeResolution(
            new OkfWorkingSet(
                registryResolution.Accumulator.Bundles,
                project.VaultRoot ?? VaultOf(registryResolution.Accumulator.Bundles),
                AllResolution(project.Resolution, registryResolution.Accumulator.Bundles, registryResolution.RegistryPath)),
            registryResolution.Accumulator.Notes);
    }

    private static RegistryResolution EmptyRegistryResolution(OkfEnvironment environment) =>
        new(
            OkfRegistry.PathFor(environment),
            new BundleAccumulator([], [], new Dictionary<string, OkfBundle>(StringComparer.Ordinal)));

    private static OkfDiscoveryException NoRegisteredBundles(OkfEnvironment environment, OkfRegistry registry) =>
        new(
            registry.Entries.Count == 0
                ? $"The registry '{OkfRegistry.PathFor(environment)}' is empty. Add a vault with `okf register [path]`."
                : "No registered vault or bundle resolved to any bundles. "
                  + "Run `okf registry list` to see what is registered, and `okf registry prune` to drop what is gone.");

    private static ProjectScope TryAddProjectScope(OkfEnvironment environment, BundleAccumulator accumulator)
    {
        try
        {
            OkfWorkingSet project = OkfDiscovery.Resolve(null, environment);
            AddBundles(project.Bundles, accumulator);
            return new ProjectScope(project.VaultRoot, project.Resolution);
        }
        catch (OkfDiscoveryException exception)
        {
            // `--scope all` with no project vault is the personal-plus-registered case, not
            // a failure: the registry is the rest of the scope and it may well be enough.
            accumulator.Notes.Add($"no project vault: {exception.Message}");
            return new ProjectScope(null, null);
        }
    }

    private static OkfDiscoveryException NoAllScopeBundles(OkfEnvironment environment) =>
        new(
            "`--scope all` resolved no bundles: no project vault, and nothing usable in the registry "
            + $"('{OkfRegistry.PathFor(environment)}'). Register one with `okf register [path]`.");

    private static string AllResolution(string? projectResolution, IReadOnlyList<OkfBundle> bundles, string registryPath) =>
        projectResolution is null
            ? Sentence("all", bundles, registryPath)
            : $"{projectResolution}, plus the registry (--scope all)";

    private static void AddRegistryBundles(OkfRegistry registry, BundleAccumulator accumulator, OkfEnvironment environment)
    {
        foreach (OkfRegistryEntry entry in registry.Entries)
        {
            AddRegistryEntry(entry, accumulator, environment);
        }
    }

    private static void AddRegistryEntry(OkfRegistryEntry entry, BundleAccumulator accumulator, OkfEnvironment environment)
    {
        if (!entry.Exists)
        {
            accumulator.Notes.Add($"registered {entry.Kind.ToRegistryString()} '{entry.Id}' is missing: '{entry.Path}'");
            return;
        }

        try
        {
            AddBundles(OkfDiscovery.Resolve(entry.Path, environment).Bundles, accumulator);
        }
        catch (OkfDiscoveryException exception)
        {
            accumulator.Notes.Add(
                $"registered {entry.Kind.ToRegistryString()} '{entry.Id}' contributed nothing: {exception.Message}");
        }
    }

    private static void AddBundles(IEnumerable<OkfBundle> bundles, BundleAccumulator accumulator)
    {
        foreach (OkfBundle bundle in bundles)
        {
            AddBundle(bundle, accumulator);
        }
    }

    private static void AddBundle(OkfBundle bundle, BundleAccumulator accumulator)
    {
        // De-duplicated by the path the filesystem ends at, not the path that was typed: a
        // registered vault that is a symlink to the project's own is one bundle, and
        // counting it twice would double every collection statistic AD-26 computes.
        if (accumulator.Seen.TryAdd(RealPath(bundle.Root), bundle))
        {
            accumulator.Bundles.Add(bundle);
        }
    }

    /// <summary>
    /// The path a directory really is, with symlinks followed — including links on the way
    /// down, because a registered vault is usually a link to a directory whose own
    /// components are not links.
    /// </summary>
    /// <param name="path">An absolute, normalized path.</param>
    /// <returns>The final target's path, or the input when nothing on it is a link.</returns>
    private static string RealPath(string path)
    {
        try
        {
            if (ResolvedLinkTarget(path) is { } target)
            {
                return RealPath(target);
            }

            return Path.GetDirectoryName(path) is { Length: > 0 } parent
                ? RecombineWithResolvedParent(path, parent)
                : path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A broken or cyclic link is not a reason to fail a search; it simply is not
            // the same directory as anything else.
            return path;
        }
    }

    private static string? ResolvedLinkTarget(string path) =>
        Directory.ResolveLinkTarget(path, returnFinalTarget: true) is { } target
            ? Path.TrimEndingDirectorySeparator(target.FullName)
            : null;

    private static string RecombineWithResolvedParent(string path, string parent)
    {
        string real = RealPath(parent);
        return string.Equals(real, parent, StringComparison.Ordinal)
            ? path
            : Path.Combine(real, Path.GetFileName(path));
    }

    /// <summary>
    /// The vault a working set can name — set only when every bundle in scope came out of
    /// one vault's <c>bundles/</c>. A multi-root scope has no single project config to
    /// point at, and guessing one would apply a team's committed contract to somebody
    /// else's vault (AD-31).
    /// </summary>
    private static string? VaultOf(IReadOnlyList<OkfBundle> bundles)
    {
        List<string> vaults = bundles.Select(VaultContaining).Distinct(StringComparer.Ordinal).ToList();
        return vaults.Count == 1 ? vaults[0] : null;
    }

    /// <summary>The vault a bundle sits in, or its own root when it is a bare bundle.</summary>
    private static string VaultContaining(OkfBundle bundle) =>
        Path.GetDirectoryName(bundle.Root) is { } parent
        && string.Equals(Path.GetFileName(parent), OkfDiscovery.BundlesDirectoryName, StringComparison.Ordinal)
        && Path.GetDirectoryName(parent) is { Length: > 0 } vault
            ? vault
            : bundle.Root;

    private static string Sentence(string scope, IReadOnlyList<OkfBundle> bundles, string registryPath)
    {
        int roots = bundles.Select(VaultContaining).Distinct(StringComparer.Ordinal).Count();
        return $"--scope {scope}: {bundles.Count} {(bundles.Count == 1 ? "bundle" : "bundles")} " +
            $"from {roots} registered {(roots == 1 ? "root" : "roots")} in '{registryPath}'";
    }

    private sealed record RegistryResolution(string RegistryPath, BundleAccumulator Accumulator);

    private sealed record ProjectScope(string? VaultRoot, string? Resolution);

    private sealed record BundleAccumulator(
        List<OkfBundle> Bundles,
        List<string> Notes,
        Dictionary<string, OkfBundle> Seen);
}
