namespace Okf.Core.Upgrade;

/// <summary>
/// A refusal by <c>okf upgrade</c>. Every one of them happens before a byte is written
/// outside the staging file, which is what makes exit 2 mean "nothing changed".
/// </summary>
public sealed class OkfUpgradeException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">What was refused, and why.</param>
    public OkfUpgradeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception.</summary>
    /// <param name="message">What was refused, and why.</param>
    /// <param name="innerException">The failure underneath.</param>
    public OkfUpgradeException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Which release channel <c>okf upgrade</c> was pointed at.</summary>
public enum OkfUpgradeChannel
{
    /// <summary>The default: releases from <c>main</c>, served under <c>stable/</c>.</summary>
    Stable = 0,

    /// <summary>Release candidates from <c>dev</c>, served under <c>dev/</c>.</summary>
    Rc,
}

/// <summary>What <c>okf upgrade</c> was asked to do, before anything is resolved.</summary>
public sealed class OkfUpgradeOptions
{
    /// <summary>The base URL to read the release from — <c>OKF_INSTALL_URL</c>, else the public Pages site.</summary>
    public string BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>
    /// Whether a validated absolute HTTPS <c>downloadUrl</c> may be used for assets.
    /// Explicit mirror and fixture installs leave this false and use <c>path</c>.
    /// </summary>
    public bool UsePublicDownloadUrl { get; init; }

    /// <summary>The version to pin to, without a leading <c>v</c>; unset means the newest.</summary>
    public string? Version { get; init; }

    /// <summary>The channel asked for.</summary>
    public OkfUpgradeChannel Channel { get; init; }

    /// <summary>The running binary's version, which the manifest's is compared against.</summary>
    public string CurrentVersion { get; init; } = "0.0.0";

    /// <summary>
    /// The file to replace: <c>Environment.ProcessPath</c> in production, a temporary file
    /// in a test. Nothing outside this file's directory is ever touched.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>The <c>User-Agent</c> the artifact host sees.</summary>
    public string UserAgent { get; init; } = "okf";

    /// <summary>
    /// The public Pages site, shared verbatim with <c>install.sh</c> and
    /// <c>install.ps1</c>.
    /// </summary>
    public const string DefaultBaseUrl = "https://georgepharrison.github.io/okf-net";

    /// <summary>The environment variable that moves the base URL, as both installers read it.</summary>
    public const string BaseUrlVariable = "OKF_INSTALL_URL";
}

/// <summary>
/// What <c>okf upgrade</c> resolved: which release, which asset, where it comes from and
/// what it must hash to. Producing one downloads nothing.
/// </summary>
public sealed class OkfUpgradePlan
{
    /// <summary>Initializes a plan.</summary>
    /// <param name="currentVersion">The running binary's version.</param>
    /// <param name="availableVersion">The version the manifest names.</param>
    /// <param name="asset">The asset selected for this machine.</param>
    /// <param name="manifestUri">Where the manifest was read from.</param>
    /// <param name="assetUri">Where the asset would be downloaded from.</param>
    /// <param name="targetPath">The file that would be replaced.</param>
    public OkfUpgradePlan(
        string currentVersion,
        string availableVersion,
        OkfUpgradeAsset asset,
        Uri manifestUri,
        Uri assetUri,
        string targetPath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(manifestUri);
        ArgumentNullException.ThrowIfNull(assetUri);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        CurrentVersion = currentVersion;
        AvailableVersion = availableVersion;
        Asset = asset;
        ManifestUri = manifestUri;
        AssetUri = assetUri;
        TargetPath = targetPath;
    }

    /// <summary>The running binary's version.</summary>
    public string CurrentVersion { get; }

    /// <summary>The version the manifest names.</summary>
    public string AvailableVersion { get; }

    /// <summary>The asset selected for this machine's platform.</summary>
    public OkfUpgradeAsset Asset { get; }

    /// <summary>Where the manifest was read from.</summary>
    public Uri ManifestUri { get; }

    /// <summary>Where the asset would be downloaded from.</summary>
    public Uri AssetUri { get; }

    /// <summary>The file that would be replaced.</summary>
    public string TargetPath { get; }

    /// <summary>Whether the running binary is already the one the manifest names.</summary>
    public bool IsUpToDate => !OkfUpgradeVersion.IsUpgradeAvailable(CurrentVersion, AvailableVersion);

    /// <summary>Whether the running binary is an unstamped local build (AD-41).</summary>
    public bool IsDevelopmentBuild => OkfUpgradeVersion.IsDevelopmentBuild(CurrentVersion);
}

/// <summary>One filesystem move in the swap that puts a verified binary in place.</summary>
public enum OkfUpgradeStepKind
{
    /// <summary>
    /// Move the running binary aside. Windows only, where a loaded image cannot be
    /// overwritten but CAN be renamed.
    /// </summary>
    Retire = 0,

    /// <summary>Rename the verified staging file onto the target.</summary>
    Install,
}

/// <summary>A single move in the swap.</summary>
/// <param name="Kind">Which move it is.</param>
/// <param name="Source">The path moved from.</param>
/// <param name="Destination">The path moved to.</param>
public readonly record struct OkfUpgradeStep(OkfUpgradeStepKind Kind, string Source, string Destination);

/// <summary>What replacing the binary did.</summary>
/// <param name="TargetPath">The file now holding the new bytes.</param>
/// <param name="Version">The version installed.</param>
/// <param name="RetiredPath">
/// Where the old binary was moved to, or <see langword="null" /> when it was replaced in
/// place. Non-null only on Windows, and the next <c>okf upgrade</c> deletes it.
/// </param>
public readonly record struct OkfUpgradeResult(string TargetPath, string Version, string? RetiredPath);
