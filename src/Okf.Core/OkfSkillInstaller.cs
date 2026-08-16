using System.Text;

namespace Okf.Core;

/// <summary>Where a skill install writes.</summary>
public enum OkfSkillHost
{
    /// <summary>
    /// okf's own user-level data directory — the canonical copy, written by every install
    /// that is not aimed at one host, and what <c>okf skills path</c> falls back to.
    /// </summary>
    Data = 0,

    /// <summary>Claude Code: <c>~/.claude/skills</c>, or <c>.claude/skills</c> in a project.</summary>
    Claude,

    /// <summary>pi: <c>~/.pi/agent/skills</c>, or <c>.pi/agent/skills</c> in a project.</summary>
    Pi,

    /// <summary>A directory named on the command line, for a host okf-net does not know about.</summary>
    Directory,
}

/// <summary>Whether a host target is the user's or one project's.</summary>
public enum OkfSkillScope
{
    /// <summary>The user's home directory: every project on this machine sees it.</summary>
    User = 0,

    /// <summary>The working directory: committed with the project, if the project wants that.</summary>
    Project,
}

/// <summary>What an install did to one file.</summary>
public enum OkfSkillInstallStatus
{
    /// <summary>The file was missing, or differed and <c>--force</c> was given; it was written.</summary>
    Written = 0,

    /// <summary>The file was already there with exactly these bytes.</summary>
    Unchanged,

    /// <summary>The file was there with different bytes and was left alone.</summary>
    SkippedModified,
}

/// <summary>One file an install considered, and what became of it.</summary>
/// <param name="SkillName">The skill the file belongs to.</param>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Status">Whether the run wrote it, found it identical, or left it alone.</param>
public sealed record OkfSkillInstallFile(string SkillName, string Path, OkfSkillInstallStatus Status);

/// <summary>Everything an install needs beyond the environment.</summary>
public sealed class OkfSkillInstallOptions
{
    /// <summary>
    /// The hosts to write. Empty means the default set: the data copy plus whichever hosts
    /// this machine shows (<see cref="OkfSkillInstaller.DetectHosts" />).
    /// </summary>
    public IReadOnlyList<OkfSkillHost> Hosts { get; set; } = [];

    /// <summary>Whether host targets resolve against the home directory or the project.</summary>
    public OkfSkillScope Scope { get; set; } = OkfSkillScope.User;

    /// <summary>The directory <see cref="OkfSkillHost.Directory" /> writes into.</summary>
    public string? Directory { get; set; }

    /// <summary>Whether a file whose bytes differ is overwritten rather than left alone.</summary>
    public bool Force { get; set; }
}

/// <summary>
/// Installs the embedded skills onto this machine, and finds the ones that are already
/// there. Nothing here downloads anything: the skills are in the binary (AD-7), so an
/// install is a set of file writes and a fresh <c>okf</c> is a complete one.
/// </summary>
/// <remarks>
/// <para><b>It never overwrites a file that differs.</b> A skill somebody edited is that
/// person's copy, and a reinstall that silently replaced it would be a tool deleting work
/// it did not write. The file is reported as skipped and the run still succeeds — the same
/// discipline <see cref="OkfScaffold" /> follows, for the same reason.</para>
/// <para><b>Output is deterministic.</b> A skill is written byte-for-byte as embedded, so
/// two installs of the same binary produce the same files and the second one reports every
/// file unchanged.</para>
/// </remarks>
public static class OkfSkillInstaller
{
    /// <summary>Claude Code's per-user directory, relative to the home or project root.</summary>
    public const string ClaudeDirectoryName = ".claude";

    /// <summary>pi's agent directory, relative to the home or project root.</summary>
    public static readonly string PiDirectoryPath = Path.Combine(".pi", "agent");

    /// <summary>
    /// The hosts an install writes when none was named: the data copy always, plus each
    /// host whose directory already exists in the given scope. A machine without Claude
    /// Code installed does not grow a <c>~/.claude</c> because okf was installed.
    /// </summary>
    /// <param name="environment">The environment home and project paths resolve against.</param>
    /// <param name="scope">Whether to look in the home directory or the project.</param>
    /// <returns>The hosts to write, in write order.</returns>
    public static IReadOnlyList<OkfSkillHost> DetectHosts(OkfEnvironment environment, OkfSkillScope scope)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var hosts = new List<OkfSkillHost> { OkfSkillHost.Data };
        var root = scope == OkfSkillScope.Project ? environment.CurrentDirectory : environment.HomeDirectory;

        if (System.IO.Directory.Exists(Path.Combine(root, ClaudeDirectoryName)))
        {
            hosts.Add(OkfSkillHost.Claude);
        }

        if (System.IO.Directory.Exists(Path.Combine(root, PiDirectoryPath)))
        {
            hosts.Add(OkfSkillHost.Pi);
        }

