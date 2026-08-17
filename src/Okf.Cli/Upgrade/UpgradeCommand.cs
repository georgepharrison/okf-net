using Okf.Core;

namespace Okf.Cli.Upgrade;

/// <summary>
/// What the running binary is, and how it reaches the network — the three facts
/// <c>okf upgrade</c> cannot read off a command line.
/// </summary>
/// <remarks>
/// Every default here describes this process, so production passes nothing. A test passes
/// all three, because it must describe a running binary that is NOT the test runner: the
/// version to compare against, a throwaway file to replace, and a fetch that answers from
/// memory instead of from the artifact host.
/// </remarks>
internal sealed class UpgradeRuntime
{
    /// <summary>The running binary's version.</summary>
    public string Version { get; init; } = CliApplication.Version;

    /// <summary>The file <c>okf upgrade</c> would replace.</summary>
    public string? ExecutablePath { get; init; } = System.Environment.ProcessPath;

    /// <summary>How a URL is read; the real HTTP client when unset.</summary>
    public OkfUpgrade.Fetch? Fetch { get; init; }
}

/// <summary>
/// <c>okf upgrade</c> — replace this binary with the newest release (work item #23).
/// </summary>
/// <remarks>
/// The one verb that talks to a network, and the only one that ever will: the exception to
/// AD-7 is confined to <see cref="OkfUpgrade" />, and no other command reaches it — okf
/// never checks for an update on its own, on any verb, at any time. Core decides what to
/// download and whether the bytes are the right ones; this file renders and maps to exit
/// codes (AD-6).
/// </remarks>
internal static class UpgradeCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>upgrade</c>.</param>
    /// <param name="environment">The environment <c>OKF_INSTALL_URL</c> is read from.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and notes go.</param>
    /// <param name="runtime">
    /// What the running binary is. Left unset in production, where it is this process;
    /// supplied by every test, which must neither replace the test runner nor reach the
    /// artifact host.
    /// </param>
    /// <returns>The process exit code.</returns>
    public static int Run(
        string[] args,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error,
        UpgradeRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        UpgradeRuntime self = runtime ?? new UpgradeRuntime();
        if (Parse(args, error) is not { } parsed)
        {
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        OkfUpgradeOptions options = Options(parsed, environment, self);
        if (Resolved(options, self, error) is not { } plan)
        {
            return CliApplication.ExitUsage;
        }

        return Apply(new UpgradeRun(parsed, plan, self, options.UserAgent), output, error);
    }

    private static UpgradeArguments? Parse(string[] args, TextWriter error)
    {
        try
        {
            return UpgradeArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf upgrade --help` for usage.");
            return null;
        }
    }

    private static OkfUpgradeOptions Options(
        UpgradeArguments parsed,
        OkfEnvironment environment,
        UpgradeRuntime self)
    {
        string? configured = environment.GetVariable(OkfUpgradeOptions.BaseUrlVariable);
        return new OkfUpgradeOptions
        {
            BaseUrl = configured is not null ? configured : OkfUpgradeOptions.DefaultBaseUrl,
            UsePublicDownloadUrl = configured is null,
            Version = parsed.Version,
            Channel = parsed.Channel,
            CurrentVersion = self.Version,
            ExecutablePath = self.ExecutablePath,
            UserAgent = $"okf/{self.Version}",
        };
    }

    private static OkfUpgradePlan? Resolved(OkfUpgradeOptions options, UpgradeRuntime self, TextWriter error)
    {
        try
        {
            return OkfUpgrade.Resolve(options, self.Fetch);
        }
        catch (OkfUpgradeException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return null;
        }
    }

    private sealed record UpgradeRun(
        UpgradeArguments Parsed,
        OkfUpgradePlan Plan,
        UpgradeRuntime Self,
        string UserAgent);

    private static int Apply(UpgradeRun run, TextWriter output, TextWriter error)
    {
        UpgradeArguments parsed = run.Parsed;
        OkfUpgradePlan plan = run.Plan;
        if (parsed.Json)
        {
            output.WriteLine(Json(plan));
        }

        if (parsed.Check)
        {
            return Check(plan, parsed.Json, output);
        }

        WriteResolved(parsed, plan, output);
        if (DryRun(parsed, output) || AlreadyCurrent(parsed, plan, output))
        {
            return CliApplication.ExitSuccess;
        }

        return Install(run, output, error);
    }

    private static bool DryRun(UpgradeArguments parsed, TextWriter output)
    {
        if (!parsed.DryRun)
        {
            return false;
        }

        if (!parsed.Json)
        {
            output.WriteLine("==> --dry-run: nothing was downloaded or written");
        }

        return true;
    }

    private static void WriteResolved(UpgradeArguments parsed, OkfUpgradePlan plan, TextWriter output)
    {
        if (!parsed.Json)
        {
            WritePlan(plan, parsed.Version is not null, output);
        }
    }

    /// <summary>
    /// An explicit `--version` is a re-install as much as an upgrade — pinning back to
    /// an older release is exactly how a tester bisects one — so it downloads even when
    /// the version matches. Without one, matching means there is nothing to do.
    /// </summary>
    private static bool AlreadyCurrent(UpgradeArguments parsed, OkfUpgradePlan plan, TextWriter output)
    {
        if (!plan.IsUpToDate || parsed.Version is not null)
        {
            return false;
        }

        if (!parsed.Json)
        {
            output.WriteLine($"==> already {plan.AvailableVersion}; nothing to do");
        }

        return true;
    }

    private static int Install(UpgradeRun run, TextWriter output, TextWriter error)
    {
        UpgradeArguments parsed = run.Parsed;
        OkfUpgradePlan plan = run.Plan;
        UpgradeRuntime self = run.Self;
        string userAgent = run.UserAgent;
        // Only here, on the path that actually installs: a `.old` left by a previous
        // Windows upgrade is deleted at the start of THIS verb and of no other. Nothing
        // else in okf-net may delete a file it did not just write.
        OkfUpgrade.RemoveRetired(plan.TargetPath);
        if (Applied(plan, self, userAgent, error) is not { } result)
        {
            return CliApplication.ExitUsage;
        }

        WriteInstalled(parsed, plan, result, output);
        return CliApplication.ExitSuccess;
    }

    private static OkfUpgradeResult? Applied(
        OkfUpgradePlan plan,
        UpgradeRuntime self,
        string userAgent,
        TextWriter error)
    {
        try
        {
            return OkfUpgrade.Apply(plan, self.Fetch, userAgent);
        }
        catch (OkfUpgradeException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return null;
        }
    }

    private static void WriteInstalled(
        UpgradeArguments parsed,
        OkfUpgradePlan plan,
        OkfUpgradeResult result,
        TextWriter output)
    {
        if (parsed.Json)
        {
            return;
        }

        output.WriteLine("    sha256 verified");
        output.WriteLine($"==> upgraded {plan.CurrentVersion} -> {result.Version}");
        if (result.RetiredPath is { Length: > 0 } retired)
        {
            output.WriteLine(
                $"    the running binary was moved to {retired}; the next `okf upgrade` deletes it");
        }
    }

    /// <summary>
    /// <c>--check</c>: report and exit, never download. Exit 1 for "an upgrade is
    /// available" makes this a mechanical gate in AD-5's sense — a shell can branch on it
    /// with no output parsing, the same way `okf index --check` reports drift.
    /// </summary>
    private static int Check(OkfUpgradePlan plan, bool json, TextWriter output)
    {
        if (!json)
        {
            output.WriteLine($"current:   {plan.CurrentVersion}");
            output.WriteLine($"available: {plan.AvailableVersion}");

            if (plan.IsDevelopmentBuild)
            {
                output.WriteLine(
                    "this is an unstamped local build, so every release counts as newer than it");
            }

            output.WriteLine(plan.IsUpToDate
                ? "okf is up to date"
                : "an upgrade is available; run `okf upgrade`");
        }

        return plan.IsUpToDate ? CliApplication.ExitSuccess : CliApplication.ExitDiagnostics;
    }

    private static void WritePlan(OkfUpgradePlan plan, bool pinned, TextWriter output)
    {
        output.WriteLine(pinned ? $"==> okf {plan.AvailableVersion}" : "==> okf (newest release)");
        output.WriteLine($"    manifest: {plan.ManifestUri}");
        output.WriteLine($"    version:  {plan.AvailableVersion}");
        output.WriteLine($"    asset:    {plan.Asset.Name}");
        output.WriteLine($"    binary:   {plan.AssetUri}");
        output.WriteLine($"    sha256:   {plan.Asset.Sha256}");
        output.WriteLine($"    replaces: {plan.TargetPath}");
    }

    private static string Json(OkfUpgradePlan plan)
    {
        return JsonOutput.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("current", plan.CurrentVersion);
            writer.WriteString("available", plan.AvailableVersion);
            writer.WriteString("asset", plan.Asset.Name);
            writer.WriteString("url", plan.AssetUri.ToString());
            writer.WriteString("sha256", plan.Asset.Sha256);
            writer.WriteBoolean("upToDate", plan.IsUpToDate);
            writer.WriteEndObject();
        });
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf upgrade [options]

            Replace this binary with a release from the public GitHub release host. The release manifest
            is read, the asset for this machine is downloaded beside the running binary,
            its sha256 is checked against the manifest, and only then is it renamed into
            place — so okf is either the old binary or the new one, never a partial file.
            Nothing outside the running binary's own directory is read or written.

            This is the only okf command that uses a network, and it does so only when you
            run it: no other verb ever checks for an update.

            Options:
              --check                Report the running version against the available one
                                     and stop. Downloads nothing.
              --version <x.y.z>      Install this release instead of the newest, with or
                                     without a leading `v`. Downgrades too.
              --channel <stable|rc>  Which channel to read (default: stable). Stable reads
                                     releases from main; rc reads candidates from dev.
              --dry-run              Resolve and report; download and write nothing.
              --json                 Report as JSON: current, available, asset, url,
                                     sha256, upToDate.
              --help, -h             Show this help

            Environment:
              OKF_INSTALL_URL        Base URL to upgrade from (default
                                     https://georgepharrison.github.io/okf-net). The same variable
                                     install.sh and install.ps1 read. With no explicit
                                     OKF_INSTALL_URL, assets use a validated absolute HTTPS
                                     downloadUrl from the public manifest. An explicit base
                                     always uses its contained relative path. An https base URL
                                     is followed only to https, on every redirect; plain http
                                     is accepted for loopback only.

            Exit codes:
              0  upgraded, already current, or reported under --check/--dry-run
              1  --check found an upgrade available
              2  refused before writing anything: an unusable base URL, an unreadable
                 manifest, no asset for this platform, a sha256 mismatch, an asset that
                 is oversized or outside the base URL, or an install directory that
                 cannot be written
            """);
    }
}
