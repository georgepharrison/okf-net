using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Okf.Core;

/// <summary>What a registry entry points at (PRD CLI-2).</summary>
public enum OkfRegistryKind
{
    /// <summary>A vault: a directory holding <c>bundles/</c>.</summary>
    Vault,

    /// <summary>A bare bundle root, foreign or not.</summary>
    Bundle,
}

/// <summary>Serialized spellings of <see cref="OkfRegistryKind" />.</summary>
public static class OkfRegistryKindExtensions
{
    /// <summary>Renders a kind as it is written in <c>registry.json</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><c>vault</c> or <c>bundle</c>.</returns>
    public static string ToRegistryString(this OkfRegistryKind kind) =>
        kind == OkfRegistryKind.Vault ? "vault" : "bundle";

    /// <summary>Parses a kind as written in <c>registry.json</c>.</summary>
    /// <param name="text">The value read from the file.</param>
    /// <param name="kind">The parsed kind.</param>
    /// <returns><see langword="true" /> when the value is one okf-net writes.</returns>
    public static bool TryParse(string? text, out OkfRegistryKind kind)
    {
        switch (text)
        {
            case "vault":
                kind = OkfRegistryKind.Vault;
                return true;
            case "bundle":
                kind = OkfRegistryKind.Bundle;
                return true;
            default:
                kind = OkfRegistryKind.Vault;
                return false;
        }
    }
}

/// <summary>
/// One registry entry: a vault root or a bare bundle root, stored by absolute path under
/// an id that outlives a move (PRD CLI-2).
/// </summary>
public sealed class OkfRegistryEntry
{
    /// <summary>Initializes an entry.</summary>
    /// <param name="id">The stable identifier, chosen once at register time.</param>
    /// <param name="path">The absolute, normalized path.</param>
    /// <param name="kind">Whether the path is a vault or a bare bundle root.</param>
    /// <param name="registeredAt">When it was registered, in the canonical form (<see cref="OkfCanonicalTimestamp" />).</param>
    public OkfRegistryEntry(string id, string path, OkfRegistryKind kind, string registeredAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(registeredAt);
        Id = id;
        Path = path;
        Kind = kind;
        RegisteredAt = registeredAt;
    }

    /// <summary>The stable identifier — chosen at register time and never recomputed.</summary>
    public string Id { get; }

    /// <summary>The absolute, normalized path of the vault or bundle root.</summary>
    public string Path { get; }

    /// <summary>Whether the path is a vault or a bare bundle root.</summary>
    public OkfRegistryKind Kind { get; }

    /// <summary>When it was registered, canonical RFC 3339 UTC.</summary>
    public string RegisteredAt { get; }

    /// <summary>Whether the path still exists on this machine.</summary>
    public bool Exists => Directory.Exists(Path);
}

/// <summary>
/// The registry of known vaults and bundles: <c>registry.json</c> in okf's global config
/// directory (<c>$XDG_CONFIG_HOME/okf/</c>, else <c>~/.config/okf/</c>). It is the one
/// mechanism by which anything beyond the current project enters a command's scope, and
/// <b>the personal vault is an ordinary entry in it</b> — no special-casing (decisions.md
/// §6, PRD CLI-2).
/// </summary>
/// <remarks>
/// <para>Machine-maintained, and therefore <b>strict JSON</b> rather than the JSONC
/// <c>okf.json</c> is (AD-32): nobody hand-edits a file whose only writer is
/// <c>okf register</c>, and the comments JSONC exists to carry would be erased by the next
/// write. Read through <see cref="JsonDocument" /> so nothing reflective is on the AOT
/// path, and written deterministically — entries sorted by id, two-space indent, one
/// trailing newline — so a rewrite that changed nothing produces the same bytes.</para>
/// <para>Reading a registry never writes one: discovery stays pure (PRD CORE-13).</para>
/// </remarks>
public sealed class OkfRegistry
{
    /// <summary>The registry filename inside okf's global config directory.</summary>
    public const string FileName = "registry.json";

