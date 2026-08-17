using System.Text;

namespace Okf.Core.Tests.Skills;

/// <summary>
/// The skills that ship inside the binary (work item #41): that the embedded set is the
/// set in the repository, and that each one carries the metadata a skill host preloads.
/// </summary>
/// <remarks>
/// The oracle is <c>skills/</c> itself, read from the checkout by a path that has nothing
/// to do with the embedding: the resources are globbed into <c>Okf.Core.csproj</c>, and a
/// skill added to the repository but not embedded — a broken glob, a renamed folder, a
/// stale build — is exactly what these assertions catch.
/// </remarks>
public class OkfSkillsTests
{
    [Fact]
    public void TheEmbeddedSetIsTheSetOnDisk()
    {
        var onDisk = SkillDirectories();

        Assert.NotEmpty(onDisk);
        Assert.Equal(
            onDisk.Select(Path.GetFileName).ToArray(),
            OkfSkills.Names.ToArray());
    }

    [Fact]
    public void EverySkillIsEmbeddedByteForByte()
    {
        foreach (var directory in SkillDirectories())
        {
            var name = Path.GetFileName(directory);
            var skill = OkfSkills.Find(name);

            Assert.NotNull(skill);
            Assert.Equal(
                File.ReadAllText(Path.Combine(directory, OkfSkills.SkillFileName)),
                skill.Content);
        }
    }

    [Fact]
    public void ASkillIsNamedByItsDirectoryAndSaysSoInItsFrontmatter()
    {
        foreach (var skill in OkfSkills.All)
        {
            var frontmatter = OkfDocument.Parse(skill.Content).Frontmatter;
            Assert.Equal(skill.Name, (frontmatter["name"] as OkfScalar)?.Value);
        }
    }

    [Fact]
    public void TheDescriptionIsTheFrontmatterDescriptionOnOneLine()
    {
        foreach (var skill in OkfSkills.All)
        {
            var written = (OkfDocument.Parse(skill.Content).Frontmatter["description"] as OkfScalar)?.Value;
            Assert.NotNull(written);

            // The oracle is the frontmatter as written — a folded YAML scalar over several
            // lines — with its whitespace collapsed by a route that shares no code with
            // OkfSkills: what a host preloads is one line, and it is this one.
            Assert.Equal(
                string.Join(' ', written.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)),
                skill.Description);
            Assert.DoesNotContain('\n', skill.Description);
            Assert.NotEmpty(skill.Description);
        }
    }

    [Fact]
    public void TheResourceNameIsTheOneTheProjectEmbedsUnder()
    {
        var assembly = typeof(OkfSkills).Assembly;
        var embedded = assembly.GetManifestResourceNames();

        foreach (var name in OkfSkills.Names)
        {
            Assert.Contains(OkfSkills.ResourceNameFor(name), embedded);
        }
    }

    [Fact]
    public void AnUnknownNameIsNotFound() => Assert.Null(OkfSkills.Find("okf-nonesuch"));

    private static IReadOnlyList<string> SkillDirectories()
    {
        var root = RepositoryRoot()
            ?? throw new InvalidOperationException(
                "The tests are not running inside a checkout; the on-disk skill set has no oracle here.");

        return [.. Directory
            .EnumerateDirectories(Path.Combine(root, OkfSkills.DirectoryName))
            .Where(directory => File.Exists(Path.Combine(directory, OkfSkills.SkillFileName)))
            .OrderBy(directory => Path.GetFileName(directory), StringComparer.Ordinal)];
    }

    private static string? RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Okf.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}

/// <summary>
/// Where <c>okf skills install</c> writes, what it refuses to overwrite, and where
/// <c>okf skills path</c> then finds a skill (work item #41).
/// </summary>
/// <remarks>
/// Every case injects an <see cref="OkfEnvironment" /> whose <c>HOME</c> and
/// <c>XDG_DATA_HOME</c> point inside a temporary tree, so nothing here reads or writes the
/// developer's real home directory.
/// </remarks>
public class OkfSkillInstallerTests
{
    [Fact]
    public void AnInstallWithNoHostWritesTheUserDataCopy()
    {
        using var tree = new TempTree();
        var files = OkfSkillInstaller.Install(Environment(tree));

        Assert.Equal(
            OkfSkills.Names.Select(name => Path.Combine(tree.Root, "data", "okf", "skills", name, "SKILL.md")),
            files.Select(file => file.Path));
        Assert.All(files, file => Assert.Equal(OkfSkillInstallStatus.Written, file.Status));
    }

