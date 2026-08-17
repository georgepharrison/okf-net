using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Okf.Core.Capture;

/// <summary>The two shapes a capture takes in <c>raw/</c> (AD-16).</summary>
public enum OkfCaptureForm
{
    /// <summary>One file, as retrieved: <c>&lt;id&gt;.&lt;ext&gt;</c>.</summary>
    Flat,

    /// <summary>A directory <c>&lt;id&gt;/</c> holding the original plus its readable rendering.</summary>
    Packet,
}

/// <summary>What a write to the capture manifest did, or why it did nothing.</summary>
public enum OkfCaptureWriteOutcome
{
    /// <summary>A new entry was appended.</summary>
    Added,

    /// <summary>An uningested entry for the same item was replaced (AD-18).</summary>
    Recaptured,

    /// <summary>An open entry's <c>ingestion</c> was closed.</summary>
    Closed,

    /// <summary>
    /// The manifest does not parse, or does not read as a manifest. Reported and left
    /// exactly as found — never rewritten to make it parse (AD-18).
    /// </summary>
    ManifestUnreadable,

    /// <summary>The item already has an entry, and that entry is ingested (AD-18).</summary>
    AlreadyCaptured,

    /// <summary>The entry's <c>ingestion</c> is already closed; closing is what starts immutability.</summary>
    AlreadyClosed,

    /// <summary>Nothing in the manifest names the entry the caller asked for.</summary>
    NoSuchEntry,

    /// <summary>The item on disk is not something a capture entry can describe.</summary>
    ItemRefused,
}

/// <summary>A capture to append: an item already sitting under <c>&lt;vault&gt;/raw/</c>.</summary>
public sealed class OkfCaptureAddition
{
    /// <summary>The item's absolute path — a file for a flat capture, a directory for a packet.</summary>
    public required string ItemPath { get; init; }

    /// <summary>The capturing actor, in one of §7's forms.</summary>
    public required string CapturedBy { get; init; }

    /// <summary>When the capture happened; rendered by <see cref="OkfCanonicalTimestamp" /> (AD-24).</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Where the artifact came from, or <see langword="null" /> for material with no URL.</summary>
    public string? OriginalUrl { get; init; }

    /// <summary>The artifact's title, which becomes the ingested concept's <c>title</c>.</summary>
    public string? Title { get; init; }

    /// <summary>The artifact's own last-modified date, <c>YYYY-MM-DD</c>.</summary>
    public string? SourceLastModified { get; init; }

    /// <summary>
    /// The form the caller asserts, checked against what the item actually is. Null takes
    /// the form from the item: a file is flat, a directory is a packet.
    /// </summary>
    public OkfCaptureForm? Form { get; init; }
}

/// <summary>An ingestion to close on an existing entry.</summary>
public sealed class OkfCaptureClosure
{
    /// <summary>The entry's <c>id</c>, or a path under <c>raw/</c> that one of its files claims.</summary>
    public required string Entry { get; init; }

    /// <summary>The ingesting actor, in one of §7's forms.</summary>
    public required string By { get; init; }

    /// <summary>When the ingestion happened.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>
    /// The concepts the capture landed in, each vault-root-relative (AD-18) — one capture
    /// may be ingested into more than one bundle.
    /// </summary>
    public required IReadOnlyList<string> Concepts { get; init; }
}

/// <summary>The result of a manifest write: the new text, or the refusal and its reason.</summary>
public sealed class OkfCaptureWriteResult
{
    private OkfCaptureWriteResult(OkfCaptureWriteOutcome outcome, string id, string? text, string? problem)
    {
        Outcome = outcome;
        Id = id;
        Text = text;
        Problem = problem;
    }

    /// <summary>What the write did, or why it did nothing.</summary>
    public OkfCaptureWriteOutcome Outcome { get; }

    /// <summary>The entry the write concerned, or the empty string when none was identified.</summary>
    public string Id { get; }

    /// <summary>The manifest's new text, or <see langword="null" /> when nothing is to be written.</summary>
    public string? Text { get; }

    /// <summary>Why the write was refused, or <see langword="null" /> when it was not.</summary>
    public string? Problem { get; }

    /// <summary>Whether there is text to write.</summary>
    public bool IsWritten => Text is not null;

    internal static OkfCaptureWriteResult Written(OkfCaptureWriteOutcome outcome, string id, string text) =>
        new(outcome, id, text, null);

