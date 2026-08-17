namespace Okf.Core.Vault;

/// <summary>
/// Raised when a command cannot work out which bundles to operate on. Callers map it to
/// the CLI's usage/environment exit code (PRD CLI-14, exit 2).
/// </summary>
public class OkfDiscoveryException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OkfDiscoveryException" /> class.</summary>
    public OkfDiscoveryException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfDiscoveryException" /> class.</summary>
    /// <param name="message">The error message.</param>
    public OkfDiscoveryException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfDiscoveryException" /> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public OkfDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// What a command resolved to work on: the bundles, the vault they came from when there
/// is one, and a sentence explaining how they were found for <c>--verbose</c> output
/// (PRD CLI-1).
/// </summary>
public sealed class OkfWorkingSet
{
    /// <summary>Initializes a working set.</summary>
    /// <param name="bundles">The bundles to operate on, in a deterministic order.</param>
    /// <param name="vaultRoot">The vault the bundles came from, when there is one.</param>
    /// <param name="resolution">A sentence explaining how the working set was resolved.</param>
    public OkfWorkingSet(IReadOnlyList<OkfBundle> bundles, string? vaultRoot, string resolution)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentException.ThrowIfNullOrEmpty(resolution);
        Bundles = bundles;
        VaultRoot = vaultRoot;
        Resolution = resolution;
    }

    /// <summary>The bundles to operate on.</summary>
    public IReadOnlyList<OkfBundle> Bundles { get; }

    /// <summary>The vault the bundles came from — <c>&lt;project&gt;/okf</c> or the personal vault.</summary>
    public string? VaultRoot { get; }

    /// <summary>How the working set was resolved, for <c>--verbose</c> output.</summary>
    public string Resolution { get; }

    /// <summary>
    /// The project config file for this working set — <c>okf.json</c> at the vault root —
    /// or <see langword="null" /> when the target is a bundle outside any vault.
    /// </summary>
    public string? ProjectConfigPath =>
        VaultRoot is null ? null : Path.Combine(VaultRoot, OkfDiscovery.ConfigFileName);
}

/// <summary>
/// Vault and bundle discovery (PRD CORE-13, CLI-1). Pure: it reads the filesystem and
/// never writes.
/// </summary>
public static class OkfDiscovery
{
    /// <summary>The vault directory name walked up for, <c>okf</c> (decisions.md §6).</summary>
    public const string VaultDirectoryName = "okf";

    /// <summary>The directory inside a vault that holds bundle roots (decisions.md §2).</summary>
    public const string BundlesDirectoryName = "bundles";

    /// <summary>
    /// The config filename, used both for the global file in <c>~/.config/okf/</c> and for
    /// the project file at the vault root.
    /// </summary>
    public const string ConfigFileName = "okf.json";

    /// <summary>
    /// Resolves the bundles a command should operate on.
    /// <list type="bullet">
    /// <item>An explicit path wins over discovery: a vault (a directory holding
    /// <c>bundles/</c>) or a project root (holding <c>okf/bundles/</c>) expands to every
    /// bundle it contains; anything else is treated as a single bundle root, which is how
    /// foreign bundles are linted.</item>
    /// <item>With no path, the nearest ancestor holding <c>okf/bundles/</c> wins; failing
    /// that, the personal vault (<c>OKF_HOME</c>, else <c>~/okf</c>).</item>
    /// </list>
    /// </summary>
    /// <param name="path">The explicit path, or <see langword="null" /> to discover one.</param>
    /// <param name="environment">The environment to resolve against.</param>
    /// <returns>The resolved working set.</returns>
    /// <exception cref="OkfDiscoveryException">No bundle could be resolved.</exception>
    public static OkfWorkingSet Resolve(string? path, OkfEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return string.IsNullOrEmpty(path)
            ? Discover(environment)
            : FromExplicitPath(path, environment);
    }

    private static OkfWorkingSet FromExplicitPath(string path, OkfEnvironment environment)
    {
        var full = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(environment.CurrentDirectory, path)));

        if (File.Exists(full))
        {
            throw new OkfDiscoveryException($"'{full}' is a file; okf operates on bundle directories.");
        }

        if (!Directory.Exists(full))
        {
            throw new OkfDiscoveryException($"No such directory: '{full}'.");
        }

        if (Directory.Exists(Path.Combine(full, BundlesDirectoryName)))
        {
            return FromVault(full, $"vault '{full}' (explicit path)");
        }

        if (Directory.Exists(Path.Combine(full, VaultDirectoryName, BundlesDirectoryName)))
        {
            var vault = Path.Combine(full, VaultDirectoryName);
            return FromVault(vault, $"vault '{vault}' (explicit project path '{full}')");
        }

        // Any other directory is a bundle root, foreign or not. When it happens to sit in
        // a vault's `bundles/`, the vault is recorded so its project config still applies.
        var owningVault = OwningVault(full);
        return new OkfWorkingSet(
            [new OkfBundle(full)],
            owningVault,
            $"bundle '{full}' (explicit path)");
    }

    private static OkfWorkingSet Discover(OkfEnvironment environment)
    {
        for (var directory = new DirectoryInfo(environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            // A working directory inside the vault itself resolves to that vault, so
            // `okf lint` behaves the same run from the project root or from `okf/bundles/`.
            if (string.Equals(directory.Name, VaultDirectoryName, StringComparison.Ordinal)
                && Directory.Exists(Path.Combine(directory.FullName, BundlesDirectoryName)))
            {
                return FromVault(
                    directory.FullName,
                    $"vault '{directory.FullName}' (found by walking up from '{environment.CurrentDirectory}')");
            }

            var candidate = Path.Combine(directory.FullName, VaultDirectoryName);
            if (Directory.Exists(Path.Combine(candidate, BundlesDirectoryName)))
            {
                return FromVault(
                    candidate,
                    $"vault '{candidate}' (found by walking up from '{environment.CurrentDirectory}')");
            }
        }

        var personal = environment.PersonalVault;
        if (Directory.Exists(Path.Combine(personal, BundlesDirectoryName)))
        {
            return FromVault(personal, $"personal vault '{personal}'");
        }

        throw new OkfDiscoveryException(
            $"No project vault found by walking up from '{environment.CurrentDirectory}' " +
            $"(looking for a '{VaultDirectoryName}/{BundlesDirectoryName}/' directory), " +
            $"and the personal vault '{personal}' has no '{BundlesDirectoryName}/' directory. " +
            "Pass a bundle path explicitly.");
    }

    private static OkfWorkingSet FromVault(string vault, string resolution)
    {
        var bundlesDirectory = Path.Combine(vault, BundlesDirectoryName);
        // The same list the walk inside a bundle uses, for the same reason: a leading dot
        // hides nothing, and only named tool state is passed over.
        var bundles = Directory.EnumerateDirectories(bundlesDirectory)
            .Where(directory => !OkfBundle.IgnoredMetadataNames.Contains(Path.GetFileName(directory)))
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .Select(directory => new OkfBundle(directory))
            .ToList();

        if (bundles.Count == 0)
        {
            throw new OkfDiscoveryException($"No bundles found in '{bundlesDirectory}'.");
        }

        return new OkfWorkingSet(bundles, vault, resolution);
    }

    private static string? OwningVault(string bundleRoot)
    {
        var bundlesDirectory = Directory.GetParent(bundleRoot);
        if (bundlesDirectory is null
            || !string.Equals(bundlesDirectory.Name, BundlesDirectoryName, StringComparison.Ordinal))
        {
            return null;
        }

        return bundlesDirectory.Parent?.FullName;
    }
}