    private readonly List<OkfRegistryEntry> entries;

    private OkfRegistry(IEnumerable<OkfRegistryEntry> entries) =>
        this.entries = [.. entries.OrderBy(entry => entry.Id, StringComparer.Ordinal)];

    /// <summary>The entries, ordered by id — the order they are written and read in.</summary>
    public IReadOnlyList<OkfRegistryEntry> Entries => this.entries;

    /// <summary>An empty registry, which is what a machine with no registry file has.</summary>
    /// <returns>The empty registry.</returns>
    public static OkfRegistry Empty() => new([]);

    /// <summary>The registry file's path for an environment.</summary>
    /// <param name="environment">The environment to resolve against.</param>
    /// <returns>The absolute path of <c>registry.json</c>.</returns>
    public static string PathFor(OkfEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return System.IO.Path.Combine(environment.ConfigDirectory, FileName);
    }

    /// <summary>Reads the registry, returning an empty one when the file does not exist.</summary>
    /// <param name="environment">The environment to resolve the file's location against.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="OkfConfigException">The file exists but is not a valid registry.</exception>
    public static OkfRegistry Load(OkfEnvironment environment) => Load(PathFor(environment));

    /// <summary>Reads a registry file, returning an empty registry when it does not exist.</summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="OkfConfigException">The file exists but is not a valid registry.</exception>
    public static OkfRegistry Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
        {
            return Empty();
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new OkfConfigException($"Cannot read registry '{path}': {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new OkfConfigException($"Cannot read registry '{path}': {ex.Message}", ex);
        }

        return Parse(text, path);
    }

    /// <summary>Parses registry text.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="source">How to describe the origin in messages.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="OkfConfigException">The text is not a valid registry.</exception>
    public static OkfRegistry Parse(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(source);

        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty();
        }

        JsonDocument document;
        try
        {
            // Strict: no comments, no trailing commas. `okf register` is the only writer,
            // so there is no author whose comments a rewrite would destroy (AD-32).
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new OkfConfigException($"Registry '{source}' is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OkfConfigException($"Registry '{source}' must contain a JSON object at its root.");
            }

            if (!root.TryGetProperty("entries", out var array))
            {
                return Empty();
            }

            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new OkfConfigException($"Registry '{source}': `entries` must be an array.");
            }

