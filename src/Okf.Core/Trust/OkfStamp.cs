namespace Okf.Core.Trust;

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
    /// Renders one verification event as the flow mapping okf-net writes: the actor
    /// quoted, because every <c>human:</c> and <c>process:</c> actor carries a colon and a
    /// plain scalar holding one is ambiguous in flow context.
    /// </summary>
    /// <param name="actor">The verifying actor.</param>
    /// <param name="at">When the verification happened, in the canonical form (<see cref="OkfCanonicalTimestamp" />).</param>
    /// <returns>The YAML text of the event.</returns>
    public static string VerifiedEntry(string actor, DateTimeOffset at) => Entry(actor, at);

    /// <summary>
    /// Renders a generation stamp as the flow mapping okf-net writes — the same shape a
    /// verification event takes, because §5.2 gives both keys the same <c>{ by, at }</c>
    /// value.
    /// </summary>
    /// <param name="actor">The generating actor.</param>
    /// <param name="at">When the content was written, in the canonical form (<see cref="OkfCanonicalTimestamp" />).</param>
    /// <returns>The YAML text of the stamp.</returns>
    public static string GeneratedEntry(string actor, DateTimeOffset at) => Entry(actor, at);

    private static string Entry(string actor, DateTimeOffset at) =>
        $"{{ by: \"{actor}\", at: {OkfCanonicalTimestamp.ToCanonical(at)} }}";

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

        int before = OkfDocument.NormalizeVerified(OkfDocument.Parse(text).Frontmatter).Count;
        if (TryInsert(text, VerifiedEntry(actor, at)) is { } inserted
            && Accepts(inserted, actor, before))
        {
            return inserted;
        }

        string emitted = EmitVerified(text, actor, at);
        if (!Accepts(emitted, actor, before))
        {
            // The fallback is held to the same terms as the insertion. It is the less
            // travelled of the two paths, which is the argument for checking it rather
            // than against: frontmatter okf cannot read back is a corrupted concept
            // whichever path produced it, and refusing costs one parse.
            throw new OkfDocumentException(
                "Stamping produced frontmatter that does not read back as one more "
                + "verification event, so nothing was written.");
        }

        return emitted;
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

        EventList(document.Frontmatter).Add(EntryMapping(actor, at));
    }

    /// <summary>
    /// The <c>verified</c> sequence an event is appended to, created when the key is
    /// absent and — per §5.2, where a single verifier MAY be written as one mapping
    /// without the list dash — wrapping a bare mapping already there, because appending
    /// to that in place would silently drop the first verifier.
    /// </summary>
    private static OkfSequence EventList(OkfMapping frontmatter)
    {
        if (frontmatter.TryGetValue(VerifiedKey, out OkfValue? existing) && existing is OkfSequence sequence)
        {
            return sequence;
        }

        OkfSequence events = new OkfSequence { Style = OkfCollectionStyle.Block };
        if (existing is OkfMapping bare)
        {
            events.Add(bare);
        }

        // The indexer replaces in place when the key exists and appends when it does not,
        // so `verified` keeps whatever position the author gave it.
        frontmatter[VerifiedKey] = events;
        return events;
    }

    /// <summary>
    /// The <c>{ by, at }</c> mapping §5.2 gives both keys, as the model rather than as the
    /// text <see cref="Entry" /> renders.
    /// </summary>
    private static OkfMapping EntryMapping(string actor, DateTimeOffset at)
    {
        OkfMapping entry = new OkfMapping { Style = OkfCollectionStyle.Flow };
        entry.Add(OkfValue.Scalar("by"), OkfValue.Scalar(actor, OkfScalarStyle.DoubleQuoted));
        entry.Add(OkfValue.Scalar("at"), OkfValue.Scalar(OkfCanonicalTimestamp.ToCanonical(at)));
        return entry;
    }

    /// <summary>
    /// The emitter fallback for <see cref="VerifyText" />: parse, append, re-emit. Faithful
    /// in the sense CORE-2 requires but not byte-faithful, which is why the text insertion
    /// is tried first.
    /// </summary>
    private static string EmitVerified(string text, string actor, DateTimeOffset at)
    {
        OkfDocument document = OkfDocument.Parse(text);
        Verify(document, actor, at);
        return document.Serialize();
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

        string stamped = VerifyText(File.ReadAllText(path), actor, at);

        // UTF-8 with no byte-order mark, the same encoding `okf index` writes, so no
        // okf-written file ever grows one.
        File.WriteAllText(path, stamped, FileText.Utf8NoBom);
    }

    /// <summary>
    /// Writes or refreshes a concept's <c>generated</c> stamp in the file's text (§5.2),
    /// replacing whatever <c>{ by, at }</c> was there. Everything else — key order, the
    /// body, unknown producer keys, <c>verified</c> — is left byte for byte.
    /// </summary>
    /// <remarks>
    /// A fresh <c>generated.at</c> makes the concept unacknowledged again by AD-21, and
    /// that is the point: the stamp is the claim that the content was written now, so
    /// whatever acknowledgment preceded it no longer covers what is there.
    /// </remarks>
    /// <param name="text">The concept's full text, frontmatter and body.</param>
    /// <param name="actor">The generating actor, in one of §7's forms.</param>
    /// <param name="at">When the content was written.</param>
    /// <returns>The concept's text with the stamp written.</returns>
    /// <exception cref="ArgumentException">The actor is not a well-formed §7 actor.</exception>
    /// <exception cref="OkfDocumentException">The frontmatter does not parse, or the stamp did not read back.</exception>
    public static string StampGeneratedText(string text, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(text);
        Validate(actor);

        string stamp = OkfCanonicalTimestamp.ToCanonical(at);
        if (TryStampGenerated(text, GeneratedEntry(actor, at)) is { } inserted
            && AcceptsGenerated(inserted, actor, stamp))
        {
            return inserted;
        }

        string emitted = EmitGenerated(text, actor, at);
        if (!AcceptsGenerated(emitted, actor, stamp))
        {
            throw new OkfDocumentException(
                "Stamping produced frontmatter whose `generated` does not read back as the stamp just written, "
                + "so nothing was written.");
        }

        return emitted;
    }

    /// <summary>
    /// Sets a parsed document's <c>generated</c> stamp (§5.2). This is the model-level
    /// operation, for a library consumer holding a document; a caller working from a file
    /// wants <see cref="StampGeneratedText" />, which keeps the file's formatting.
    /// </summary>
    /// <param name="document">The document to stamp.</param>
    /// <param name="actor">The generating actor, in one of §7's forms.</param>
    /// <param name="at">When the content was written.</param>
    /// <exception cref="ArgumentException">The actor is not a well-formed §7 actor.</exception>
    public static void StampGenerated(OkfDocument document, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(actor);

        // The indexer replaces in place when the key exists and appends when it does not,
        // so `generated` keeps whatever position the author gave it.
        document.Frontmatter[GeneratedKey] = EntryMapping(actor, at);
    }

    /// <summary>
    /// The emitter fallback for <see cref="StampGeneratedText" />: parse, stamp, re-emit.
    /// </summary>
    private static string EmitGenerated(string text, string actor, DateTimeOffset at)
    {
        OkfDocument document = OkfDocument.Parse(text);
        StampGenerated(document, actor, at);
        return document.Serialize();
    }

    /// <summary>
    /// Reads a concept, writes its <c>generated</c> stamp, and writes it back — the whole
    /// filesystem side of <c>okf generated stamp</c>.
    /// </summary>
    /// <param name="path">The concept's absolute path.</param>
    /// <param name="actor">The generating actor.</param>
    /// <param name="at">When the content was written.</param>
    /// <exception cref="OkfDocumentException">The frontmatter does not parse.</exception>
    /// <exception cref="IOException">The file could not be read or written.</exception>
    public static void StampGeneratedFile(string path, string actor, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string stamped = StampGeneratedText(File.ReadAllText(path), actor, at);
        File.WriteAllText(path, stamped, FileText.Utf8NoBom);
    }

    /// <summary>
    /// Whether a stamped result is one okf can read back: it parses, and its
    /// <c>generated</c> is a mapping holding exactly the actor and instant just written.
    /// </summary>
    private static bool AcceptsGenerated(string text, string actor, string stamp)
    {
        try
        {
            return OkfDocument.Parse(text).Frontmatter.TryGetValue(GeneratedKey, out OkfValue? generated)
                && generated is OkfMapping mapping
                && mapping.TryGetValue("by", out OkfValue? by)
                && by is OkfScalar actorValue
                && string.Equals(actorValue.Value, actor, StringComparison.Ordinal)
                && mapping.TryGetValue("at", out OkfValue? at)
                && at is OkfScalar instant
                && string.Equals(instant.Value, stamp, StringComparison.Ordinal);
        }
        catch (OkfDocumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the <c>generated</c> stamp into the frontmatter text, or returns
    /// <see langword="null" /> when the key's shape is one an edit cannot reach. Two shapes
    /// are reached, and they are the two that occur: no <c>generated</c> key at all, and a
    /// one-line flow mapping. A block mapping under the key is left to the emitter, because
    /// replacing it means re-indenting somebody else's frontmatter.
    /// </summary>
    private static string? TryStampGenerated(string text, string entry)
    {
        if (FrontmatterText.Of(text) is not { } frontmatter)
        {
            return null;
        }

        if (frontmatter.Find(GeneratedKey) is not { } generated)
        {
            frontmatter.Insert(frontmatter.Fence, $"{GeneratedKey}: {entry}");
            return frontmatter.Joined();
        }

        if (generated.Region.Count > 0 || !IsFlowMapping(generated.Inline))
        {
            return null;
        }

        frontmatter.Replace(generated.Line, $"{GeneratedKey}: {entry}");
        return frontmatter.Joined();
    }

    /// <summary>Whether a value is a one-line flow mapping, the only shape an edit replaces in place.</summary>
    private static bool IsFlowMapping(string inline) =>
        inline.StartsWith('{') && inline.EndsWith('}');

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
            IReadOnlyList<OkfMapping> events = OkfDocument.NormalizeVerified(OkfDocument.Parse(text).Frontmatter);
            return events.Count == before + 1
                && events[^1].TryGetValue("by", out OkfValue? by)
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
        if (FrontmatterText.Of(text) is not { } frontmatter)
        {
            return null;
        }

        if (frontmatter.Find(VerifiedKey) is not { } verified)
        {
            return OpenListWithFirstEvent(frontmatter, entry);
        }

        if (verified.Inline.Length > 0)
        {
            return NormalizeBareMappingIntoList(frontmatter, verified, entry);
        }

        return verified.Region.Count == 0
            ? AppendEventUnderEmptyKey(frontmatter, verified, entry)
            : AppendEventToSequence(frontmatter, verified, entry);
    }

    /// <summary>No <c>verified</c> key at all: the key and its one event end the block.</summary>
    private static string OpenListWithFirstEvent(FrontmatterText frontmatter, string entry)
    {
        frontmatter.Insert(frontmatter.Fence, $"{VerifiedKey}:");
        frontmatter.Insert(frontmatter.Fence + 1, $"  - {entry}");
        return frontmatter.Joined();
    }

    /// <summary>
    /// §5.2's bare mapping, written inline. It becomes the list's first element and the new
    /// event its second, which is the normalization every consumer already performs when
    /// reading. A value that is not a one-line flow mapping is left to the emitter.
    /// </summary>
    private static string? NormalizeBareMappingIntoList(
        FrontmatterText frontmatter,
        FrontmatterKey verified,
        string entry)
    {
        if (verified.Region.Count > 0 || !IsFlowMapping(verified.Inline))
        {
            return null;
        }

        frontmatter.Replace(verified.Line, $"{VerifiedKey}:");
        frontmatter.Insert(verified.Line + 1, $"  - {verified.Inline}");
        frontmatter.Insert(verified.Line + 2, $"  - {entry}");
        return frontmatter.Joined();
    }

    /// <summary><c>verified:</c> with no value: a null the spec reads as no events.</summary>
    private static string AppendEventUnderEmptyKey(
        FrontmatterText frontmatter,
        FrontmatterKey verified,
        string entry)
    {
        frontmatter.Insert(verified.Line + 1, $"  - {entry}");
        return frontmatter.Joined();
    }

    /// <summary>
    /// A block sequence of events. Every line of the value belongs to it — a further item,
    /// or a continuation of one — so the new item goes after the last of them, at the
    /// indentation the author gave the first. A value that does not open with a list dash
    /// is a block-style bare mapping (<c>verified:</c> then <c>by:</c>/<c>at:</c> lines):
    /// turning that into a list means re-indenting somebody else's frontmatter, so the
    /// emitter can have it.
    /// </summary>
    private static string? AppendEventToSequence(
        FrontmatterText frontmatter,
        FrontmatterKey verified,
        string entry)
    {
        string first = frontmatter.LineAt(verified.Region[0]);
        if (!first.TrimStart().StartsWith("- ", StringComparison.Ordinal))
        {
            return null;
        }

        string indent = first[..(first.Length - first.TrimStart().Length)];
        frontmatter.Insert(verified.Region[^1] + 1, $"{indent}- {entry}");
        return frontmatter.Joined();
    }

    /// <summary>
    /// Where one top-level frontmatter key's value is written: the key's own line, the
    /// value on that line, and the non-blank lines the value spills onto below it.
    /// </summary>
    /// <param name="Line">The index of the key's line.</param>
    /// <param name="Inline">The value written after the colon, trimmed; empty when there is none.</param>
    /// <param name="Region">The indexes of the non-blank lines the value continues onto.</param>
    private readonly record struct FrontmatterKey(int Line, string Inline, List<int> Region);

    /// <summary>
    /// A concept's frontmatter block as the file's own lines, so an edit can put one line
    /// in and leave every other byte alone.
    /// </summary>
    /// <remarks>
    /// Each line keeps its own carriage return: split on <c>\n</c> and joined back with
    /// <c>\n</c>, the pieces reproduce the file byte for byte, so a line an edit did not
    /// touch keeps the ending its author gave it. Deciding one ending for the whole file
    /// from "is there a <c>\r</c> anywhere in it" would rewrite every line of an
    /// LF-terminated concept that happens to carry one stray carriage return in its body —
    /// an acknowledgment arriving as a whole-file diff, which is the thing text surgery
    /// exists to avoid. Every read here either trims or only inspects a prefix, so the
    /// retained <c>\r</c> changes no decision.
    /// </remarks>
    private sealed class FrontmatterText
    {
        private readonly List<string> _lines;
        private readonly string _carriage;

        private FrontmatterText(List<string> lines, int fence, string carriage)
        {
            _lines = lines;
            _carriage = carriage;
            Fence = fence;
        }

        /// <summary>The index of the closing <c>---</c>.</summary>
        public int Fence { get; }

        /// <summary>
        /// Locates the frontmatter block, or returns <see langword="null" /> when the text
        /// does not open with one or never closes it — either way, a shape no edit reaches.
        /// </summary>
        /// <param name="text">The concept's full text.</param>
        /// <returns>The block, or <see langword="null" />.</returns>
        public static FrontmatterText? Of(string text)
        {
            List<string> lines = text.Split('\n').ToList();
            if (lines.Count == 0 || lines[0].Trim() != OkfDocument.FrontmatterDelimiter)
            {
                return null;
            }

            // The ending inserted lines get: the one the opening fence carries, which is
            // the frontmatter's own convention rather than the file's most common one.
            string carriage = lines[0].EndsWith('\r') ? "\r" : string.Empty;

            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Trim() == OkfDocument.FrontmatterDelimiter)
                {
                    return new FrontmatterText(lines, i, carriage);
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a top-level key — one at column 0. A duplicate is not considered because
        /// the caller has already parsed the document, and YAML rejects one.
        /// </summary>
        /// <param name="key">The key to find.</param>
        /// <returns>Where its value is written, or <see langword="null" /> when absent.</returns>
        public FrontmatterKey? Find(string key)
        {
            int line = Enumerable.Range(1, Fence - 1)
                .FirstOrDefault(i => _lines[i].StartsWith(key + ":", StringComparison.Ordinal), -1);

            return line < 0
                ? null
                : new FrontmatterKey(line, _lines[line][(key.Length + 1)..].Trim(), ValueRegion(line));
        }

        public string LineAt(int index) => _lines[index];

        public void Insert(int index, string line) => _lines.Insert(index, line + _carriage);

        public void Replace(int index, string line) => _lines[index] = line + _carriage;

        /// <summary>The lines joined back into the file's text.</summary>
        /// <returns>The text.</returns>
        public string Joined() => string.Join('\n', _lines);

        private List<int> ValueRegion(int key)
        {
            // Where the value ends: the next top-level key, or the closing fence.
            int stop = Fence;
            for (int i = key + 1; i < Fence; i++)
            {
                if (_lines[i].Length > 0 && !char.IsWhiteSpace(_lines[i][0]))
                {
                    stop = i;
                    break;
                }
            }

            return Enumerable.Range(key + 1, stop - key - 1).Where(i => _lines[i].Trim().Length > 0).ToList();
        }
    }
}
