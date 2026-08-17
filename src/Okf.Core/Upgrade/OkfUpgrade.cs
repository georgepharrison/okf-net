using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Okf.Core.Upgrade;

/// <summary>
/// <c>okf upgrade</c> — resolve the newest release, verify it, and rename it over the
/// running binary (work item #23).
/// </summary>
/// <remarks>
/// <para>
/// <b>This file is the toolset's only network path, and that is deliberate.</b> AD-7 says
/// no command makes a network call, because a gate that fails when a network does is not a
/// gate. A self-updating binary cannot honour that and still be a self-updating binary, so
/// the exception is confined the way AD-9 confines a third-party surface: one file, one
/// injectable seam, and no other verb reaches it. Everything above this file receives an
/// <see cref="Fetch" /> delegate, so every test in the suite runs against a fake or an
/// in-process listener and none of them touches the artifact host.
/// </para>
/// <para>
/// The resolve → verify → stage → rename sequence, the platform table and the
/// https-stays-https redirect rule are all <c>install.sh</c>'s, on purpose: the installer
/// is the reference implementation (AD-42), and a second answer to "which asset does this
/// machine get" is how the two start disagreeing.
/// </para>
/// </remarks>
public static class OkfUpgrade
{
    /// <summary>
    /// Opens one URL for reading. The seam that keeps the network out of every test: the
    /// production implementation is <see cref="HttpFetch" />, and nothing above this file
    /// constructs an <see cref="HttpClient" />.
    /// </summary>
    /// <param name="uri">The URL to read.</param>
    /// <returns>The response body.</returns>
    public delegate Stream Fetch(Uri uri);

    /// <summary>The asset name for a 64-bit Linux machine.</summary>
    public const string LinuxAsset = "okf-linux-x64";

    /// <summary>The asset name for an Apple Silicon Mac.</summary>
    public const string MacAsset = "okf-osx-arm64";

    /// <summary>The asset name for a 64-bit Windows machine.</summary>
    public const string WindowsAsset = "okf-win-x64.exe";

    /// <summary>The manifest every release publishes, relative to the base URL.</summary>
    public const string ManifestFileName = "latest.json";

    /// <summary>How long any single request may take.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many redirects are followed before giving up. `curl --location` defaults to 50;
    /// five is enough for a host that moves and small enough that a redirect loop is
    /// reported rather than waited out.
    /// </summary>
    private const int MaximumRedirects = 5;

    /// <summary>
    /// The most an asset may be before the download is abandoned. The largest asset any
    /// release has carried is the 15 MB osx-arm64 build, so this is an order of magnitude of
    /// headroom.
    /// </summary>
    /// <remarks>
    /// A bound is needed at all because the staging file is written into the directory the
    /// user's binary lives in, and a plain <see cref="Stream.CopyTo(Stream)" /> writes
    /// whatever the host sends: a host answering with an endless body fills that filesystem
    /// long before a single byte is verified — measured at 7.4 GB in five seconds over
    /// loopback. The manifest's own <c>size</c> cannot be the bound, because whoever writes
    /// a lying digest writes a lying size in the same file.
    /// </remarks>
    public const long MaximumAssetBytes = 200L * 1024 * 1024;

    /// <summary>
    /// The most a release manifest may be. It is a few kilobytes of JSON; the bound exists
    /// so that reading one into a string cannot be turned into an out-of-memory by a host
    /// that answers <c>latest.json</c> with an endless body.
    /// </summary>
    public const int MaximumManifestBytes = 1024 * 1024;

    private static readonly Lazy<HttpClient> SharedClient = new(CreateClient);