    internal static OkfCaptureWriteResult Refused(OkfCaptureWriteOutcome outcome, string problem, string id = "") =>
        new(outcome, id, null, problem);
}

/// <summary>
/// The write side of the capture manifest (AD-52): the code that owns <c>sha256</c>,
/// <c>capturedAt</c>, and the JSON mutation, so an agent writes meaning and never
/// structure.
/// </summary>
/// <remarks>
/// <para>Every edit is a <em>text splice</em> rather than a re-serialization, for the
/// reason <see cref="OkfStamp" /> edits a concept's text rather than re-emitting it: the
/// manifest is the immutability record, and a writer that reformats it hands a reviewer a
/// diff in which the one changed entry is invisible. So the document's bytes are located
/// with <see cref="Utf8JsonReader" /> — which reports where each token starts and ends —
/// and only the appended, replaced, or closed region is rewritten. Every other byte,
/// including whitespace, key order and the timestamp spellings already on disk, survives
/// exactly as found.</para>
/// <para>A manifest that does not parse, or that does not read as a manifest, is refused
/// with <see cref="OkfCaptureWriteOutcome.ManifestUnreadable" /> and left alone (AD-18): a
/// tool that repairs the immutability record is how the record is lost.</para>
/// </remarks>
public static class OkfCaptureWriter
{
    /// <summary>The manifest version this writer emits and requires (the capture skill's schema).</summary>
    public const int ManifestVersion = 1;

    /// <summary>
    /// An empty manifest: what <c>okf init</c> scaffolds, and what the first
    /// <c>okf capture add</c> in a vault without one starts from.
    /// </summary>
    public const string EmptyManifest = "{\n  \"manifestVersion\": 1,\n  \"captures\": []\n}\n";

    private const string Unreadable =
        "does not read as a capture manifest: a JSON object carrying `manifestVersion` 1 and a `captures` array. "
        + "It is left exactly as found — repairing the immutability record is how the record is lost (AD-18).";

    private static readonly JsonWriterOptions EntryWriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,

        // Fixed rather than inherited from the platform: the line ending an inserted entry
        // gets is the manifest's own, decided per file below, not the one the machine
        // running okf happens to prefer.
        NewLine = "\n",

