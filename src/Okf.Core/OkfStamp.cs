using System.Globalization;
using System.Text;

namespace Okf.Core;

/// <summary>
/// The stamp operations (PRD CORE-14): the only writes okf-net makes into a concept's
/// frontmatter. Each writes the fields decisions.md §7 permits and nothing else — key
/// order, unknown producer keys, and the body all survive untouched.
/// </summary>
/// <remarks>
/// <para>Stamping works on the file's <em>text</em> rather than on a re-emitted document,
/// and that is a deliberate choice rather than an optimization. A round trip through the
/// YAML emitter is faithful in the sense CORE-2 requires — same keys, same order, same
/// values — but not byte-faithful: it re-indents block sequences, closes up flow mappings,
/// and quotes a timestamp that sits inside one. Appending one acknowledgment would then
/// arrive as a diff touching every line of frontmatter, which is the opposite of what an
/// acknowledgment is.</para>
/// <para>So the append is a text insertion, and the emitter is the fallback for the
/// frontmatter shapes an insertion cannot safely reach. Either way the result is parsed
/// back and checked before it is returned: surgery that produced something okf cannot read
/// is surgery that did not happen.</para>
/// </remarks>
public static class OkfStamp
{
    /// <summary>The frontmatter key verification events live under (§5.2).</summary>
    public const string VerifiedKey = "verified";

    /// <summary>The frontmatter key generation stamps live under (§5.2).</summary>
    public const string GeneratedKey = "generated";