    /// <summary>
    /// Resolves what an upgrade would install, without downloading it.
    /// </summary>
    /// <param name="options">What was asked for.</param>
    /// <param name="fetch">How to read a URL; the real HTTP client when omitted.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="OkfUpgradeException">
    /// The base URL is not usable, the manifest could not be read, the release does not
    /// describe the version that was asked for, or it carries no asset for this platform.
    /// </exception>
    public static OkfUpgradePlan Resolve(OkfUpgradeOptions options, Fetch? fetch = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Uri baseUri = BaseUri(options.BaseUrl);
        Uri manifestUri = ManifestUri(baseUri, options.Version);
        OkfUpgradeManifest manifest = ReadManifest(manifestUri, fetch ?? HttpFetch(options.UserAgent));

        // The installer's check, for the installer's reason: a host that answers every path
        // with the newest release would otherwise silently ignore a pinned version.
        if (options.Version is { Length: > 0 } pinned
            && !string.Equals(manifest.Version, pinned, StringComparison.Ordinal))
        {
            throw new OkfUpgradeException(
                $"asked for {pinned} but {manifestUri} describes {manifest.Version}.");
        }

        string assetName = AssetName();
        OkfUpgradeAsset asset = manifest.Find(assetName)
            ?? throw new OkfUpgradeException(
                $"release {manifest.Version} lists no {assetName} asset with a path and a sha256.");

        string target = options.ExecutablePath is { Length: > 0 } path
            ? Path.GetFullPath(path)
            : Environment.ProcessPath
                ?? throw new OkfUpgradeException(
                    "could not determine which file is running, so there is nothing to replace.");

        return new OkfUpgradePlan(
            options.CurrentVersion,
            manifest.Version,
            asset,
            manifestUri,
            AssetUri(baseUri, asset),
            target);
    }