        return hosts;
    }

    /// <summary>The directory a host's skills live in — the parent of every <c>&lt;name&gt;/SKILL.md</c>.</summary>
    /// <param name="host">The host.</param>
    /// <param name="scope">Whether the host target is the user's or the project's.</param>
    /// <param name="environment">The environment the paths resolve against.</param>
    /// <param name="directory">The directory for <see cref="OkfSkillHost.Directory" />.</param>
    /// <returns>The absolute skills directory.</returns>
    /// <exception cref="ArgumentException"><see cref="OkfSkillHost.Directory" /> was asked for without a directory.</exception>
    public static string SkillsDirectory(
        OkfSkillHost host,
        OkfSkillScope scope,
        OkfEnvironment environment,
        string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // The data copy is the user's by definition: it is okf's own directory, not a
        // host's, and a project-scoped install still wants one canonical copy to point at.
        var root = scope == OkfSkillScope.Project && host != OkfSkillHost.Data
            ? environment.CurrentDirectory
            : environment.HomeDirectory;

        return host switch
        {
            OkfSkillHost.Data => Path.Combine(environment.DataDirectory, OkfSkills.DirectoryName),
            OkfSkillHost.Claude => Path.Combine(root, ClaudeDirectoryName, OkfSkills.DirectoryName),
            OkfSkillHost.Pi => Path.Combine(root, PiDirectoryPath, OkfSkills.DirectoryName),
            _ => Path.GetFullPath(Path.Combine(
                environment.CurrentDirectory,
                directory ?? throw new ArgumentException(
                    "A generic host install needs a directory to write into (--dir).",
                    nameof(directory)))),
        };
    }

    /// <summary>Installs the embedded skills, writing only what is missing or asked for.</summary>
    /// <param name="environment">The environment the targets resolve against.</param>
    /// <param name="options">The hosts, scope, directory and force flag.</param>
    /// <returns>Every file the run considered, in write order.</returns>
    /// <exception cref="IOException">A file could not be written.</exception>
    public static IReadOnlyList<OkfSkillInstallFile> Install(
        OkfEnvironment environment,
        OkfSkillInstallOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new OkfSkillInstallOptions();

        var hosts = options.Hosts.Count > 0
            ? options.Hosts
            : DetectHosts(environment, options.Scope);

        var files = new List<OkfSkillInstallFile>();
        foreach (var host in hosts)
        {
            var target = SkillsDirectory(host, options.Scope, environment, options.Directory);
            foreach (var skill in OkfSkills.All)
            {
                files.Add(Write(Path.Combine(target, skill.Name, OkfSkills.SkillFileName), skill, options.Force));
            }
        }

        return files;
    }

    /// <summary>
    /// Every place a skill of this name could be installed on this machine, nearest first:
    /// the project's own copies, then the user-level ones. The order is what makes a
    /// project able to pin a skill without every other project on the machine following it.
    /// </summary>
    /// <param name="name">The skill's name.</param>
    /// <param name="environment">The environment the paths resolve against.</param>
    /// <returns>The candidate <c>SKILL.md</c> paths, in resolution order.</returns>
    public static IReadOnlyList<string> Candidates(string name, OkfEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(environment);

        return
        [
            .. ProjectCandidates(name, environment.CurrentDirectory),
            Path.Combine(SkillsDirectory(OkfSkillHost.Data, OkfSkillScope.User, environment), name, OkfSkills.SkillFileName),
            Path.Combine(SkillsDirectory(OkfSkillHost.Claude, OkfSkillScope.User, environment), name, OkfSkills.SkillFileName),
            Path.Combine(SkillsDirectory(OkfSkillHost.Pi, OkfSkillScope.User, environment), name, OkfSkills.SkillFileName),
        ];
    }

    /// <summary>
    /// The candidates that live inside a project, as project-root-relative paths joined to
    /// <paramref name="projectRoot" />. These are the only ones a committed file may point
    /// at: an absolute or <c>~</c> path in a repository names one machine's home directory.
    /// </summary>
    /// <param name="name">The skill's name.</param>
    /// <param name="projectRoot">The project root the paths are relative to.</param>
    /// <returns>The candidate <c>SKILL.md</c> paths, in resolution order.</returns>
    public static IReadOnlyList<string> ProjectCandidates(string name, string projectRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);

        return
        [
            .. ProjectRelativeCandidates(name).Select(relative =>
                Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar))),
        ];
    }

    /// <summary>
    /// The project-relative forms of <see cref="ProjectCandidates" />, <c>/</c>-separated:
    /// a checkout that keeps the skills the way this repository does, then a project-scoped
    /// host install.
    /// </summary>
    /// <param name="name">The skill's name.</param>
    /// <returns>The candidate paths relative to a project root, in resolution order.</returns>
    public static IReadOnlyList<string> ProjectRelativeCandidates(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return
        [
            $"{OkfSkills.DirectoryName}/{name}/{OkfSkills.SkillFileName}",
            $"{ClaudeDirectoryName}/{OkfSkills.DirectoryName}/{name}/{OkfSkills.SkillFileName}",
            $".pi/agent/{OkfSkills.DirectoryName}/{name}/{OkfSkills.SkillFileName}",
        ];
    }

    /// <summary>Finds where a skill is installed on this machine.</summary>
    /// <param name="name">The skill's name.</param>
    /// <param name="environment">The environment the paths resolve against.</param>
    /// <returns>The first candidate that exists, or <see langword="null" /> when none does.</returns>
    public static string? Locate(string name, OkfEnvironment environment) =>
        Candidates(name, environment).FirstOrDefault(File.Exists);

    private static OkfSkillInstallFile Write(string path, OkfSkill skill, bool force)
    {
        if (File.Exists(path))
        {
            if (string.Equals(File.ReadAllText(path), skill.Content, StringComparison.Ordinal))
            {
                return new OkfSkillInstallFile(skill.Name, path, OkfSkillInstallStatus.Unchanged);
            }

            if (!force)
            {
                return new OkfSkillInstallFile(skill.Name, path, OkfSkillInstallStatus.SkippedModified);
            }
        }

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // UTF-8 with no byte-order mark and the content's own line endings: an install is
        // a copy of the embedded bytes, so two runs of one binary produce identical files
        // and a digest of an installed skill is comparable across machines.
        File.WriteAllText(path, skill.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new OkfSkillInstallFile(skill.Name, path, OkfSkillInstallStatus.Written);
    }
}