    [Fact]
    public void AnInstalledSkillIsTheEmbeddedBytes()
    {
        using var tree = new TempTree();
        var files = OkfSkillInstaller.Install(Environment(tree));

        foreach (var file in files)
        {
            var skill = OkfSkills.Find(file.SkillName);
            Assert.NotNull(skill);
            Assert.Equal(
                Encoding.UTF8.GetBytes(skill.Content),
                File.ReadAllBytes(file.Path));
        }
    }

    [Fact]
    public void AHostIsWrittenOnlyWhenTheMachineShowsIt()
    {
        using var tree = new TempTree();
        tree.CreateDirectory("home/.claude");
        var claude = Path.Combine(tree.Root, "home", ".claude", "skills");
        var pi = Path.Combine(tree.Root, "home", ".pi", "agent", "skills");

        var files = OkfSkillInstaller.Install(Environment(tree));

        Assert.Contains(files, file => file.Path.StartsWith(claude, StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.Path.StartsWith(pi, StringComparison.Ordinal));
        Assert.False(Directory.Exists(pi));
    }

    [Fact]
    public void BothKnownHostsAreDetected()
    {
        using var tree = new TempTree();
        tree.CreateDirectory("home/.claude");
        tree.CreateDirectory("home/.pi/agent");

        var hosts = OkfSkillInstaller.DetectHosts(Environment(tree), OkfSkillScope.User);

        Assert.Equal([OkfSkillHost.Data, OkfSkillHost.Claude, OkfSkillHost.Pi], hosts);
    }

    /// <summary>
    /// Detection looks in the scope it was asked about: a project's own <c>.claude</c> is
    /// what <c>--scope project</c> reports, and a home directory's is not.
    /// </summary>
    [Fact]
    public void HostsAreDetectedInTheScopeAskedAbout()
    {
        using var tree = new TempTree();
        tree.CreateDirectory("project/.claude");
        tree.CreateDirectory("home/.pi/agent");
        var environment = Environment(tree, Path.Combine(tree.Root, "project"));

        Assert.Equal(
            [OkfSkillHost.Data, OkfSkillHost.Claude],
            OkfSkillInstaller.DetectHosts(environment, OkfSkillScope.Project));
        Assert.Equal(
            [OkfSkillHost.Data, OkfSkillHost.Pi],
            OkfSkillInstaller.DetectHosts(environment, OkfSkillScope.User));
    }

    [Fact]
    public void ProjectScopeWritesBesideTheProjectAndNotTheHome()
    {
        using var tree = new TempTree();
        var project = tree.CreateDirectory("project/.claude");

        var files = OkfSkillInstaller.Install(
            Environment(tree, Path.Combine(tree.Root, "project")),
            new OkfSkillInstallOptions
            {
                Hosts = [OkfSkillHost.Claude],
                Scope = OkfSkillScope.Project,
            });

        Assert.All(files, file =>
            Assert.StartsWith(Path.Combine(project, "skills"), file.Path, StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(tree.Root, "home", ".claude")));
    }

    [Fact]
    public void AGenericHostWritesTheDirectoryItWasGiven()
    {
        using var tree = new TempTree();
        var target = Path.Combine(tree.Root, "elsewhere", "skills");

        var files = OkfSkillInstaller.Install(
            Environment(tree),
            new OkfSkillInstallOptions { Hosts = [OkfSkillHost.Directory], Directory = target });

        Assert.Equal(
            OkfSkills.Names.Select(name => Path.Combine(target, name, "SKILL.md")),
            files.Select(file => file.Path));
    }

    [Fact]
    public void AGenericHostWithNoDirectoryIsRefused()
    {
        using var tree = new TempTree();

        Assert.Throws<ArgumentException>(() => OkfSkillInstaller.Install(
            Environment(tree),
            new OkfSkillInstallOptions { Hosts = [OkfSkillHost.Directory] }));
    }

    [Fact]
    public void ASecondInstallWritesNothingAndReportsEveryFileUnchanged()
    {
        using var tree = new TempTree();
        var environment = Environment(tree);
        var first = OkfSkillInstaller.Install(environment);
        var stamps = first.ToDictionary(file => file.Path, file => File.GetLastWriteTimeUtc(file.Path));

        var second = OkfSkillInstaller.Install(environment);

        Assert.All(second, file => Assert.Equal(OkfSkillInstallStatus.Unchanged, file.Status));
        Assert.All(second, file => Assert.Equal(stamps[file.Path], File.GetLastWriteTimeUtc(file.Path)));
    }

    [Fact]
    public void AnEditedSkillIsSkippedRatherThanOverwritten()
    {
        using var tree = new TempTree();
        var environment = Environment(tree);
        var edited = OkfSkillInstaller.Install(environment)[0].Path;
        File.WriteAllText(edited, "--- edited by hand ---\n");

        var files = OkfSkillInstaller.Install(environment);

        Assert.Equal(
            OkfSkillInstallStatus.SkippedModified,
            files.Single(file => file.Path == edited).Status);
        Assert.Equal("--- edited by hand ---\n", File.ReadAllText(edited));
    }

    [Fact]
    public void ForceOverwritesTheEditedSkill()
    {
        using var tree = new TempTree();
        var environment = Environment(tree);
        var installed = OkfSkillInstaller.Install(environment)[0];
        File.WriteAllText(installed.Path, "--- edited by hand ---\n");

        var files = OkfSkillInstaller.Install(environment, new OkfSkillInstallOptions { Force = true });

        Assert.Equal(
            OkfSkillInstallStatus.Written,
            files.Single(file => file.Path == installed.Path).Status);
        Assert.Equal(OkfSkills.Find(installed.SkillName)!.Content, File.ReadAllText(installed.Path));
    }

    [Fact]
    public void NothingIsFoundBeforeAnythingIsInstalled()
    {
        using var tree = new TempTree();
        Assert.Null(OkfSkillInstaller.Locate("okf-capture", Environment(tree)));
    }

    [Fact]
    public void AnInstalledSkillIsFoundInTheDataCopy()
    {
        using var tree = new TempTree();
        var environment = Environment(tree);
        OkfSkillInstaller.Install(environment);

        Assert.Equal(
            Path.Combine(tree.Root, "data", "okf", "skills", "okf-capture", "SKILL.md"),
            OkfSkillInstaller.Locate("okf-capture", environment));
    }

    [Fact]
    public void TheProjectsOwnCopyWinsOverEveryUserLevelOne()
    {
        using var tree = new TempTree();
        var project = tree.CreateDirectory("project");
        var environment = Environment(tree, project);
        OkfSkillInstaller.Install(environment);
        var vendored = tree.Write("project/skills/okf-capture/SKILL.md", "vendored\n");

        Assert.Equal(vendored, OkfSkillInstaller.Locate("okf-capture", environment));
    }

    [Fact]
    public void AProjectHostInstallWinsOverTheUserLevelCopies()
    {
        using var tree = new TempTree();
        var project = tree.CreateDirectory("project");
        var environment = Environment(tree, project);
        OkfSkillInstaller.Install(environment);
        var inProject = tree.Write("project/.claude/skills/okf-capture/SKILL.md", "project host\n");

        Assert.Equal(inProject, OkfSkillInstaller.Locate("okf-capture", environment));
    }

    [Fact]
    public void TheCandidateOrderIsProjectFirstThenDataThenTheHosts()
    {
        using var tree = new TempTree();
        var project = tree.CreateDirectory("project");

        Assert.Equal(
            [
                Path.Combine(project, "skills", "okf-capture", "SKILL.md"),
                Path.Combine(project, ".claude", "skills", "okf-capture", "SKILL.md"),
                Path.Combine(project, ".pi", "agent", "skills", "okf-capture", "SKILL.md"),
                Path.Combine(tree.Root, "data", "okf", "skills", "okf-capture", "SKILL.md"),
                Path.Combine(tree.Root, "home", ".claude", "skills", "okf-capture", "SKILL.md"),
                Path.Combine(tree.Root, "home", ".pi", "agent", "skills", "okf-capture", "SKILL.md"),
            ],
            OkfSkillInstaller.Candidates("okf-capture", Environment(tree, project)));
    }

    private static OkfEnvironment Environment(TempTree tree, string? currentDirectory = null)
    {
        var home = tree.CreateDirectory("home");
        return new OkfEnvironment(
            currentDirectory ?? tree.Root,
            [
                new KeyValuePair<string, string>("HOME", home),
                new KeyValuePair<string, string>("XDG_DATA_HOME", Path.Combine(tree.Root, "data")),

                // Windows reads this one instead, and an unset one resolves to the real
                // profile — which is how a test suite writes into the developer's own
                // %LOCALAPPDATA%. Same directory, so the assertions below hold on both.
                new KeyValuePair<string, string>("LOCALAPPDATA", Path.Combine(tree.Root, "data")),
            ]);
    }
}