        // The manifest is read by `check-manifest.py`, by a reviewer, and by nothing that
        // embeds it in HTML. The default encoder would spell a URL's `&` as `&`,
        // which is valid JSON that no hand-written entry in this repository looks like.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Appends a capture entry, or replaces an uningested one for the same item.
    /// </summary>
    /// <param name="manifestText">The manifest's current text.</param>
    /// <param name="rawDirectory">The absolute path of <c>&lt;vault&gt;/raw/</c>.</param>
    /// <param name="addition">The capture to record.</param>
    /// <returns>The new manifest text, or the refusal.</returns>
    /// <exception cref="ArgumentException">The capturing actor is not a well-formed §7 actor.</exception>
    /// <exception cref="IOException">A file of the item could not be read.</exception>
    public static OkfCaptureWriteResult Add(string manifestText, string rawDirectory, OkfCaptureAddition addition)
    {
        ArgumentNullException.ThrowIfNull(manifestText);
        ArgumentException.ThrowIfNullOrEmpty(rawDirectory);
        ArgumentNullException.ThrowIfNull(addition);
        ValidateActor(addition.CapturedBy, nameof(addition));

        (CapturedItem? item, string? problem) = Describe(rawDirectory, addition);
        if (item is null)
        {
            return OkfCaptureWriteResult.Refused(OkfCaptureWriteOutcome.ItemRefused, problem!);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(manifestText);
        if (Locate(bytes) is not { } manifest)
        {
            return OkfCaptureWriteResult.Refused(OkfCaptureWriteOutcome.ManifestUnreadable, Unreadable);
        }

        string entry = Render(item, addition);
        LocatedEntry? clash = manifest.Entries.FirstOrDefault(
            candidate => string.Equals(candidate.Id, item.Id, StringComparison.Ordinal)
                || candidate.Paths.Intersect(item.Paths, StringComparer.Ordinal).Any());

        if (clash is null)
        {
            return ReadBack(OkfCaptureWriteOutcome.Added, item.Id, Append(bytes, manifest, entry), false);
        }

        if (clash.IsIngested)
        {
            return OkfCaptureWriteResult.Refused(
                OkfCaptureWriteOutcome.AlreadyCaptured,
                $"`{clash.Id}` is already captured and ingested, so it is immutable (AD-18). New evidence is a "
                + "new capture under a new id, never an edit of this entry.",
                clash.Id);
        }

        // An uningested entry is the custodian's work queue and nothing has been built on
        // it yet, which is exactly what "freely re-capturable" means (AD-18).
        return ReadBack(
            OkfCaptureWriteOutcome.Recaptured,
            item.Id,
            Splice(bytes, clash.Start, clash.End, Reindent(entry, Indent(bytes, clash.Start), manifest.Newline)),
            false);
    }

    /// <summary>Closes an entry's <c>ingestion</c>.</summary>
    /// <param name="manifestText">The manifest's current text.</param>
    /// <param name="closure">The ingestion to record.</param>
    /// <returns>The new manifest text, or the refusal.</returns>
    /// <exception cref="ArgumentException">The actor is not a well-formed §7 actor, or no concept was named.</exception>
    public static OkfCaptureWriteResult Close(string manifestText, OkfCaptureClosure closure)
    {
        ArgumentNullException.ThrowIfNull(manifestText);
        ArgumentNullException.ThrowIfNull(closure);
        ValidateActor(closure.By, nameof(closure));
        if (closure.Concepts.Count == 0)
        {
            throw new ArgumentException("An ingestion names at least one concept.", nameof(closure));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(manifestText);
        if (Locate(bytes) is not { } manifest)
        {
            return OkfCaptureWriteResult.Refused(OkfCaptureWriteOutcome.ManifestUnreadable, Unreadable);
        }

        string wanted = closure.Entry.Replace('\\', '/').Trim('/');
        LocatedEntry? entry = manifest.Entries.FirstOrDefault(
            candidate => string.Equals(candidate.Id, wanted, StringComparison.Ordinal)
                || candidate.Paths.Contains(wanted, StringComparer.Ordinal));

        if (entry is null)
        {
            return OkfCaptureWriteResult.Refused(
                OkfCaptureWriteOutcome.NoSuchEntry,
                $"no capture entry has the id `{wanted}`, and none claims it as a path under raw/.");
        }

        if (entry.IsIngested)
        {
            return OkfCaptureWriteResult.Refused(
                OkfCaptureWriteOutcome.AlreadyClosed,
                $"`{entry.Id}` is already ingested. Closing is what starts immutability, so a second close would "
                + "rewrite the record of when the artifact froze.",
                entry.Id);
        }

        if (entry.IngestionStart is not { } start
            || entry.IngestionEnd is not { } end
            || entry.IngestionKey is not { } key)
        {
            return OkfCaptureWriteResult.Refused(
                OkfCaptureWriteOutcome.ManifestUnreadable,
                $"`{entry.Id}` carries no `ingestion` key, so there is nothing to close and this writer will not "
                + "add one: an entry that lost a required key is a record to resolve by hand.",
                entry.Id);
        }

        return ReadBack(
            OkfCaptureWriteOutcome.Closed,
            entry.Id,
            Splice(bytes, start, end, Reindent(RenderIngestion(closure), Indent(bytes, key), manifest.Newline)),
            true);
    }

    /// <summary>
    /// Writes manifest text to disk atomically: a sibling temporary file, then a rename
    /// over the target. A half-written immutability record is worse than none, and a
    /// rename within one directory is the operation that cannot leave one.
    /// </summary>
    /// <param name="path">The manifest's absolute path.</param>
    /// <param name="text">The text to write.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static void Save(string path, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(text);

        FileText.WriteAtomic(path, text);
    }

    /// <summary>
    /// Whether a capture id is the <c>&lt;YYYY-MM-DD&gt;-&lt;slug&gt;</c> the capture skill
    /// and <c>check-manifest.py</c> both require: a day that exists, then lowercase
    /// alphanumeric words joined by single hyphens.
    /// </summary>
    /// <param name="id">The candidate id.</param>
    /// <returns><see langword="true" /> when the id is well formed.</returns>
    public static bool IsValidId(string? id)
    {
        if (id is null || id.Length < 12 || id[10] != '-')
        {
            return false;
        }

        if (!DateOnly.TryParseExact(id[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }

        bool previousWasHyphen = true;
        foreach (char character in id[11..])
        {
            if (character == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (!char.IsAsciiDigit(character) && !char.IsAsciiLetterLower(character))
            {
                return false;
            }

            previousWasHyphen = false;
        }

        return !previousWasHyphen;
    }

    /// <summary>
    /// Holds a spliced manifest to the same terms <see cref="OkfStamp" /> holds a spliced
    /// concept to (AD-23): it is read back through the manifest reader, and the entry the
    /// write concerned has to be there in the state the write claims. A splice that
    /// produced something okf cannot read did not happen.
    /// </summary>
    private static OkfCaptureWriteResult ReadBack(
        OkfCaptureWriteOutcome outcome,
        string id,
        string text,
        bool expectIngested)
    {
        OkfCaptureEntry? entry = OkfCaptureManifest.Parse(text, "manifest.json")?.Captures
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

        return entry is not null && entry.IsIngested == expectIngested
            ? OkfCaptureWriteResult.Written(outcome, id, text)
            : OkfCaptureWriteResult.Refused(
                OkfCaptureWriteOutcome.ManifestUnreadable,
                $"the edit for `{id}` produced a manifest that does not read back as one, so nothing was written.",
                id);
    }

    private static void ValidateActor(string actor, string parameter)
    {
        if (!OkfActor.IsValid(actor))
        {
            throw new ArgumentException(
                $"'{actor}' is not an actor: §7 spells one `<producer>/<version>`, `human:<id>`, or `process:<id>`.",
                parameter);
        }
    }

    /// <summary>
    /// The item on disk, or the reason it is not one a capture entry can describe. The two
    /// travel together so a refusal is stated where the rule that produced it lives.
    /// </summary>
    private static (CapturedItem? Item, string? Problem) Describe(string rawDirectory, OkfCaptureAddition addition)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawDirectory));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(addition.ItemPath));
        string relative = Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return (null, $"'{full}' is not under '{root}'. A capture records an item already dropped into raw/, "
                + "which sits outside every bundle root (AD-17).");
        }

