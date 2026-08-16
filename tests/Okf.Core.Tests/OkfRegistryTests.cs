namespace Okf.Core.Tests;

/// <summary>The vault registry: entries, ids, and the file (PRD CLI-2, decisions.md §6).</summary>
public class OkfRegistryTests : IDisposable
{
    private static readonly DateTimeOffset Registered = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());

    /// <summary>Builds two projects with vaults, a bare bundle, and a config directory.</summary>
    public OkfRegistryTests()
    {
        Directory.CreateDirectory(Path.Combine(this.root, "alpha", "okf", "bundles", "one"));
        Directory.CreateDirectory(Path.Combine(this.root, "elsewhere", "alpha", "okf", "bundles", "two"));
        Directory.CreateDirectory(Path.Combine(this.root, "foreign-bundle"));
        Directory.CreateDirectory(Path.Combine(this.root, ".config", "okf"));
    }

    [Fact]
    public void RegisteringTheSamePathTwiceAddsOneEntry()
    {
        var registry = OkfRegistry.Empty();
        var first = registry.Register(Project("alpha"), Registered);
        var second = registry.Register(Project("alpha"), Registered);

        Assert.True(first.Added);
        Assert.False(second.Added);
        Assert.Equal(first.Entry.Id, second.Entry.Id);
        Assert.Single(registry.Entries);
    }

    /// <summary>
    /// A registered path is stored in the vocabulary a working set is: the same
    /// normalization <see cref="OkfDiscovery" /> performs, so `okf register &lt;project&gt;`
    /// and `okf lint &lt;project&gt;` name the same vault.
    /// </summary>
    [Fact]
    public void ClassificationAgreesWithDiscoveryAboutWhatAVaultIs()
    {
        var environment = Environment(this.root);

        foreach (var path in new[] { Project("alpha"), Path.Combine(Project("alpha"), "okf") })
        {
            var (classified, kind) = OkfRegistry.Classify(path);

            Assert.Equal(OkfRegistryKind.Vault, kind);
            Assert.Equal(OkfDiscovery.Resolve(path, environment).VaultRoot, classified);
        }

        var (bundle, bundleKind) = OkfRegistry.Classify(Path.Combine(this.root, "foreign-bundle"));

        Assert.Equal(OkfRegistryKind.Bundle, bundleKind);
        Assert.Equal(
            OkfDiscovery.Resolve(bundle, environment).Bundles.Single().Root,
            bundle);
    }

    [Fact]
    public void ClassifyingSomethingThatIsNotADirectoryIsRefused()
    {
        var file = Path.Combine(this.root, "notes.md");
        File.WriteAllText(file, "x");

        Assert.Throws<OkfDiscoveryException>(() => OkfRegistry.Classify(file));
        Assert.Throws<OkfDiscoveryException>(() => OkfRegistry.Classify(Path.Combine(this.root, "nowhere")));
    }

    /// <summary>
    /// Ids are readable and unique. A vault directory literally called <c>okf</c> is named
    /// for the project above it, so the two vaults here do not both want to be called
    /// <c>okf</c> — and when two really do collide, the second is suffixed rather than
    /// overwriting the first.
    /// </summary>
    [Fact]
    public void IdsComeFromTheDirectoryNameAndAreUniquified()
    {
        var registry = OkfRegistry.Empty();

        var first = registry.Register(Project("alpha"), Registered);
        var second = registry.Register(Path.Combine(this.root, "elsewhere", "alpha"), Registered);
        var bundle = registry.Register(Path.Combine(this.root, "foreign-bundle"), Registered);

        Assert.Equal("alpha", first.Entry.Id);
        Assert.Equal("alpha-2", second.Entry.Id);
        Assert.Equal("foreign-bundle", bundle.Entry.Id);
        Assert.Equal(["alpha", "alpha-2", "foreign-bundle"], registry.Entries.Select(entry => entry.Id));
    }

    /// <summary>
    /// The id is chosen once and stored, never derived from the path on read — which is the
    /// property that lets a directory move without the entry changing identity.
    /// </summary>
    [Fact]
    public void AnIdIsReadBackAsWrittenEvenWhenItNoLongerMatchesTheDirectory()
    {
        var moved = Path.Combine(this.root, "elsewhere", "alpha", "okf");
        var registry = OkfRegistry.Parse(
            $$"""
            {
              "entries": [
                {
                  "id": "alpha",
                  "path": {{System.Text.Json.JsonSerializer.Serialize(moved)}},
                  "kind": "vault",
                  "registeredAt": "2026-08-16T09:30:00Z"
                }
              ]
            }
            """,
            "test");

        var entry = Assert.Single(registry.Entries);

        Assert.Equal("alpha", entry.Id);
        Assert.Equal(moved, entry.Path);
        Assert.True(entry.Exists);
    }

    [Fact]
    public void TheStampIsTheCanonicalUtcFormEveryOtherWriterUses()
    {
        var registry = OkfRegistry.Empty();
        var entry = registry.Register(Project("alpha"), new DateTimeOffset(2026, 8, 16, 11, 30, 0, TimeSpan.FromHours(2))).Entry;

        Assert.Equal("2026-08-16T09:30:00Z", entry.RegisteredAt);
        Assert.True(OkfCanonicalTimestamp.IsCanonical(entry.RegisteredAt));
    }

    [Fact]
    public void TheFileIsSortedByIdAndEndsInExactlyOneNewline()
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Path.Combine(this.root, "foreign-bundle"), Registered);
        registry.Register(Project("alpha"), Registered);

        var json = registry.ToJson();

        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.True(
            json.IndexOf("\"alpha\"", StringComparison.Ordinal)
            < json.IndexOf("\"foreign-bundle\"", StringComparison.Ordinal),
            "entries are written in id order regardless of the order they were registered in");
    }

    [Fact]
    public void WritingRoundTripsAndRewritingChangesNoBytes()
    {
        var path = RegistryPath;
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        registry.Register(Path.Combine(this.root, "foreign-bundle"), Registered);
        registry.Save(path);

        var written = File.ReadAllText(path);
        var reloaded = OkfRegistry.Load(path);
        reloaded.Save(path);

        Assert.Equal(written, File.ReadAllText(path));
        Assert.Equal(
            registry.Entries.Select(entry => (entry.Id, entry.Path, entry.Kind, entry.RegisteredAt)),
            reloaded.Entries.Select(entry => (entry.Id, entry.Path, entry.Kind, entry.RegisteredAt)));

        // The write goes through a temp file; none of it may survive the write.
        Assert.Equal(
            [OkfRegistry.FileName],
            Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    /// <summary>
    /// A registry that cannot be written is reported in okf's own vocabulary and mapped to
    /// the usage exit code by the caller — never a raw <see cref="IOException" /> escaping
    /// from the middle of a command.
    /// </summary>
    [Fact]
    public void AnUnwritableRegistryIsAConfigFailureRatherThanARawIoError()
    {
        // A directory where the file must go: every write to it fails, on every platform.
        var path = Path.Combine(this.root, "blocked", OkfRegistry.FileName);
        Directory.CreateDirectory(path);
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);

        var exception = Assert.Throws<OkfConfigException>(() => registry.Save(path));

        Assert.Contains("Cannot write registry", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(this.root, "blocked")));
    }

    [Fact]
    public void SavingCreatesTheConfigDirectoryWhenItIsAbsent()
    {
        var path = Path.Combine(this.root, "fresh", "okf", OkfRegistry.FileName);
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        registry.Save(path);

        Assert.True(File.Exists(path));
        Assert.Single(OkfRegistry.Load(path).Entries);
    }

    [Fact]
    public void AMissingRegistryFileReadsAsAnEmptyRegistryAndWritesNothing()
    {
        var path = RegistryPath;

        Assert.Empty(OkfRegistry.Load(path).Entries);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// The registry is machine-maintained, so it is strict JSON — unlike <c>okf.json</c>,
    /// which is JSONC by AD-32 because a person writes it. A comment here would be erased by
    /// the next write, so it is refused rather than silently dropped.
    /// </summary>
    [Theory]
    [InlineData("""{ "entries": [] } // machine-written""")]
    [InlineData("""{ "entries": [{ "id": "a", "path": "/tmp/a", "kind": "vault", "registeredAt": "2026-08-16T09:30:00Z" },] }""")]
    [InlineData("""{ "entries": [{ "id": "a", "path": "relative/path", "kind": "vault", "registeredAt": "2026-08-16T09:30:00Z" }] }""")]
    [InlineData("""{ "entries": [{ "id": "a", "path": "/tmp/a", "kind": "folder", "registeredAt": "2026-08-16T09:30:00Z" }] }""")]
    [InlineData("""{ "entries": [{ "id": "a", "path": "/tmp/a", "kind": "vault" }] }""")]
    [InlineData("""{ "entries": [{ "id": "a", "path": "/tmp/a", "kind": "vault", "registeredAt": "x" }, { "id": "a", "path": "/tmp/b", "kind": "vault", "registeredAt": "x" }] }""")]
    [InlineData("""["a"]""")]
    public void AMalformedRegistryIsRefusedRatherThanPartiallyRead(string json) =>
        Assert.Throws<OkfConfigException>(() => OkfRegistry.Parse(json, "test"));

    [Fact]
    public void UnregisteringWorksByIdOrByPathAndAnUnknownOneIsANoOp()
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        registry.Register(Path.Combine(this.root, "foreign-bundle"), Registered);

        Assert.Equal("alpha", registry.Unregister("alpha", this.root)?.Id);
        Assert.Null(registry.Unregister("alpha", this.root));
        Assert.Equal("foreign-bundle", registry.Unregister("foreign-bundle", this.root)?.Id);
        Assert.Empty(registry.Entries);
    }

    /// <summary>
    /// `okf register &lt;project&gt;` stores `&lt;project&gt;/okf`, so `okf unregister
    /// &lt;project&gt;` — the same word the person typed — has to find it.
    /// </summary>
    [Fact]
    public void UnregisteringAProjectRootRemovesTheVaultItRegistered()
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);

        Assert.NotNull(registry.Unregister("alpha", this.root));
        Assert.Empty(registry.Entries);
    }

    [Fact]
    public void PruningRemovesOnlyTheEntriesWhosePathIsGone()
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        var doomed = registry.Register(Path.Combine(this.root, "foreign-bundle"), Registered).Entry;
        Directory.Delete(doomed.Path);

        var pruned = registry.Prune();

        Assert.Equal(["foreign-bundle"], pruned.Select(entry => entry.Id));
        Assert.Equal(["alpha"], registry.Entries.Select(entry => entry.Id));
        Assert.Empty(registry.Prune());
    }

    /// <summary>Removes the temporary tree.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private string RegistryPath => Path.Combine(this.root, ".config", "okf", OkfRegistry.FileName);

    private string Project(string name) => Path.Combine(this.root, name);

    private OkfEnvironment Environment(string workingDirectory) =>
        new(workingDirectory, [new KeyValuePair<string, string>("HOME", this.root)]);
}