    /// <summary>
    /// Downloads the planned asset, verifies it against the manifest's digest, and renames
    /// it over the running binary.
    /// </summary>
    /// <param name="plan">The plan <see cref="Resolve" /> produced.</param>
    /// <param name="fetch">How to read a URL; the real HTTP client when omitted.</param>
    /// <param name="userAgent">The <c>User-Agent</c> for the default fetch.</param>
    /// <returns>What was installed, and what was moved aside to install it.</returns>
    /// <exception cref="OkfUpgradeException">
    /// The target is not a file named <c>okf</c>, its directory cannot be written, the
    /// download failed, or the bytes do not match the manifest. In every case the target
    /// is left exactly as it was and no staging file survives.
    /// </exception>
    public static OkfUpgradeResult Apply(OkfUpgradePlan plan, Fetch? fetch = null, string userAgent = "okf")
    {
        ArgumentNullException.ThrowIfNull(plan);

        string target = plan.TargetPath;
        string? directory = Path.GetDirectoryName(target);
        if (directory is not { Length: > 0 })
        {
            throw new OkfUpgradeException($"'{target}' has no directory to stage a download in.");
        }

        // A binary that replaces itself must be sure that "itself" is what it is replacing.
        // Launched through the muxer — `dotnet okf.dll`, which is how a framework-dependent
        // build runs — `Environment.ProcessPath` is `dotnet`, and renaming a verified okf
        // over it would break the machine's .NET rather than upgrade okf. Confirmed
        // empirically, and it caught exactly that during this work item.
        string name = Path.GetFileName(target);
        if (!string.Equals(name, "okf", StringComparison.Ordinal)
            && !string.Equals(name, "okf.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new OkfUpgradeException(
                $"the running executable is '{name}', not 'okf' — refusing to replace it.\n" +
                "    This is what `dotnet okf.dll` looks like: the process is the .NET host, not\n" +
                "    a published okf. Upgrade an installed binary, or reinstall with install.sh.");
        }

        string staging = Path.Combine(directory, $".okf.upgrade.{Guid.NewGuid():N}");
        try
        {
            Download(plan.AssetUri, staging, fetch ?? HttpFetch(userAgent));

            // Verified BEFORE anything is renamed, and both digests are printed on a
            // mismatch: the two hashes are what tells a truncated download apart from the
            // wrong file (install.sh says the same, for the same reason).
            string digest = OkfCaptureManifest.Sha256Of(staging);
            if (!string.Equals(digest, plan.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new OkfUpgradeException(
                    $"sha256 mismatch for {plan.Asset.Name}\n" +
                    $"    manifest:   {plan.Asset.Sha256}\n" +
                    $"    downloaded: {digest}\n" +
                    $"    Refusing to install. {target} was not touched.");
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    staging,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            string? retired = null;
            foreach (OkfUpgradeStep step in PlanSwap(target, staging, OperatingSystem.IsWindows()))
            {
                Move(step);
                if (step.Kind == OkfUpgradeStepKind.Retire)
                {
                    retired = step.Destination;
                }
            }

            return new OkfUpgradeResult(target, plan.AvailableVersion, retired);
        }
        catch (Exception exception) when (exception is not OkfUpgradeException)
        {
            throw new OkfUpgradeException(
                $"could not install {plan.Asset.Name} to {target}: {exception.Message}", exception);
        }
        finally
        {
            // The staging file is gone whether the swap happened (it was renamed away) or
            // failed (it is deleted here), so a refused upgrade leaves the directory as it
            // found it.
            TryDelete(staging);
        }
    }

    /// <summary>
    /// The moves that put a verified staging file in place, in order.
    /// </summary>
    /// <param name="target">The file being replaced.</param>
    /// <param name="staging">The verified download, in the same directory.</param>
    /// <param name="windows">Whether the target platform locks a loaded executable.</param>
    /// <returns>The steps to run.</returns>
    /// <remarks>
    /// Two platforms, two different physics, and this is where the difference is stated
    /// once. On Linux and macOS a rename over a running binary succeeds: the open image
    /// keeps the old inode alive until the process exits, so one atomic
    /// <c>File.Move(overwrite: true)</c> leaves the path holding either the old bytes or the
    /// new ones and never a half-written file. Windows locks a loaded image against
    /// overwrite but permits it to be RENAMED, so the running binary is moved to
    /// <c>okf.exe.old</c> first and deleted by the next upgrade.
    /// </remarks>
    public static IReadOnlyList<OkfUpgradeStep> PlanSwap(string target, string staging, bool windows)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentException.ThrowIfNullOrEmpty(staging);

        return windows
            ?
            [
                new OkfUpgradeStep(OkfUpgradeStepKind.Retire, target, RetiredPath(target)),
                new OkfUpgradeStep(OkfUpgradeStepKind.Install, staging, target),
            ]
            : [new OkfUpgradeStep(OkfUpgradeStepKind.Install, staging, target)];
    }

    /// <summary>Where a Windows upgrade moves the running binary to.</summary>
    /// <param name="target">The binary being replaced.</param>
    /// <returns>The retired path.</returns>
    public static string RetiredPath(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        return target + ".old";
    }

    /// <summary>
    /// Deletes the binary a previous Windows upgrade moved aside, if it is still there.
    /// </summary>
    /// <param name="target">The binary being replaced.</param>
    /// <returns><see langword="true" /> when a retired file was deleted.</returns>
    /// <remarks>
    /// Called by <c>okf upgrade</c> and by nothing else. A file the operating system was
    /// still holding open is left where it is rather than reported: it is a stale copy of a
    /// working binary, and failing an upgrade over it would be worse than the megabyte.
    /// </remarks>
    public static bool RemoveRetired(string target) => TryDelete(RetiredPath(target));

    /// <summary>The asset this machine gets.</summary>
    /// <returns>The asset name.</returns>
    /// <exception cref="OkfUpgradeException">No release asset runs on this machine.</exception>
    public static string AssetName() =>
        AssetName(
            OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsLinux() ? OSPlatform.Linux
            : OSPlatform.Create(RuntimeInformation.OSDescription),
            RuntimeInformation.OSArchitecture);

    /// <summary>The asset a given platform gets.</summary>
    /// <param name="platform">The operating system.</param>
    /// <param name="architecture">The machine's architecture, not the process's.</param>
    /// <returns>The asset name.</returns>
    /// <exception cref="OkfUpgradeException">No release asset runs there.</exception>
    /// <remarks>
    /// The table and the refusals are <c>install.sh</c>'s, down to the wording, because a
    /// user who is told two different things by the installer and the upgrader has to work
    /// out which one is lying. One case the installer needs and this does not: Rosetta.
    /// `uname -m` reports the translated process on an Apple Silicon Mac, but
    /// <see cref="RuntimeInformation.OSArchitecture" /> reports the hardware — and no x64
    /// macOS binary ships, so an okf that is running on a Mac at all is an arm64 one.
    /// </remarks>
    public static string AssetName(OSPlatform platform, Architecture architecture)
    {
        if (platform == OSPlatform.Linux)
        {
            return architecture == Architecture.X64
                ? LinuxAsset
                : throw new OkfUpgradeException(
                    $"okf ships a linux-x86_64 binary for Linux; this machine is {Name(architecture)}.\n" +
                    "    There is no such build yet. Build from source instead:\n" +
                    "    https://gitlab.tychostation.dev/ringo/okf-net");
        }

        if (platform == OSPlatform.OSX)
        {
            return architecture == Architecture.Arm64
                ? MacAsset
                : throw new OkfUpgradeException(
                    "okf has no Intel-Mac build. The macOS build that ships is Apple Silicon\n" +
                    "    (osx-arm64), and Rosetta translates the wrong way. Adding osx-x64 is one\n" +
                    "    more line in the publish job — ask for it at\n" +
                    "    https://gitlab.tychostation.dev/ringo/okf-net/-/issues/36");
        }

        if (platform == OSPlatform.Windows)
        {
            return architecture == Architecture.X64
                ? WindowsAsset
                : throw new OkfUpgradeException(
                    $"okf ships a win-x64 binary for Windows; this machine is {Name(architecture)}.\n" +
                    "    There is no such build yet. Build from source instead:\n" +
                    "    https://gitlab.tychostation.dev/ringo/okf-net");
        }

        throw new OkfUpgradeException(
            $"okf ships Linux x86_64, macOS arm64 and Windows x64 binaries; this is {platform}.\n" +
            "    Build from source instead: https://gitlab.tychostation.dev/ringo/okf-net");
    }

    /// <summary>
    /// Normalizes and checks the base URL every other URL is built from.
    /// </summary>
    /// <param name="baseUrl">The base URL, from <c>OKF_INSTALL_URL</c> or the default.</param>
    /// <returns>The normalized base.</returns>
    /// <exception cref="OkfUpgradeException">The URL is malformed, or is cleartext to somewhere that is not this machine.</exception>
    /// <remarks>
    /// <b>Every</b> trailing slash is trimmed, not one: `https://host//latest.json` is a
    /// different URL to most caches and some servers, and a base URL ending `//` is an
    /// ordinary copy-paste. install.sh and install.ps1 agree, and they have to — the three
    /// read the same environment variable.
    /// </remarks>
    public static Uri BaseUri(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);

        string trimmed = baseUrl.TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            throw new OkfUpgradeException($"'{baseUrl}' is not a URL.");
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return uri;
        }