        bool isDirectory = Directory.Exists(full);
        if (!isDirectory && !File.Exists(full))
        {
            return (null, $"no such file or directory: '{full}'.");
        }

        if (IsLink(full, isDirectory))
        {
            return (null, Linked(relative));
        }

        OkfCaptureForm form = isDirectory ? OkfCaptureForm.Packet : OkfCaptureForm.Flat;
        if (addition.Form is { } asserted && asserted != form)
        {
            return (null, $"'{relative}' is {(isDirectory ? "a directory, which is a `packet`" : "a file, which is `flat`")}, "
                + $"and --form said `{Spell(asserted)}`. A packet is a directory and a flat capture is one file: the "
                + "form is the item's shape, not a label on it.");
        }

        if (relative.Contains('/', StringComparison.Ordinal))
        {
            return (null, $"'{relative}' is nested inside raw/. A capture is a file or a directory sitting directly "
                + "in raw/, because the entry's id is the item's own name.");
        }

        // `check-manifest.py` joins entry to disk through exactly this rule — a flat
        // capture's file is `<id>.<ext>`, a packet's files live under `<id>/` — so
        // deriving the id here is what keeps a CLI-written capture passing the gate that
        // reads it.
        string id = form == OkfCaptureForm.Flat ? Stem(relative) : relative;
        if (!IsValidId(id))
        {
            return (null, $"'{relative}' gives the capture id `{id}`, which is not `<YYYY-MM-DD>-<slug>`: a day that "
                + "exists, then lowercase words joined by single hyphens. Rename the item, because the id is its name.");
        }

        List<string> paths = new List<string>();
        if (form == OkfCaptureForm.Flat)
        {
            paths.Add(relative);
        }
        else
        {
            if (Collect(root, full, paths) is { } linked)
            {
                return (null, Linked(linked));
            }

            paths.Sort(StringComparer.Ordinal);
            if (paths.Count == 0)
            {
                return (null, $"'{relative}' holds no files, and a capture records at least one.");
            }
        }

