using System.Text;

namespace Okf.Core.Tests.Vault;

/// <summary>The vault registry: entries, ids, and the file (PRD CLI-2, decisions.md §6).</summary>
public sealed class OkfRegistryTests : IDisposable
{
    private static readonly DateTimeOffset Registered = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());

    /// <summary>Builds two projects with vaults, a bare bundle, and a config directory.</summary>
    public OkfRegistryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha", "okf", "bundles", "one"));
        Directory.CreateDirectory(Path.Combine(_root, "elsewhere", "alpha", "okf", "bundles", "two"));
        Directory.CreateDirectory(Path.Combine(_root, "foreign-bundle"));
        Directory.CreateDirectory(Path.Combine(_root, ".config", "okf"));
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
        var environment = Environment(_root);

        foreach (var path in new[] { Project("alpha"), Path.Combine(Project("alpha"), "okf") })
        {
            var (classified, kind) = OkfRegistry.Classify(path);

            Assert.Equal(OkfRegistryKind.Vault, kind);
            Assert.Equal(OkfDiscovery.Resolve(path, environment).VaultRoot, classified);
        }

        var (bundle, bundleKind) = OkfRegistry.Classify(Path.Combine(_root, "foreign-bundle"));

        Assert.Equal(OkfRegistryKind.Bundle, bundleKind);
        Assert.Equal(
            OkfDiscovery.Resolve(bundle, environment).Bundles.Single().Root,
            bundle);
    }

    [Fact]
    public void ClassifyingSomethingThatIsNotADirectoryIsRefused()
    {
        var file = Path.Combine(_root, "notes.md");
        File.WriteAllText(file, "x");

        // The two refusals are different findings and say so: a file is a wrong kind of
        // thing to register, a missing path is a typo.
        Assert.Contains(
            "is a file",
            Assert.Throws<OkfDiscoveryException>(() => OkfRegistry.Classify(file)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "No such directory",
            Assert.Throws<OkfDiscoveryException>(() => OkfRegistry.Classify(Path.Combine(_root, "nowhere"))).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A slug is ASCII letters and digits, lowercased, with every other run collapsed to one
    /// hyphen and no hyphen at either end — and a name with nothing to slug still has to
    /// produce an id, because the id is what <c>okf unregister</c> takes (PRD CLI-2).
    /// </summary>
    [Theory]
    [InlineData("Alpha Two", "alpha-two")]
    [InlineData("a--b", "a-b")]
    [InlineData("-alpha", "alpha")]
    [InlineData("alpha-", "alpha")]
    [InlineData("!!!", "vault")]
    public void AnIdIsASlugOfTheDirectoryName(string directory, string expected)
    {
        var path = Path.Combine(_root, directory);
        Directory.CreateDirectory(path);
        var registry = OkfRegistry.Empty();

        Assert.Equal(expected, registry.Register(path, Registered).Entry.Id);
    }

    /// <summary>
    /// Only a <em>vault</em> directory called <c>okf</c> is named for the project above it —
    /// a bare bundle that happens to be called <c>okf</c> is its own thing and keeps the
    /// name it has.
    /// </summary>
    [Fact]
    public void ABundleDirectoryCalledOkfIsNotNamedForItsParent()
    {
        var bundle = Path.Combine(_root, "notes", OkfDiscovery.VaultDirectoryName);
        Directory.CreateDirectory(bundle);
        var registry = OkfRegistry.Empty();

        var (entry, _) = registry.Register(bundle, Registered);

        Assert.Equal(OkfRegistryKind.Bundle, entry.Kind);
        Assert.Equal(OkfDiscovery.VaultDirectoryName, entry.Id);
    }

    /// <summary>
    /// The id is matched before either path lookup, so a word that is one entry's id and
    /// another entry's directory name removes the entry it names, not the one it is beside.
    /// </summary>
    [Fact]
    public void UnregisteringMatchesTheIdBeforeThePath()
    {
        var registry = OkfRegistry.Empty();
        var elsewhere = registry.Register(Path.Combine(_root, "elsewhere", "alpha"), Registered).Entry;
        var beside = registry.Register(Project("alpha"), Registered).Entry;

        Assert.Equal("alpha", elsewhere.Id);
        Assert.Equal("alpha-2", beside.Id);
        Assert.Equal("alpha", registry.Unregister("alpha", _root)?.Id);
        Assert.Equal(["alpha-2"], registry.Entries.Select(entry => entry.Id));
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
        var second = registry.Register(Path.Combine(_root, "elsewhere", "alpha"), Registered);
        var bundle = registry.Register(Path.Combine(_root, "foreign-bundle"), Registered);

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
        var moved = Path.Combine(_root, "elsewhere", "alpha", "okf");
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
        registry.Register(Path.Combine(_root, "foreign-bundle"), Registered);
        registry.Register(Project("alpha"), Registered);

        var json = registry.ToJson();

        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.True(
            json.IndexOf("\"alpha\"", StringComparison.Ordinal)
            < json.IndexOf("\"foreign-bundle\"", StringComparison.Ordinal),
            "entries are written in id order regardless of the order they were registered in");
    }

    /// <summary>
    /// AD-51 says the registry is written deterministically, which is a statement about
    /// bytes: UTF-8 with no byte-order mark, indented, LF, one trailing newline. A BOM in
    /// particular would ship silently — every JSON reader okf uses tolerates one — and
    /// would change the file on a platform that starts emitting it.
    /// </summary>
    [Fact]
    public void TheFileIsUtf8WithoutAByteOrderMark()
    {
        var path = RegistryPath;
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        registry.Save(path);

        var bytes = File.ReadAllBytes(path);

        Assert.Equal((byte)'{', bytes[0]);
        Assert.NotEqual<byte[]>([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal(Encoding.UTF8.GetBytes(registry.ToJson()), bytes);
    }

    /// <summary>
    /// The JSON is indented for a person to read and escapes nothing a path may legally
    /// contain — the relaxed encoder is what keeps <c>+</c> and <c>&amp;</c> in a directory
    /// name from being written as <c>+</c> and <c>&</c>.
    /// </summary>
    [Fact]
    public void TheJsonIsIndentedAndLeavesPathCharactersUnescaped()
    {
        var directory = Path.Combine(_root, "a+b&c");
        Directory.CreateDirectory(directory);
        var registry = OkfRegistry.Empty();
        registry.Register(directory, Registered);

        var json = registry.ToJson();

        Assert.Contains("\n  \"entries\": [", json, StringComparison.Ordinal);
        Assert.Contains(directory, json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritingRoundTripsAndRewritingChangesNoBytes()
    {
        var path = RegistryPath;
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        registry.Register(Path.Combine(_root, "foreign-bundle"), Registered);
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
        var path = Path.Combine(_root, "blocked", OkfRegistry.FileName);
        Directory.CreateDirectory(path);
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);

        var exception = Assert.Throws<OkfConfigException>(() => registry.Save(path));

        Assert.Contains("Cannot write registry", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "blocked")));
    }

    [Fact]
    public void SavingCreatesTheConfigDirectoryWhenItIsAbsent()
    {
        var path = Path.Combine(_root, "fresh", "okf", OkfRegistry.FileName);
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
    [InlineData("""{ "entries": ["a"] }""")]
    [InlineData("""{ "entries": [{ "id": "", "path": "/tmp/a", "kind": "vault", "registeredAt": "2026-08-16T09:30:00Z" }] }""")]
    public void AMalformedRegistryIsRefusedRatherThanPartiallyRead(string json) =>
        Assert.Throws<OkfConfigException>(() => OkfRegistry.Parse(json, "test"));

    [Fact]
    public void UnregisteringWorksByIdOrByPathAndAnUnknownOneIsANoOp()
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        registry.Register(Path.Combine(_root, "foreign-bundle"), Registered);

        Assert.Equal("alpha", registry.Unregister("alpha", _root)?.Id);
        Assert.Null(registry.Unregister("alpha", _root));
        Assert.Equal("foreign-bundle", registry.Unregister("foreign-bundle", _root)?.Id);
        Assert.Empty(registry.Entries);
    }

    /// <summary>
    /// `okf register &lt;project&gt;` stores `&lt;project&gt;/okf`, so `okf unregister
    /// &lt;project&gt;` — the same word the person typed — has to find it. The project here
    /// is the one whose id was uniquified to <c>alpha-2</c>, so nothing but the path lookup
    /// can be what matches.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnregisteringAProjectRootRemovesTheVaultItRegistered(bool relative)
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        var project = Path.Combine(_root, "elsewhere", "alpha");
        var entry = registry.Register(project, Registered).Entry;

        Assert.Equal("alpha-2", entry.Id);
        Assert.Equal(Path.Combine(project, "okf"), entry.Path);
        Assert.Equal("alpha-2", registry.Unregister(relative ? Path.Combine("elsewhere", "alpha") : project, _root)?.Id);
        Assert.Equal(["alpha"], registry.Entries.Select(existing => existing.Id));
    }

    /// <summary>
    /// A vault path is removable by the path itself, not only by the id — and the id is
    /// tried first, so a path that happens to spell another entry's id can never take the
    /// wrong entry out.
    /// </summary>
    [Fact]
    public void UnregisteringByTheStoredVaultPathRemovesThatEntry()
    {
        var registry = OkfRegistry.Empty();
        var alpha = registry.Register(Project("alpha"), Registered).Entry;
        registry.Register(Path.Combine(_root, "elsewhere", "alpha"), Registered);

        Assert.Equal("alpha", registry.Unregister(alpha.Path, _root)?.Id);
        Assert.Equal(["alpha-2"], registry.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void PruningRemovesOnlyTheEntriesWhosePathIsGone()
    {
        var registry = OkfRegistry.Empty();
        registry.Register(Project("alpha"), Registered);
        var doomed = registry.Register(Path.Combine(_root, "foreign-bundle"), Registered).Entry;
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
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private string RegistryPath => Path.Combine(_root, ".config", "okf", OkfRegistry.FileName);

    private string Project(string name) => Path.Combine(_root, name);

    private OkfEnvironment Environment(string workingDirectory) =>
        new(workingDirectory, [new KeyValuePair<string, string>("HOME", _root)]);
}