            var parsed = new List<OkfRegistryEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in array.EnumerateArray())
            {
                var entry = ReadEntry(element, source);
                if (!seen.Add(entry.Id))
                {
                    throw new OkfConfigException(
                        $"Registry '{source}': two entries share the id '{entry.Id}'. An id names one entry.");
                }

                parsed.Add(entry);
            }

            return new OkfRegistry(parsed);
        }
    }

    /// <summary>
    /// Classifies a path the way <see cref="OkfDiscovery" /> does, so a registered path is
    /// in the same vocabulary a working set is: a directory holding <c>bundles/</c> is a
    /// vault, a project root holding <c>okf/bundles/</c> normalizes to its vault, and
    /// anything else is a bare bundle root.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The path to store and what it is.</returns>
    /// <exception cref="OkfDiscoveryException">The path is not an existing directory.</exception>
    public static (string Path, OkfRegistryKind Kind) Classify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));

        if (File.Exists(full))
        {
            throw new OkfDiscoveryException($"'{full}' is a file; okf registers vault and bundle directories.");
        }

        if (!Directory.Exists(full))
        {
            throw new OkfDiscoveryException($"No such directory: '{full}'.");
        }

        if (Directory.Exists(System.IO.Path.Combine(full, OkfDiscovery.BundlesDirectoryName)))
        {
            return (full, OkfRegistryKind.Vault);
        }

        var vault = System.IO.Path.Combine(full, OkfDiscovery.VaultDirectoryName);
        return Directory.Exists(System.IO.Path.Combine(vault, OkfDiscovery.BundlesDirectoryName))
            ? (vault, OkfRegistryKind.Vault)
            : (full, OkfRegistryKind.Bundle);
    }

    /// <summary>Finds the entry registered for a path, if any.</summary>
    /// <param name="path">The absolute path, already classified.</param>
    /// <returns>The entry, or <see langword="null" />.</returns>
    public OkfRegistryEntry? ByPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        return this.entries.FirstOrDefault(entry => string.Equals(entry.Path, full, PathComparison));
    }

    /// <summary>Finds the entry with an id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The entry, or <see langword="null" />.</returns>
    public OkfRegistryEntry? ById(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return this.entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Adds an entry for a path, or returns the existing one unchanged: registering a known
    /// path is a no-op success (PRD CLI-2).
    /// </summary>
    /// <param name="path">The path to register; classified with <see cref="Classify" />.</param>
    /// <param name="registeredAt">The instant to stamp, canonical.</param>
    /// <returns>The entry and whether it was newly added.</returns>
    /// <exception cref="OkfDiscoveryException">The path is not an existing directory.</exception>
    public (OkfRegistryEntry Entry, bool Added) Register(string path, DateTimeOffset registeredAt)
    {
        var (full, kind) = Classify(path);
        if (ByPath(full) is { } existing)
        {
            return (existing, false);
        }

        var entry = new OkfRegistryEntry(
            UniqueId(SlugFor(full, kind)),
            full,
            kind,
            OkfCanonicalTimestamp.ToCanonical(registeredAt));
        this.entries.Add(entry);
        this.entries.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        return (entry, true);
    }

    /// <summary>
    /// Removes the entry naming a path or carrying an id. Removing an unknown entry is a
    /// no-op success (PRD CLI-2), which is what the <see langword="null" /> return says.
    /// </summary>
    /// <param name="pathOrId">An absolute or relative path, or an entry id.</param>
    /// <param name="baseDirectory">The directory a relative path resolves against.</param>
    /// <returns>The removed entry, or <see langword="null" /> when nothing matched.</returns>
    public OkfRegistryEntry? Unregister(string pathOrId, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(pathOrId);
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);

        // The id is tried first and matched exactly: an id is a name okf chose, so it can
        // never be mistaken for a path a person typed, while a path that happens to spell
        // an id would otherwise remove the wrong entry.
        var found = ById(pathOrId)
            ?? ByPath(System.IO.Path.Combine(baseDirectory, pathOrId))
            ?? ByPathOfVault(System.IO.Path.Combine(baseDirectory, pathOrId));

        if (found is not null)
        {
            this.entries.Remove(found);
        }

        return found;
    }

    /// <summary>Removes every entry whose path no longer exists (<c>okf registry prune</c>).</summary>
    /// <returns>The removed entries, in id order.</returns>
    public IReadOnlyList<OkfRegistryEntry> Prune()
    {
        var missing = this.entries.Where(entry => !entry.Exists).ToList();
        foreach (var entry in missing)
        {
            this.entries.Remove(entry);
        }

        return missing;
    }

    /// <summary>Renders the registry as the bytes <see cref="Save" /> writes.</summary>
    /// <returns>The JSON text, ending in a newline.</returns>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("entries");
            foreach (var entry in this.entries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteString("path", entry.Path);
                writer.WriteString("kind", entry.Kind.ToRegistryString());
                writer.WriteString("registeredAt", entry.RegisteredAt);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    /// Writes the registry, creating the config directory if needed. The write is atomic —
    /// a sibling temp file, then a rename — so an interrupted write can never leave a
    /// half-written registry where the readable one was.
    /// </summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <exception cref="OkfConfigException">The file could not be written.</exception>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = System.IO.Path.GetDirectoryName(path)
            ?? throw new OkfConfigException($"'{path}' has no directory to write the registry into.");

        var temporary = System.IO.Path.Combine(directory, FileName + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new OkfConfigException($"Cannot write registry '{path}': {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// How two registered paths are compared. Ordinal everywhere: a case-insensitive
    /// comparison would silently merge two distinct directories on a case-sensitive
    /// filesystem, and the paths okf stores are the ones it normalized itself.
    /// </summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private OkfRegistryEntry? ByPathOfVault(string path)
    {
        // `okf unregister <project-root>` should remove the entry `okf register
        // <project-root>` created, and that entry stores `<project-root>/okf`.
        var vault = System.IO.Path.Combine(path, OkfDiscovery.VaultDirectoryName);
        return Directory.Exists(vault) ? ByPath(vault) : null;
    }

    /// <summary>
    /// The id a new entry gets: a slug of the directory name.
    /// </summary>
    /// <remarks>
    /// Three candidates were considered and two rejected. A hash of the normalized path is
    /// stable only while the path is — the entry would have to be re-keyed on the first
    /// move, which is exactly the case CLI-2 asks the id to survive. A random GUID survives
    /// a move but is unreadable, and <c>okf unregister &lt;id&gt;</c> and <c>okf registry list</c> are the
    /// inspectable state the issue asks for. A slug of the directory name is readable,
    /// chosen once at register time, and never recomputed — so a moved entry keeps the name
    /// it was registered under even when it no longer matches the directory it points at.
    /// A vault is named for its <em>parent</em> directory when the vault directory itself
    /// is called <c>okf</c>, because <c>~/okf</c> and <c>&lt;project&gt;/okf</c> would
    /// otherwise all be called <c>okf</c>.
    /// </remarks>
    private static string SlugFor(string path, OkfRegistryKind kind)
    {
        var name = System.IO.Path.GetFileName(path);
        if (kind == OkfRegistryKind.Vault
            && string.Equals(name, OkfDiscovery.VaultDirectoryName, StringComparison.Ordinal)
            && System.IO.Path.GetDirectoryName(path) is { Length: > 0 } parent
            && System.IO.Path.GetFileName(parent) is { Length: > 0 } parentName)
        {
            name = parentName;
        }

        var slug = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(char.ToLowerInvariant(character));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var trimmed = slug.ToString().Trim('-');
        return trimmed.Length > 0 ? trimmed : "vault";
    }

    private string UniqueId(string slug)
    {
        if (ById(slug) is null)
        {
            return slug;
        }

        // Bounded by the entry count rather than open-ended: n entries can occupy at most n
        // suffixes, so the loop always finds a free one — and a bug that made it not is a
        // failure rather than a process that spins.
        for (var suffix = 2; suffix <= this.entries.Count + 2; suffix++)
        {
            var candidate = slug + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            if (ById(candidate) is null)
            {
                return candidate;
            }
        }

        throw new OkfConfigException(
            $"Could not find a free id for '{slug}' among {this.entries.Count.ToString(CultureInfo.InvariantCulture)} entries.");
    }

    private static OkfRegistryEntry ReadEntry(JsonElement element, string source)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new OkfConfigException($"Registry '{source}': every entry must be an object.");
        }

        var id = RequiredString(element, "id", source);
        var path = RequiredString(element, "path", source);
        var kindText = RequiredString(element, "kind", source);
        if (!OkfRegistryKindExtensions.TryParse(kindText, out var kind))
        {
            throw new OkfConfigException(
                $"Registry '{source}': entry '{id}' has kind '{kindText}'; expected \"vault\" or \"bundle\".");
        }

        if (!System.IO.Path.IsPathRooted(path))
        {
            throw new OkfConfigException(
                $"Registry '{source}': entry '{id}' has a relative path '{path}'. " +
                "Entries are stored by absolute path so a registry means the same thing from any directory.");
        }

        return new OkfRegistryEntry(id, path, kind, RequiredString(element, "registeredAt", source));
    }

    private static string RequiredString(JsonElement element, string name, string source)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new OkfConfigException($"Registry '{source}': every entry needs a string `{name}`.");
        }

        return value.GetString() is { Length: > 0 } text
            ? text
            : throw new OkfConfigException($"Registry '{source}': `{name}` must not be empty.");
    }
}
