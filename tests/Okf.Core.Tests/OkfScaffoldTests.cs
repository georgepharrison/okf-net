namespace Okf.Core.Tests;

/// <summary>
/// What <c>okf init</c> writes, what it refuses, and what it leaves alone (PRD CLI-8).
/// </summary>
/// <remarks>
/// The expected file set comes from decisions.md §2's layout and the dogfood friction log
/// (#20-9 for <c>raw/.gitignore</c>, #20-8 for the tag registry, #19-5/6 for the
/// markdownlint config, #20-7 for the log); the expected lint outcome comes from okf-net's
/// own rules run over the result, which is an oracle the scaffolding code has no say in.
/// </remarks>
public class OkfScaffoldTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 15, 19, 30, 45, TimeSpan.FromHours(-5));

    private static readonly string[] ExpectedLayout =
    [
        ".markdownlint.yaml",
        "README.md",
        "bundles/demo/about-this-bundle.md",
        "bundles/demo/index.md",
        "bundles/demo/log.md",
        "custodian/README.md",
        "custodian/recipe.json",
        "okf.json",
        "raw/.gitignore",
        "raw/manifest.json",
    ];

    [Fact]
    public void TheScaffoldedLayoutIsTheOneDecisionsDescribes()
    {
        using var tree = new TempTree();
        var result = Initialize(tree);

        Assert.Equal(ExpectedLayout, Layout(result.VaultRoot));
        Assert.Equal(ExpectedLayout.Length, result.CreatedCount);
        Assert.Equal(0, result.ExistingCount);
        Assert.False(result.IsNoOp);
    }

    [Fact]
    public void TheScaffoldedVaultLintsCleanUnderItsOwnConfig()
    {
        // The strongest available oracle: the vault okf init produces has to satisfy the
        // rules okf init just promoted, including the two tag rules the registry turns on.
        using var tree = new TempTree();
        var result = Initialize(tree);
        var config = OkfConfig.Load(Path.Combine(result.VaultRoot, OkfDiscovery.ConfigFileName));

        var diagnostics = new OkfLinter(new OkfLintOptions
        {
            Severities = new OkfSeverityResolver([config.Severities]),
            TagRegistry = config.TagRegistry,
            VaultRoot = result.VaultRoot,
            Today = TempBundle.Today,
        }).Lint([new OkfBundle(result.BundleRoot)]).Diagnostics;

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TheProjectConfigPromotesTheFourRulesAndWarnsOnTheTwoTagRules()
    {
        using var tree = new TempTree();
        var result = Initialize(tree);
        var config = OkfConfig.Load(Path.Combine(result.VaultRoot, OkfDiscovery.ConfigFileName));
        var severities = new OkfSeverityResolver([config.Severities]);

        Assert.Equal(OkfSeverity.Error, severities.Resolve(OkfRules.MissingDescription));
        Assert.Equal(OkfSeverity.Error, severities.Resolve(OkfRules.GeneratedIndexDrift));
        Assert.Equal(OkfSeverity.Error, severities.Resolve(OkfRules.MissingSourceResource));
        Assert.Equal(OkfSeverity.Error, severities.Resolve(OkfRules.RawItemMutated));
        Assert.Equal(OkfSeverity.Warning, severities.Resolve(OkfRules.MissingTags));
        Assert.Equal(OkfSeverity.Warning, severities.Resolve(OkfRules.UnregisteredTag));
    }

    [Fact]
    public void TheTagRegistryIsPresentAndCoversTheScaffoldedConcept()
    {
        // Friction #20-8: the registry cannot be introduced later without a change that is
        // simultaneously unenforceable and enormous. It is present from the first commit,
        // holding exactly the tags the one scaffolded concept carries — which is also the
        // discipline it documents, a tag added by the change that needed the word.
        using var tree = new TempTree();
        var result = Initialize(tree);
        var config = OkfConfig.Load(Path.Combine(result.VaultRoot, OkfDiscovery.ConfigFileName));
        var document = OkfDocument.Parse(
            File.ReadAllText(Path.Combine(result.BundleRoot, OkfScaffold.AboutThisBundleFileName)));

        var registry = Assert.IsAssignableFrom<IReadOnlyList<string>>(config.TagRegistry);
        var tags = Assert.IsType<OkfSequence>(document.Frontmatter["tags"])
            .OfType<OkfScalar>()
            .Select(tag => tag.Value);

        Assert.NotEmpty(registry);
        Assert.Equal(registry.Order(StringComparer.Ordinal), tags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheConfigIsJsoncAndCarriesItsReasons()
    {
        // Friction #19-10 / #20-13: okf.json is named .json and parsed as JSONC. The
        // convention is only worth anything if init actually uses it, and the reasons are
        // the half of a config file a reviewer reads.
        using var tree = new TempTree();
        var result = Initialize(tree);
        var text = File.ReadAllText(Path.Combine(result.VaultRoot, OkfDiscovery.ConfigFileName));

        Assert.Contains("// ", text, StringComparison.Ordinal);
        Assert.Contains("JSONC", text, StringComparison.Ordinal);

        // And it still parses: the comments are legal input to the reader, not decoration
        // that happens to survive.
        Assert.NotNull(OkfConfig.Parse(text, "test").TagRegistry);
    }

    [Fact]
    public void TheFirstConceptIsStampedInTheCanonicalTimestampForm()
    {
        using var tree = new TempTree();
        var result = Initialize(tree);
        var document = OkfDocument.Parse(
            File.ReadAllText(Path.Combine(result.BundleRoot, OkfScaffold.AboutThisBundleFileName)));

        var generated = Assert.IsType<OkfMapping>(document.Frontmatter["generated"]);
        var at = Assert.IsType<OkfScalar>(generated["at"]).Value;

        // The clock handed in sits at -05:00; what lands on disk is the same instant as
        // RFC 3339 UTC, which is the point of having a canonical form at all.
        Assert.Equal("2026-08-16T00:30:45Z", at);
        Assert.True(OkfCanonicalTimestamp.IsCanonical(at));
    }

    [Fact]
    public void TheRootIndexIsWhatTheRealGeneratorWouldEmit()
    {
        // Generated, not templated: an index born from a template is an index that drifts
        // the first time the renderer changes, and OKF0306 is promoted to error here.
        using var tree = new TempTree();
        var result = Initialize(tree);

        var plan = OkfIndexGenerator.Plan(new OkfBundle(result.BundleRoot));

        Assert.False(plan.HasDrift);
        Assert.Equal(OkfIndexStatus.Unchanged, plan.For(result.BundleRoot)!.Status);
        Assert.Contains(
            OkfIndexGenerator.RootFrontmatter,
            File.ReadAllText(Path.Combine(result.BundleRoot, OkfBundle.IndexFileName)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RawCarriesAGitignoreThatUnignoresEverything()
    {
        // Friction #20-9: a repo-wide `*.log` or `tmp/` silently eats captured evidence.
        // Directories first, because a file cannot be re-included while a parent directory
        // of it is still excluded — that is git's rule, not a style choice.
        using var tree = new TempTree();
        var result = Initialize(tree);
        var lines = File.ReadAllLines(Path.Combine(result.VaultRoot, "raw", ".gitignore"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        Assert.Equal(["!*/", "!*"], lines);
    }

    [Fact]
    public void TheMarkdownLintConfigExtendsAHostConfigWhenThereIsOne()
    {
        // Friction #19-6: a nested config REPLACES the parent rather than merging with it,
        // so a vault config without `extends` silently switches the host repo's disabled
        // rules back on.
        using var tree = new TempTree();
        tree.Write(".markdownlint.yaml", "MD013: false\n");
        var result = Initialize(tree);
        var lines = Settings(result.VaultRoot);

        // The whole line, not a substring: `extends` has to be a setting markdownlint
        // reads, and a commented-out one reads identically to a substring check.
        Assert.Equal(["extends: ../.markdownlint.yaml", "MD025: false"], lines);
    }

    [Fact]
    public void TheMarkdownLintConfigOmitsExtendsWhenThereIsNoHostConfig()
    {
        // `extends` naming a file that is not there is an error, which would break
        // markdownlint for a repository that had no config at all.
        using var tree = new TempTree();
        var result = Initialize(tree);

        Assert.Equal(["MD025: false"], Settings(result.VaultRoot));
    }

    [Theory]
    [InlineData(".markdownlint.yml")]
    [InlineData(".markdownlint.json")]
    public void AnyOfMarkdownlintsConfigNamesCountsAsAHostConfig(string name)
    {
        using var tree = new TempTree();
        tree.Write(name, "{}");
        var result = Initialize(tree);

        Assert.Equal([$"extends: ../{name}", "MD025: false"], Settings(result.VaultRoot));
    }

    [Fact]
    public void ASecondRunChangesNothingAndReportsWhatIsThere()
    {
        using var tree = new TempTree();
        var first = Initialize(tree);
        var before = Snapshot(first.VaultRoot);

        var second = OkfScaffold.Initialize(
            first.VaultRoot,
            new OkfScaffoldOptions { BundleName = "demo", Now = Noon.AddDays(30) });

        Assert.True(second.IsNoOp);
        Assert.Equal(ExpectedLayout.Length, second.ExistingCount);
        Assert.All(second.Files, file => Assert.Equal(OkfScaffoldStatus.Exists, file.Status));
        Assert.Equal(before, Snapshot(first.VaultRoot));
    }

    [Fact]
    public void AFileSomebodyElseWroteIsNeverOverwritten()
    {
        using var tree = new TempTree();
        var first = Initialize(tree);
        var readme = Path.Combine(first.VaultRoot, "README.md");
        File.WriteAllText(readme, "# Our vault\n\nWe rewrote this.\n");

        OkfScaffold.Initialize(first.VaultRoot, new OkfScaffoldOptions { BundleName = "demo", Now = Noon });

        Assert.Equal("# Our vault\n\nWe rewrote this.\n", File.ReadAllText(readme));
    }

    [Fact]
    public void AMissingFileIsRestoredWithoutTouchingTheRest()
    {
        using var tree = new TempTree();
        var first = Initialize(tree);
        File.Delete(Path.Combine(first.VaultRoot, "raw", ".gitignore"));

        var second = OkfScaffold.Initialize(
            first.VaultRoot,
            new OkfScaffoldOptions { BundleName = "demo", Now = Noon });

        Assert.Equal(1, second.CreatedCount);
        Assert.Equal(ExpectedLayout.Length - 1, second.ExistingCount);
        Assert.Equal(ExpectedLayout, Layout(first.VaultRoot));
    }

    [Fact]
    public void InitializingAtABundleRootIsRefused()
    {
        // Friction #19-14: the README trap surfaces here, by name, instead of later as a
        // generic OKF0001 on a file somebody already committed.
        using var tree = new TempTree();
        Initialize(tree);
        var bundleRoot = Path.Combine(tree.Root, "okf", "bundles", "demo");

        var failure = Assert.Throws<OkfScaffoldException>(
            () => OkfScaffold.Initialize(bundleRoot));

        Assert.Contains("is a bundle root", failure.Message, StringComparison.Ordinal);
        Assert.Contains("README.md", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(bundleRoot, "README.md")));
    }

    [Fact]
    public void InitializingInsideABundleRootIsRefused()
    {
        using var tree = new TempTree();
        Initialize(tree);
        var inside = Path.Combine(tree.Root, "okf", "bundles", "demo", "topics", "okf");

        var failure = Assert.Throws<OkfScaffoldException>(() => OkfScaffold.Initialize(inside));

        Assert.Contains("sits inside the bundle root", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(inside));
    }

    [Fact]
    public void ABundleRootThatDoesNotExistYetIsRefusedToo()
    {
        // The refusal has to describe the tree AFTER the run, not before it. `bundles/`
        // not existing yet is no reprieve: creating it is precisely what would turn the
        // target into a bundle root with a README.md sitting in it.
        using var tree = new TempTree();
        var vault = tree.CreateDirectory("okf");

        var failure = Assert.Throws<OkfScaffoldException>(
            () => OkfScaffold.Initialize(Path.Combine(vault, "bundles", "new-one")));

        Assert.Contains("is a bundle root", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(vault, "bundles")));
    }

    [SkippableFact]
    public void ASymlinkPointingAtABundleRootIsRefused()
    {
        // Path.GetFullPath normalizes `.` and `..` but does not follow links, and a
        // lexical path is not where the write lands: `okf init link` with
        // `link -> <vault>/bundles/<name>` used to walk past the refusal and put the
        // README trap in a real bundle root.
        using var tree = new TempTree();
        var first = Initialize(tree);
        var link = Path.Combine(tree.Root, "shortcut");
        Symlink(link, first.BundleRoot);
        var before = Snapshot(first.VaultRoot);

        var failure = Assert.Throws<OkfScaffoldException>(() => OkfScaffold.Initialize(link));

        Assert.Contains("is a bundle root", failure.Message, StringComparison.Ordinal);
        Assert.Contains(first.BundleRoot, failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(first.VaultRoot));
    }

    [SkippableFact]
    public void ASymlinkAnywhereAlongTheTargetPathIsFollowedBeforeRefusing()
    {
        // The link need not be the target itself. Here only an intermediate component is
        // one, which a check that resolves nothing but the final component still misses.
        using var tree = new TempTree();
        var first = Initialize(tree);
        var link = Path.Combine(tree.Root, "shortcut");
        Symlink(link, Path.Combine(first.VaultRoot, "bundles"));
        var before = Snapshot(first.VaultRoot);

        var failure = Assert.Throws<OkfScaffoldException>(
            () => OkfScaffold.Initialize(Path.Combine(link, "demo", "okf")));

        Assert.Contains("sits inside the bundle root", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(first.VaultRoot));
    }

    [SkippableFact]
    public void AnOrdinaryPathReachedThroughALinkStillScaffolds()
    {
        // Following links is not the same as distrusting them: a project checkout reached
        // through a symlink is an ordinary project, and refusing it would be the cure
        // doing more damage than the disease.
        using var tree = new TempTree();
        var project = tree.CreateDirectory("widgets");
        var link = Path.Combine(tree.Root, "shortcut");
        Symlink(link, project);

        var result = OkfScaffold.Initialize(
            Path.Combine(link, "okf"),
            new OkfScaffoldOptions { BundleName = "demo", Now = Noon });

        Assert.Equal(ExpectedLayout, Layout(result.VaultRoot));
        Assert.True(File.Exists(Path.Combine(project, "okf", "README.md")));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    [InlineData(".hidden")]
    [InlineData("")]
    public void AnUnusableBundleNameIsRefused(string name)
    {
        using var tree = new TempTree();

        Assert.Throws<OkfScaffoldException>(() => OkfScaffold.Initialize(
            Path.Combine(tree.Root, "okf"),
            new OkfScaffoldOptions { BundleName = name, Now = Noon }));
    }

    [Fact]
    public void ALongBundleNameStillScaffolds()
    {
        // The README's tree diagram pads the bundle line so its comment lines up with the
        // rest; a name longer than the column leaves no room, and the padding has to
        // degrade to one space rather than to a negative width.
        using var tree = new TempTree();
        var name = "a-rather-long-bundle-name-indeed";

        var result = OkfScaffold.Initialize(
            Path.Combine(tree.Root, "okf"),
            new OkfScaffoldOptions { BundleName = name, Now = Noon });

        Assert.Equal(name, Path.GetFileName(result.BundleRoot));
        Assert.Contains($"{name}/ #", File.ReadAllText(Path.Combine(result.VaultRoot, "README.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void ABundleNameThatCannotBeDerivedAsksForOne()
    {
        using var tree = new TempTree();

        // Nothing in "---" survives sanitizing, so there is no name to derive and the
        // command has to say which flag supplies one rather than inventing a bundle.
        var failure = Assert.Throws<OkfScaffoldException>(() => OkfScaffold.Initialize(
            Path.Combine(tree.CreateDirectory("---"), "okf"),
            new OkfScaffoldOptions { Now = Noon }));

        Assert.Contains("--name", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBundleNameDefaultsToTheProjectDirectorysName()
    {
        using var tree = new TempTree();
        var project = tree.CreateDirectory("My Project");

        var result = OkfScaffold.Initialize(
            Path.Combine(project, "okf"),
            new OkfScaffoldOptions { Now = Noon });

        Assert.Equal("my-project", Path.GetFileName(result.BundleRoot));
    }

    [Fact]
    public void ResolvingAVaultReadsAPathTheWayDiscoveryDoes()
    {
        using var tree = new TempTree();
        var environment = new OkfEnvironment(tree.Root);
        var project = tree.CreateDirectory("project");
        tree.CreateDirectory("vault/bundles");

        // A project root gets okf/ underneath it; a directory that already holds bundles/
        // IS the vault, so `okf init okf/` on an initialized vault stays idempotent rather
        // than nesting a second vault inside the first.
        Assert.Equal(Path.Combine(project, "okf"), OkfScaffold.ResolveVault("project", environment));
        Assert.Equal(Path.Combine(tree.Root, "vault"), OkfScaffold.ResolveVault("vault", environment));
        Assert.Equal(Path.Combine(tree.Root, "okf"), OkfScaffold.ResolveVault(null, environment));
    }

    /// <summary>Creates a directory symlink, skipping the test where the OS will not.</summary>
    private static void Symlink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows needs Developer Mode or elevation to create a directory symlink.
            throw new SkipException($"This platform will not create directory symlinks: {exception.Message}");
        }
    }

    /// <summary>The vault markdownlint config's actual settings — comments and blanks dropped.</summary>
    private static string[] Settings(string vault) =>
        [.. File.ReadAllLines(Path.Combine(vault, OkfScaffold.MarkdownLintFileName))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];

    private static OkfScaffoldResult Initialize(TempTree tree) =>
        OkfScaffold.Initialize(
            Path.Combine(tree.Root, "okf"),
            new OkfScaffoldOptions { BundleName = "demo", Now = Noon });

    /// <summary>Every file in a vault, as vault-relative paths with <c>/</c> separators.</summary>
    private static string[] Layout(string vault) =>
        [.. Directory.EnumerateFiles(vault, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(vault, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>Path and bytes of every file in a vault, so a test can prove a run wrote nothing.</summary>
    private static string Snapshot(string vault) =>
        string.Join(
            "\n",
            Directory.EnumerateFiles(vault, "*", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(file => $"{file}|{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))}"));
}

/// <summary>A throwaway directory tree, removed when the test finishes.</summary>
internal sealed class TempTree : IDisposable
{
    /// <summary>Creates an empty temporary tree.</summary>
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(Root);
    }

    /// <summary>The tree's root directory.</summary>
    public string Root { get; }

    /// <summary>Creates a directory inside the tree.</summary>
    /// <param name="relativePath">The path relative to the tree root.</param>
    /// <returns>The absolute path of the directory.</returns>
    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes a file inside the tree, creating its directory.</summary>
    /// <param name="relativePath">The path relative to the tree root.</param>
    /// <param name="content">The file's content.</param>
    /// <returns>The absolute path of the file.</returns>
    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
