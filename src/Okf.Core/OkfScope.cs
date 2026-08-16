namespace Okf.Core;

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
        var index = text is null ? -1 : Names.ToList().IndexOf(text);
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
        var personal = environment.PersonalVault;
        if (!Directory.Exists(Path.Combine(personal, OkfDiscovery.BundlesDirectoryName)))
        {
            throw new OkfDiscoveryException(
                $"The personal vault '{personal}' has no '{OkfDiscovery.BundlesDirectoryName}/' directory. " +
                $"Create one with `okf init --personal`, or point {OkfEnvironment.HomeVariable} at an existing vault.");
        }

        var resolved = OkfDiscovery.Resolve(personal, environment);
        return new OkfScopeResolution(
            new OkfWorkingSet(resolved.Bundles, resolved.VaultRoot, $"personal vault '{personal}' (--scope personal)"),
            []);
    }

    private static OkfScopeResolution Registered(OkfEnvironment environment, OkfRegistry registry)
    {
        var notes = new List<string>();
        var bundles = FromRegistry(environment, registry, notes, new Dictionary<string, OkfBundle>(StringComparer.Ordinal));

        if (bundles.Count == 0)
        {
            throw new OkfDiscoveryException(
                registry.Entries.Count == 0
                    ? $"The registry '{OkfRegistry.PathFor(environment)}' is empty. Add a vault with `okf register [path]`."
                    : $"No registered vault or bundle resolved to any bundles. " +
                      $"Run `okf registry list` to see what is registered, and `okf registry prune` to drop what is gone.");
        }

        return new OkfScopeResolution(
            new OkfWorkingSet(
                bundles,
                VaultOf(bundles),
                Sentence("registered", bundles, OkfRegistry.PathFor(environment))),
            notes);
    }

    private static OkfScopeResolution All(OkfEnvironment environment, OkfRegistry registry)
    {
        var notes = new List<string>();
        var seen = new Dictionary<string, OkfBundle>(StringComparer.Ordinal);
        var bundles = new List<OkfBundle>();
        string? projectVault = null;
        string? projectResolution = null;

        try
        {
            var project = OkfDiscovery.Resolve(null, environment);
            projectVault = project.VaultRoot;
            projectResolution = project.Resolution;
            foreach (var bundle in project.Bundles)
            {
                Add(bundles, seen, bundle);
            }
        }
        catch (OkfDiscoveryException exception)
        {
            // `--scope all` with no project vault is the personal-plus-registered case, not
            // a failure: the registry is the rest of the scope and it may well be enough.
            notes.Add($"no project vault: {exception.Message}");
        }

        bundles.AddRange(FromRegistry(environment, registry, notes, seen));

        if (bundles.Count == 0)
        {
            throw new OkfDiscoveryException(
                "`--scope all` resolved no bundles: no project vault, and nothing usable in the registry " +
                $"('{OkfRegistry.PathFor(environment)}'). Register one with `okf register [path]`.");
        }

        var resolution = projectResolution is null
            ? Sentence("all", bundles, OkfRegistry.PathFor(environment))
            : $"{projectResolution}, plus the registry (--scope all)";

        return new OkfScopeResolution(
            new OkfWorkingSet(bundles, projectVault ?? VaultOf(bundles), resolution),
            notes);
    }

    private static List<OkfBundle> FromRegistry(
        OkfEnvironment environment,
        OkfRegistry registry,
        List<string> notes,
        Dictionary<string, OkfBundle> seen)
    {
        var bundles = new List<OkfBundle>();
        foreach (var entry in registry.Entries)
        {
            if (!entry.Exists)
            {
                notes.Add($"registered {entry.Kind.ToRegistryString()} '{entry.Id}' is missing: '{entry.Path}'");
                continue;
            }

            OkfWorkingSet resolved;
            try
            {
                resolved = OkfDiscovery.Resolve(entry.Path, environment);
            }
            catch (OkfDiscoveryException exception)
            {
                notes.Add($"registered {entry.Kind.ToRegistryString()} '{entry.Id}' contributed nothing: {exception.Message}");
                continue;
            }

            foreach (var bundle in resolved.Bundles)
            {
                Add(bundles, seen, bundle);
            }
        }

        return bundles;
    }

    private static void Add(List<OkfBundle> bundles, Dictionary<string, OkfBundle> seen, OkfBundle bundle)
    {
        // De-duplicated by the path the filesystem ends at, not the path that was typed: a
        // registered vault that is a symlink to the project's own is one bundle, and
        // counting it twice would double every collection statistic AD-26 computes.
        if (seen.TryAdd(RealPath(bundle.Root), bundle))
        {
            bundles.Add(bundle);
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
            if (Directory.ResolveLinkTarget(path, returnFinalTarget: true) is { } target)
            {
                return RealPath(Path.TrimEndingDirectorySeparator(target.FullName));
            }

            if (Path.GetDirectoryName(path) is not { Length: > 0 } parent)
            {
                return path;
            }

            var real = RealPath(parent);
            return string.Equals(real, parent, StringComparison.Ordinal)
                ? path
                : Path.Combine(real, Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A broken or cyclic link is not a reason to fail a search; it simply is not
            // the same directory as anything else.
            return path;
        }
    }

    /// <summary>
    /// The vault a working set can name — set only when every bundle in scope came out of
    /// one vault's <c>bundles/</c>. A multi-root scope has no single project config to
    /// point at, and guessing one would apply a team's committed contract to somebody
    /// else's vault (AD-31).
    /// </summary>
    private static string? VaultOf(IReadOnlyList<OkfBundle> bundles)
    {
        var vaults = bundles.Select(VaultContaining).Distinct(StringComparer.Ordinal).ToList();
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
        var roots = bundles.Select(VaultContaining).Distinct(StringComparer.Ordinal).Count();
        return $"--scope {scope}: {bundles.Count} bundles from {roots} registered " +
            $"{(roots == 1 ? "root" : "roots")} in '{registryPath}'";
    }
}
