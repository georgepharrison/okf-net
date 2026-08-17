using System.ComponentModel;
using System.Diagnostics;

namespace Okf.Core.Trust;

/// <summary>
/// Who <c>okf verify</c> will stamp as, and where that identity came from — or, when
/// there is none, what the operator has to do about it.
/// </summary>
public sealed class OkfActorResolution
{
    private OkfActorResolution(string? actor, string source, string? problem)
    {
        Actor = actor;
        Source = source;
        Problem = problem;
    }

    /// <summary>The resolved actor, or <see langword="null" /> when none could be found.</summary>
    public string? Actor { get; }

    /// <summary>Where the identity came from, for <c>--verbose</c> and dry-run output.</summary>
    public string Source { get; }

    /// <summary>Why there is no identity, when there is none; an actionable sentence.</summary>
    public string? Problem { get; }

    /// <summary>Whether an identity was found.</summary>
    public bool IsResolved => Actor is not null;

    /// <summary>Builds a resolved identity.</summary>
    /// <param name="actor">The actor to stamp.</param>
    /// <param name="source">Where it came from.</param>
    /// <returns>The resolution.</returns>
    public static OkfActorResolution Resolved(string actor, string source) => new(actor, source, null);

    /// <summary>Builds an unresolved identity.</summary>
    /// <param name="problem">What the operator has to do about it.</param>
    /// <returns>The resolution.</returns>
    public static OkfActorResolution Unresolved(string problem) => new(null, "none", problem);
}

/// <summary>
/// The identity chain behind <c>okf verify</c> (PRD CLI-13, decisions.md Q6):
/// <c>verify.actor</c> from okf configuration, else <c>git config --global user.email</c>,
/// else nothing.
/// </summary>
/// <remarks>
/// The git fallback reads the <strong>global</strong> configuration and never the
/// repository-local one, which is the whole point of Q6: a repository-local
/// <c>user.email</c> is frequently an agent identity (this repository is the example),
/// and stamping an agent's address behind a <c>human:</c> prefix would manufacture the
/// one signal §5.3 exists to carry.
/// </remarks>
public static class OkfVerifyIdentity
{
    /// <summary>The environment variables passed through to git, so a caller can make the read hermetic.</summary>
    public static IReadOnlyList<string> GitVariables { get; } =
        ["HOME", "XDG_CONFIG_HOME", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM"];

    /// <summary>Resolves the identity <c>okf verify</c> stamps as.</summary>
    /// <param name="project">The project configuration, when there is one.</param>
    /// <param name="global">The global configuration, when there is one.</param>
    /// <param name="gitEmail">
    /// Reads <c>git config --global user.email</c>. Injected so the chain is testable
    /// without a git installation; pass <see cref="GlobalUserEmail" /> for the real read.
    /// </param>
    /// <returns>The resolution.</returns>
    /// <exception cref="OkfConfigException"><c>verify.actor</c> names something other than a person.</exception>
    public static OkfActorResolution Resolve(OkfConfig? project, OkfConfig? global, Func<string?> gitEmail)
    {
        ArgumentNullException.ThrowIfNull(gitEmail);

        // PRD CLI-4's precedence, minus the layers that cannot carry an identity: project
        // configuration wins over the global file.
        foreach (var config in new[] { project, global })
        {
            if (config?.VerifyActor is { Length: > 0 } configured)
            {
                return OkfActorResolution.Resolved(FromConfig(configured, config.Source), $"verify.actor in {config.Source}");
            }
        }

        if (gitEmail() is { Length: > 0 } email)
        {
            return OkfActorResolution.Resolved(OkfActor.ToHuman(email), "git config --global user.email");
        }

        return OkfActorResolution.Unresolved(
            "no verifying identity. Set `verify.actor` in okf.json, or `git config --global user.email`. "
            + "The repository-local git email is deliberately not read: it is often an agent's.");
    }

    /// <summary>
    /// Reads <c>git config --global user.email</c>, returning <see langword="null" /> when
    /// it is unset, when git is not installed, or when git fails for any other reason.
    /// </summary>
    /// <param name="environment">
    /// The environment whose <see cref="GitVariables" /> are handed to the git process, so
    /// a test can point the read at a temporary config file instead of the operator's.
    /// </param>
    /// <returns>The configured email, or <see langword="null" />.</returns>
    public static string? GlobalUserEmail(OkfEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in (string[])["config", "--global", "--get", "user.email"])
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var name in GitVariables)
        {
            if (environment.GetVariable(name) is { Length: > 0 } value)
            {
                startInfo.Environment[name] = value;
            }
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            // Bounded: `git config` is a file read, and a command that never returns must
            // not become a command that never exits.
            if (!process.WaitForExit(GitTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            // Exit 1 is git's "the key is not set", which is a legitimate answer here and
            // not a failure to report.
            return process.ExitCode == 0 && output.Trim() is { Length: > 0 } email ? email : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            // No git on the PATH is the common case, and it is not an error: it just means
            // the chain falls through to its refusal, which says what to set.
            return null;
        }
    }

    private const int GitTimeoutMilliseconds = 5000;

    /// <summary>
    /// Renders a configured <c>verify.actor</c> as the actor to stamp. It may be written
    /// with or without the <c>human:</c> prefix; it may not be written as anything else,
    /// because CLI-13 stamps human review and a <c>process:</c> or
    /// <c>&lt;producer&gt;/&lt;version&gt;</c> identity behind that command would be a
    /// machine confirmation wearing a person's prefix.
    /// </summary>
    private static string FromConfig(string configured, string source)
    {
        var trimmed = configured.Trim();
        if (trimmed.Length == 0)
        {
            throw new OkfConfigException($"Config file '{source}': `verify.actor` must not be empty.");
        }

        if (!OkfActor.IsHuman(trimmed) && trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw new OkfConfigException(
                $"Config file '{source}': `verify.actor` is '{trimmed}', which is not a person. "
                + "`okf verify` stamps human review; write `human:<id>` or a bare id, and record a "
                + "machine confirmation with `--by` instead.");
        }

        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            throw new OkfConfigException(
                $"Config file '{source}': `verify.actor` is '{trimmed}', which reads as the "
                + "`<producer>/<version>` form §7 reserves for a tool. `okf verify` stamps human review.");
        }

        return OkfActor.ToHuman(trimmed);
    }
}