        // Cleartext is permitted to loopback and nowhere else. The acceptance suites serve
        // a fixture release over plain http on localhost, and refusing that would mean the
        // download path could only ever be tested against the real host. Over any other
        // hop, a digest fetched down the same cleartext channel as the bytes it describes
        // proves nothing at all: whoever answered wrote both.
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && uri.IsLoopback)
        {
            return uri;
        }

        throw new OkfUpgradeException(
            $"refusing to upgrade over {uri.Scheme}: use an https URL (got {baseUrl}).\n" +
            $"    A digest fetched over the same cleartext channel as the binary it describes\n" +
            $"    proves nothing. Plain http is allowed to loopback only.");
    }

    /// <summary>The manifest URL for a release.</summary>
    /// <param name="baseUri">The normalized base URL.</param>
    /// <param name="version">The pinned version, or <see langword="null" /> for the newest.</param>
    /// <returns>The manifest URL.</returns>
    /// <remarks>
    /// The host's layout, verified against it: the newest release is
    /// <c>&lt;base&gt;/latest.json</c> and every published release keeps its own copy at
    /// <c>&lt;base&gt;/v&lt;version&gt;/latest.json</c>, which is what makes a version
    /// pinnable by name with no index to consult. The channel does not appear here, because
    /// the host publishes one manifest at its root today (see
    /// <see cref="OkfUpgradeChannel.Rc" />).
    /// </remarks>
    public static Uri ManifestUri(Uri baseUri, string? version)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        string normalized = (version ?? string.Empty).Trim();
        if (normalized.StartsWith('v'))
        {
            normalized = normalized[1..];
        }

        return normalized.Length == 0
            ? Under(baseUri, ManifestFileName)
            : Under(baseUri, $"v{normalized}/{ManifestFileName}");
    }

    /// <summary>Where an asset is downloaded from.</summary>
    /// <param name="baseUri">The normalized base URL.</param>
    /// <param name="asset">The asset.</param>
    /// <returns>The asset's URL.</returns>
    /// <remarks>
    /// The manifest's relative <c>path</c>, never its absolute <c>url</c>: that one names
    /// the package registry, and okf must not know GitLab exists (AD-42). Resolving against
    /// the base URL is also what makes the same manifest describe the release from the
    /// artifact host, from a mirror, or from a fixture on localhost.
    /// </remarks>
    public static Uri AssetUri(Uri baseUri, OkfUpgradeAsset asset)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(asset);
        return Under(baseUri, asset.Path.TrimStart('/'));
    }

    /// <summary>
    /// Resolves a path against the base URL and proves the result did not leave it.
    /// </summary>
    /// <param name="baseUri">The normalized base URL.</param>
    /// <param name="relative">The release-relative path.</param>
    /// <returns>The absolute URL, which sits under the base.</returns>
    /// <exception cref="OkfUpgradeException">The path climbs out of the base URL.</exception>
    /// <remarks>
    /// <see cref="Uri" /> collapses <c>..</c> as it parses, so an asset whose <c>path</c> is
    /// <c>../../evil</c> resolves against <c>https://mirror/okf</c> to
    /// <c>https://mirror/evil</c>. The scheme and the host are safe either way, because the
    /// URL is built by appending to the base rather than by resolving a reference — but the
    /// subtree the operator pointed <c>OKF_INSTALL_URL</c> at is not, and on a mirror that
    /// serves more than okf that is the difference between "the release directory" and
    /// "anything on this host". So the containment the rest of this file assumes is checked.
    /// </remarks>
    private static Uri Under(Uri baseUri, string relative)
    {
        Uri candidate = new Uri($"{Root(baseUri)}/{relative}");
        string prefix = baseUri.AbsolutePath.TrimEnd('/') + "/";

        if (!string.Equals(candidate.Scheme, baseUri.Scheme, StringComparison.Ordinal)
            || !string.Equals(candidate.Authority, baseUri.Authority, StringComparison.Ordinal)
            || !candidate.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new OkfUpgradeException(
                $"'{relative}' resolves to {candidate}, which is outside {baseUri} — refusing to\n" +
                "    fetch it. A release manifest may only name paths within the base URL it was\n" +
                "    served from.");
        }

        return candidate;
    }

    /// <summary>
    /// The base URL as a string with no trailing slash, which is what every URL below is
    /// built by appending to.
    /// </summary>
    /// <remarks>
    /// <see cref="Uri.ToString" /> is not that string: a URL with an empty path renders as
    /// `https://host/`, so appending to it produces `https://host//latest.json` — a
    /// different URL to most caches and some servers, and precisely the doubled path
    /// <see cref="BaseUri" /> trims the caller's slashes to prevent.
    /// </remarks>
    private static string Root(Uri baseUri) => baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');

    /// <summary>
    /// Decides where a redirect may go.
    /// </summary>
    /// <param name="current">The URL that answered with the redirect.</param>
    /// <param name="location">The <c>Location</c> header, absolute or relative.</param>
    /// <param name="httpsOnly">Whether the request started on https.</param>
    /// <returns>The URL to try next.</returns>
    /// <exception cref="OkfUpgradeException">There is no location, or it leaves https.</exception>
    /// <remarks>
    /// This is curl's <c>--proto-redir '=https'</c>, which is why install.sh passes it and
    /// why install.ps1 says in its own comments that it cannot. One 302 down to <c>http</c>
    /// would fetch the manifest AND the binary in cleartext from whoever answered, and a
    /// sha256 compared against a manifest that came down the same channel proves exactly
    /// nothing. A request that STARTED on http (loopback, a fixture) may stay on http:
    /// pinning the scheme to whatever the caller asked for is the honest rule — no silent
    /// upgrade and no silent downgrade.
    /// </remarks>
    public static Uri ResolveRedirect(Uri current, Uri? location, bool httpsOnly)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (location is null)
        {
            throw new OkfUpgradeException($"{current} answered with a redirect and no Location.");
        }

        Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (httpsOnly && !string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new OkfUpgradeException(
                $"refusing a redirect from https to {next.Scheme}: {current} -> {next}\n" +
                "    An https base URL is followed only to https, on every hop.");
        }

        return next;
    }

    /// <summary>
    /// The production <see cref="Fetch" />: one <see cref="HttpClient" />, redirects
    /// followed by hand so the scheme can be checked on every hop.
    /// </summary>
    /// <param name="userAgent">The <c>User-Agent</c> to send.</param>
    /// <returns>A fetch over the real network.</returns>
    public static Fetch HttpFetch(string userAgent) => uri => Get(uri, userAgent);

    private static ResponseStream Get(Uri uri, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(uri);

        bool httpsOnly = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        Uri current = uri;

        for (int hop = 0; hop <= MaximumRedirects; hop++)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

            HttpResponseMessage response;
            try
            {
                response = SharedClient.Value.Send(request, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                throw new OkfUpgradeException(
                    $"could not fetch {current}: {exception.Message}\n" +
                    "    If this cannot resolve, note that get.okf.tychostation.dev resolves only\n" +
                    "    inside Ringo's network today — see ringo/okf-net#26.",
                    exception);
            }

            if (IsRedirect(response.StatusCode))
            {
                Uri? next = response.Headers.Location;
                response.Dispose();
                current = ResolveRedirect(current, next, httpsOnly);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                response.Dispose();
                throw new OkfUpgradeException($"could not fetch {current}: HTTP {status}.");
            }

            // The response owns the stream; disposing it disposes the response with it.
            return new ResponseStream(response);
        }

        throw new OkfUpgradeException(
            $"gave up after {MaximumRedirects} redirects starting at {uri}.");
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static HttpClient CreateClient()
    {
        // Redirects are followed by hand (see ResolveRedirect), which is the only way to
        // refuse one that leaves https — the handler would have followed it already.
#pragma warning disable CA2000 // ownership of `handler` transfers to the HttpClient below
        // (disposeHandler: true), which disposes it; the analyzer cannot see ownership
        // transfer through a bool ctor argument, and a `using` here would dispose the
        // handler before the returned HttpClient ever uses it.
        SocketsHttpHandler handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
        };
#pragma warning restore CA2000

        return new HttpClient(handler, disposeHandler: true) { Timeout = RequestTimeout };
    }

    private static OkfUpgradeManifest ReadManifest(Uri manifestUri, Fetch fetch)
    {
        string json;
        try
        {
            using Stream stream = fetch(manifestUri);
            json = ReadBounded(stream, manifestUri);
        }
        catch (Exception exception) when (exception is not OkfUpgradeException)
        {
            throw new OkfUpgradeException($"could not read {manifestUri}: {exception.Message}", exception);
        }

        try
        {
            return OkfUpgradeManifest.Parse(json);
        }
        catch (OkfUpgradeException exception)
        {
            throw new OkfUpgradeException($"{manifestUri}: {exception.Message}", exception);
        }
    }

    private static void Download(Uri assetUri, string staging, Fetch fetch)
    {
        FileStream file;
        try
        {
            // CreateNew, so a name collision is an error rather than a silent overwrite,
            // and the very first write is also the check that this directory is writable.
            file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OkfUpgradeException(
                $"cannot write to {Path.GetDirectoryName(staging)}: {exception.Message}\n" +
                "    okf upgrade replaces the binary where it already is, so it needs to write\n" +
                "    there. Reinstall into a directory you own instead:\n" +
                "        curl -fsSL https://get.okf.tychostation.dev/install.sh | sh",
                exception);
        }

        using (file)
        {
            using Stream source = fetch(assetUri);
            CopyBounded(source, file, assetUri);
        }
    }

    /// <summary>
    /// Copies a download to disk, refusing at <see cref="MaximumAssetBytes" />.
    /// </summary>
    /// <remarks>
    /// The chunk that would cross the bound is never written, so the staging file cannot
    /// exceed it even by a buffer — which is the point, since the file is being written into
    /// the directory the user's binary lives in.
    /// </remarks>
    private static void CopyBounded(Stream source, Stream destination, Uri assetUri)
    {
        byte[] buffer = new byte[81920];
        long total = 0L;

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (total + read > MaximumAssetBytes)
            {
                throw new OkfUpgradeException(
                    $"{assetUri} is bigger than {MaximumAssetBytes / (1024 * 1024)} MB, so the download\n" +
                    "    was abandoned. No okf release is anywhere near that size; the host is broken\n" +
                    "    or is not the host you think it is. Nothing was installed.");
            }

            total += read;
            destination.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// Reads a manifest as text, refusing at <see cref="MaximumManifestBytes" />.
    /// </summary>
    private static string ReadBounded(Stream source, Uri manifestUri)
    {
        byte[] buffer = new byte[8192];
        using MemoryStream text = new MemoryStream();

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (text.Length + read > MaximumManifestBytes)
            {
                throw new OkfUpgradeException(
                    $"{manifestUri} is bigger than {MaximumManifestBytes / 1024} KB, so it was not\n" +
                    "    read. A release manifest is a few kilobytes of JSON.");
            }

            text.Write(buffer, 0, read);
        }

        // Through a StreamReader rather than Encoding.UTF8.GetString, so a manifest written
        // with a byte-order mark still parses: JsonDocument refuses a leading U+FEFF.
        text.Position = 0;
        using StreamReader reader = new StreamReader(text);
        return reader.ReadToEnd();
    }

    private static void Move(OkfUpgradeStep step)
    {
        if (step.Kind == OkfUpgradeStepKind.Retire)
        {
            // A `.old` from a previous upgrade is in the way of this one. Deleting it here
            // as well as at startup covers the run where the file was still locked then.
            TryDelete(step.Destination);
        }

        File.Move(step.Source, step.Destination, overwrite: true);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Name(Architecture architecture) =>
        architecture.ToString().ToLowerInvariant();

    /// <summary>Keeps the response alive for as long as the caller is reading its body.</summary>
    private sealed class ResponseStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _inner;

        public ResponseStream(HttpResponseMessage response)
        {
            _response = response;
            _inner = response.Content.ReadAsStream();
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