        List<(string Path, string Sha256)> files = new List<(string Path, string Sha256)>();
        foreach (string path in paths)
        {
            files.Add((path, OkfCaptureManifest.Sha256Of(
                Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))));
        }

        return (new CapturedItem(id, form, files), null);
    }

    /// <summary>
    /// Collects a packet's files, refusing at the first symlink instead of walking through
    /// it. The walk is written out rather than left to <see cref="SearchOption.AllDirectories" />
    /// because that one descends a directory link, and a descent that leaves <c>raw/</c>
    /// records the digest of a file the vault does not hold under a path that says it does.
    /// The rule is the bundle walk's, tightened: okf never follows a link out of the root,
    /// and <c>raw/</c> refuses one even when it lands back inside.
    /// </summary>
    /// <returns>The raw-relative path of the first link found, or null when there is none.</returns>
    private static string? Collect(string root, string directory, List<string> paths)
    {
        foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            string relative = Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
            if (entry.LinkTarget is not null)
            {
                return relative;
            }

            if (entry is DirectoryInfo child)
            {
                if (Collect(root, child.FullName, paths) is { } linked)
                {
                    return linked;
                }

                continue;
            }

            paths.Add(relative);
        }

        return null;
    }

    /// <summary>
    /// Whether a path is a symlink or other reparse point. <c>LinkTarget</c> rather than the
    /// <see cref="FileAttributes.ReparsePoint" /> bit, because the bit is not set on a
    /// directory link on every platform and this is a containment check.
    /// </summary>
    private static bool IsLink(string path, bool isDirectory) =>
        (isDirectory ? new DirectoryInfo(path) : (FileSystemInfo)new FileInfo(path)).LinkTarget is not null;

    private static string Linked(string path) =>
        $"'{path}' is a symbolic link, not a captured artifact. raw/ holds the bytes that were retrieved, and a "
        + "link points at bytes that can be repointed.";

    private static string Spell(OkfCaptureForm form) => form == OkfCaptureForm.Flat ? "flat" : "packet";

    private static string Stem(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>Renders one capture entry, in the key order the capture skill documents.</summary>
    private static string Render(CapturedItem item, OkfCaptureAddition addition)
    {
        using MemoryStream buffer = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, EntryWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("id", item.Id);
            writer.WriteString("form", Spell(item.Form));

            writer.WriteStartArray("files");
            foreach ((string path, string sha256) in item.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", path);
                writer.WriteString("sha256", sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteString("capturedAt", OkfCanonicalTimestamp.ToCanonical(addition.CapturedAt));
            writer.WriteString("capturedBy", addition.CapturedBy);

            if (addition.OriginalUrl is { Length: > 0 } url)
            {
                writer.WriteString("originalUrl", url);
            }

            if (addition.Title is { Length: > 0 } title)
            {
                writer.WriteString("title", title);
            }

            if (addition.SourceLastModified is { Length: > 0 } modified)
            {
                writer.WriteString("sourceLastModified", modified);
            }

            // Always present and always null: `ingestion` is the work-queue flag, and
            // `okf capture close` needs a key to close rather than one to invent.
            writer.WriteNull("ingestion");
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string RenderIngestion(OkfCaptureClosure closure)
    {
        using MemoryStream buffer = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, EntryWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("at", OkfCanonicalTimestamp.ToCanonical(closure.At));
            writer.WriteString("by", closure.By);
            writer.WriteStartArray("concepts");
            foreach (string concept in closure.Concepts)
            {
                writer.WriteStringValue(concept);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Appends a rendered entry to the captures array, touching nothing else.</summary>
    private static string Append(byte[] bytes, LocatedManifest manifest, string entry)
    {
        if (manifest.Entries.Count > 0)
        {
            LocatedEntry last = manifest.Entries[^1];
            string indent = Indent(bytes, last.Start);
            return Splice(
                bytes,
                last.End,
                last.End,
                "," + manifest.Newline + indent + Reindent(entry, indent, manifest.Newline));
        }

        // An empty array is `[]` on one line, the shape `okf init` scaffolds, so the two
        // brackets are opened out rather than written between.
        string elementIndent = manifest.CapturesIndent + "  ";
        string body = manifest.Newline
            + elementIndent + Reindent(entry, elementIndent, manifest.Newline)
            + manifest.Newline + manifest.CapturesIndent;
        return Splice(bytes, manifest.ArrayStart + 1, manifest.ArrayEnd, body);
    }

    private static string Splice(byte[] bytes, int start, int end, string inserted) =>
        Encoding.UTF8.GetString(bytes, 0, start)
        + inserted
        + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);

    /// <summary>Shifts every line but the first of a rendered value to the target indent.</summary>
    private static string Reindent(string rendered, string indent, string newline) =>
        rendered.Replace("\n", newline + indent, StringComparison.Ordinal);

    /// <summary>The whitespace between the previous newline and a byte offset.</summary>
    private static string Indent(byte[] bytes, int offset)
    {
        int start = offset;
        while (start > 0 && bytes[start - 1] is (byte)' ' or (byte)'\t')
        {
            start--;
        }

        return start > 0 && bytes[start - 1] == (byte)'\n'
            ? Encoding.UTF8.GetString(bytes, start, offset - start)
            : "  ";
    }

    /// <summary>The line ending the manifest already uses, so an inserted entry is not the odd one out.</summary>
    private static string NewlineOf(byte[] bytes)
    {
        int first = Array.IndexOf(bytes, (byte)'\n');
        return first > 0 && bytes[first - 1] == (byte)'\r' ? "\r\n" : "\n";
    }

    /// <summary>
    /// Finds the captures array and every entry in it by byte offset, or returns
    /// <see langword="null" /> when the text does not read as a manifest.
    /// </summary>
    private static LocatedManifest? Locate(byte[] bytes)
    {
        try
        {
            Utf8JsonReader reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            int version = 0;
            LocatedManifest? manifest = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string? name = reader.GetString();
                int propertyStart = (int)reader.TokenStartIndex;
                if (!reader.Read())
                {
                    return null;
                }

                if (string.Equals(name, "manifestVersion", StringComparison.Ordinal)
                    && reader.TokenType == JsonTokenType.Number)
                {
                    version = reader.GetInt32();
                }
                else if (string.Equals(name, "captures", StringComparison.Ordinal)
                    && reader.TokenType == JsonTokenType.StartArray)
                {
                    manifest = ReadCaptures(ref reader, Indent(bytes, propertyStart), NewlineOf(bytes));
                    continue;
                }

                reader.TrySkip();
            }

            return version == ManifestVersion ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LocatedManifest ReadCaptures(ref Utf8JsonReader reader, string capturesIndent, string newline)
    {
        int arrayStart = (int)reader.TokenStartIndex;
        List<LocatedEntry> entries = new List<LocatedEntry>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                entries.Add(ReadEntry(ref reader));
            }
            else
            {
                reader.TrySkip();
            }
        }

        return new LocatedManifest(arrayStart, (int)reader.TokenStartIndex, capturesIndent, newline, entries);
    }

    private static LocatedEntry ReadEntry(ref Utf8JsonReader reader)
    {
        int start = (int)reader.TokenStartIndex;
        string id = string.Empty;
        List<string> paths = new List<string>();
        bool ingested = false;
        int? ingestionKey = null;
        int? ingestionStart = null;
        int? ingestionEnd = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string? name = reader.GetString();
            int keyStart = (int)reader.TokenStartIndex;
            reader.Read();

            if (string.Equals(name, "id", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.String)
            {
                id = reader.GetString() ?? string.Empty;
            }
            else if (string.Equals(name, "files", StringComparison.Ordinal)
                && reader.TokenType == JsonTokenType.StartArray)
            {
                ReadPaths(ref reader, paths);
                continue;
            }
            else if (string.Equals(name, "ingestion", StringComparison.Ordinal))
            {
                ingestionKey = keyStart;
                ingestionStart = (int)reader.TokenStartIndex;
                ingested = reader.TokenType == JsonTokenType.StartObject;
                reader.TrySkip();
                ingestionEnd = (int)reader.BytesConsumed;
                continue;
            }

            reader.TrySkip();
        }

        return new LocatedEntry(
            start,
            (int)reader.BytesConsumed,
            id,
            paths,
            ingested,
            ingestionKey,
            ingestionStart,
            ingestionEnd);
    }

    private static void ReadPaths(ref Utf8JsonReader reader, List<string> paths)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.TrySkip();
                continue;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string? name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "path", StringComparison.Ordinal)
                    && reader.TokenType == JsonTokenType.String
                    && reader.GetString() is { Length: > 0 } path)
                {
                    paths.Add(path);
                    continue;
                }

                reader.TrySkip();
            }
        }
    }

    private sealed record CapturedItem(
        string Id,
        OkfCaptureForm Form,
        IReadOnlyList<(string Path, string Sha256)> Files)
    {
        public IEnumerable<string> Paths => Files.Select(file => file.Path);
    }

    private sealed record LocatedManifest(
        int ArrayStart,
        int ArrayEnd,
        string CapturesIndent,
        string Newline,
        IReadOnlyList<LocatedEntry> Entries);

    private sealed record LocatedEntry(
        int Start,
        int End,
        string Id,
        IReadOnlyList<string> Paths,
        bool IsIngested,
        int? IngestionKey,
        int? IngestionStart,
        int? IngestionEnd);
}