    /// <summary>
    /// Renders an instant the way okf-net writes one: UTC, to the second, with the
    /// <c>Z</c> designator. A stamp is a fact about when something happened, and a local
    /// offset makes two stamps from two machines look ordered when they are not.
    /// </summary>
    /// <param name="at">The instant.</param>
    /// <returns>The ISO-8601 text.</returns>
    public static string FormatTimestamp(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders one verification event as the flow mapping okf-net writes: the actor
    /// quoted, because every <c>human:</c> and <c>process:</c> actor carries a colon and a
    /// plain scalar holding one is ambiguous in flow context.
    /// </summary>
    /// <param name="actor">The verifying actor.</param>
    /// <param name="at">When the verification happened.</param>
    /// <returns>The YAML text of the event.</returns>
    public static string VerifiedEntry(string actor, DateTimeOffset at) =>
        $"{{ by: \"{actor}\", at: {FormatTimestamp(at)} }}";

    /// <summary>
    /// Appends a verification event to a concept's text (§5.2), normalizing a pre-existing
    /// bare <c>{ by, at }</c> mapping into a one-element list first. <c>generated</c> is
    /// never read and never written: verification acknowledges content, it does not rewrite
    /// it.
    /// </summary>
    /// <param name="text">The concept's full text, frontmatter and body.</param>
    /// <param name="actor">The verifying actor, in one of §7's forms.</param>
    /// <param name="at">When the verification happened.</param>
    /// <returns>The concept's text with the event appended.</returns>
    /// <exception cref="ArgumentException">The actor is not a well-formed §7 actor.</exception>
    /// <exception cref="OkfDocumentException">The frontmatter does not parse.</exception>
    public static string VerifyText(string text, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(text);
        Validate(actor);

        var before = OkfDocument.NormalizeVerified(OkfDocument.Parse(text).Frontmatter).Count;
        if (TryInsert(text, VerifiedEntry(actor, at)) is { } inserted
            && Accepts(inserted, actor, before))
        {
            return inserted;
        }

        var document = OkfDocument.Parse(text);
        Verify(document, actor, at);
        return document.Serialize();
    }

    /// <summary>
    /// Appends a verification event to a parsed document's <c>verified</c> list (§5.2),
    /// normalizing a pre-existing bare mapping into a one-element list first. This is the
    /// model-level operation, for a library consumer holding a document; a caller working
    /// from a file wants <see cref="VerifyText" />, which keeps the file's formatting.
    /// </summary>
    /// <param name="document">The document to stamp.</param>
    /// <param name="actor">The verifying actor, in one of §7's forms.</param>
    /// <param name="at">When the verification happened.</param>
    /// <exception cref="ArgumentException">The actor is not a well-formed §7 actor.</exception>
    public static void Verify(OkfDocument document, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(actor);

        var frontmatter = document.Frontmatter;
        OkfSequence events;
        if (frontmatter.TryGetValue(VerifiedKey, out var existing) && existing is OkfSequence sequence)
        {
            events = sequence;
        }
        else
        {
            events = new OkfSequence { Style = OkfCollectionStyle.Block };
            if (existing is OkfMapping bare)
            {
                // §5.2: a single verifier MAY be written as one mapping without the list
                // dash. Appending to it in place would silently drop the first verifier.
                events.Add(bare);
            }

            // The indexer replaces in place when the key exists and appends when it does
            // not, so `verified` keeps whatever position the author gave it.
            frontmatter[VerifiedKey] = events;
        }

        var entry = new OkfMapping { Style = OkfCollectionStyle.Flow };
        entry.Add(OkfValue.Scalar("by"), OkfValue.Scalar(actor, OkfScalarStyle.DoubleQuoted));
        entry.Add(OkfValue.Scalar("at"), OkfValue.Scalar(FormatTimestamp(at)));
        events.Add(entry);
    }

    /// <summary>
    /// Reads a concept, appends a verification event, and writes it back — the whole
    /// filesystem side of <c>okf verify</c>, so a caller wanting a dry run simply does not
    /// call it.
    /// </summary>
    /// <param name="path">The concept's absolute path.</param>
    /// <param name="actor">The verifying actor.</param>
    /// <param name="at">When the verification happened.</param>
    /// <exception cref="OkfDocumentException">The frontmatter does not parse.</exception>
    /// <exception cref="IOException">The file could not be read or written.</exception>
    public static void VerifyFile(string path, string actor, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var stamped = VerifyText(File.ReadAllText(path), actor, at);

        // UTF-8 with no byte-order mark, the same encoding `okf index` writes, so no
        // okf-written file ever grows one.
        File.WriteAllText(path, stamped, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void Validate(string actor)
    {
        if (!OkfActor.IsValid(actor))
        {
            throw new ArgumentException(
                $"'{actor}' is not an actor: §7 spells one `<producer>/<version>`, `human:<id>`, or `process:<id>`.",
                nameof(actor));
        }
    }

    /// <summary>
    /// Whether an inserted result is one okf can read back: it parses, it holds exactly one
    /// more verification event than before, and the last of them is the one just written.
    /// A text insertion that fails any of these is discarded for the emitter's output.
    /// </summary>
    private static bool Accepts(string text, string actor, int before)
    {
        try
        {
            var events = OkfDocument.NormalizeVerified(OkfDocument.Parse(text).Frontmatter);
            return events.Count == before + 1
                && events[^1].TryGetValue("by", out var by)
                && by is OkfScalar scalar
                && string.Equals(scalar.Value, actor, StringComparison.Ordinal);
        }
        catch (OkfDocumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Inserts a verification event into the frontmatter text, or returns
    /// <see langword="null" /> when the frontmatter's shape is one an insertion cannot
    /// reach. Three shapes are reached, and they are the three that occur: no
    /// <c>verified</c> key at all, a block sequence of events, and §5.2's single bare
    /// mapping written on one line.
    /// </summary>
    private static string? TryInsert(string text, string entry)
    {
        var newline = text.Contains('\r', StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        if (lines.Count == 0 || lines[0].Trim() != OkfDocument.FrontmatterDelimiter)
        {
            return null;
        }

        var fence = -1;
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].Trim() == OkfDocument.FrontmatterDelimiter)
            {
                fence = i;
                break;
            }
        }

        if (fence < 0)
        {
            return null;
        }

        // Top-level only: a `verified:` at column 0. A duplicate is not considered here
        // because the caller has already parsed the document, and YAML rejects one.
        var key = Enumerable.Range(1, fence - 1)
            .FirstOrDefault(i => lines[i].StartsWith(VerifiedKey + ":", StringComparison.Ordinal), -1);

        if (key < 0)
        {
            lines.Insert(fence, $"{VerifiedKey}:");
            lines.Insert(fence + 1, $"  - {entry}");
            return string.Join(newline, lines);
        }

        var inline = lines[key][(VerifiedKey.Length + 1)..].Trim();

        // Where the value ends: the next top-level key, or the closing fence.
        var stop = fence;
        for (var i = key + 1; i < fence; i++)
        {
            if (lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0]))
            {
                stop = i;
                break;
            }
        }

        var region = Enumerable.Range(key + 1, stop - key - 1).Where(i => lines[i].Trim().Length > 0).ToList();

        if (inline.Length > 0)
        {
            // §5.2's bare mapping, written inline. It becomes the list's first element and
            // the new event its second, which is the normalization every consumer already
            // performs when reading.
            if (region.Count > 0 || !inline.StartsWith('{') || !inline.EndsWith('}'))
            {
                return null;
            }

            lines[key] = $"{VerifiedKey}:";
            lines.Insert(key + 1, $"  - {inline}");
            lines.Insert(key + 2, $"  - {entry}");
            return string.Join(newline, lines);
        }

        if (region.Count == 0)
        {
            // `verified:` with no value: a null the spec reads as no events.
            lines.Insert(key + 1, $"  - {entry}");
            return string.Join(newline, lines);
        }

        var first = lines[region[0]];
        if (!first.TrimStart().StartsWith("- ", StringComparison.Ordinal))
        {
            // A block-style bare mapping (`verified:` then `by:`/`at:` lines). Turning it
            // into a list means re-indenting somebody else's frontmatter; the emitter can
            // have it.
            return null;
        }

        // Every later line of the region belongs to this sequence — a further item, or a
        // continuation of one — so the new item goes after the last of them, at the
        // indentation the author gave the first.
        var indent = first[..(first.Length - first.TrimStart().Length)];
        lines.Insert(region[^1] + 1, $"{indent}- {entry}");
        return string.Join(newline, lines);
    }
}
