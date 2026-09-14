using System.Globalization;

namespace Okf.Core.Documents;

/// <summary>
/// An OKF v0.2 concept document: a YAML frontmatter block plus a markdown body
/// (§4). Ported from the reference implementation's
/// <c>reference_agent/bundle/document.py</c>.
/// </summary>
public sealed class OkfDocument
{
    /// <summary>The frontmatter fence, <c>---</c> on a line of its own (§4).</summary>
    public const string FrontmatterDelimiter = "---";

    /// <summary>
    /// Why a file's frontmatter cannot be read at all (§4). Three causes, one answer: nothing
    /// about the file — not its <c>type</c>, not its <c>verified</c> history — can be
    /// established from what was read.
    /// </summary>
    /// <remarks>
    /// INTERNAL SURFACE. <c>Okf.Core</c> is non-packable and its public surface is frozen
    /// (architecture.md header), and #71 ruled that the corpus-reading machinery added for the
    /// candidate scanner is internal to Core while only the scanner's RESULT is public. This
    /// answers "was a frontmatter block found and read", which is a step of that machinery, not a
    /// verdict a consumer is allowed to re-derive: a CLI that asked this question itself would be
    /// a second authority on what a concept is, which is what AD-6 forbids.
    /// </remarks>
    /// <remarks>
    /// The causes are kept distinct because the fixes are distinct, and a report that printed
    /// one message for all three would send a person to the wrong line of the file. They are
    /// deliberately NOT the same question as "is the <c>verified</c> block readable", which
    /// <c>OkfVerificationHistory</c> answers for a file whose frontmatter parsed fine.
    /// </remarks>
    internal enum OkfFrontmatterRead
    {
        /// <summary>The fence opened, closed, and parsed to a mapping (§4).</summary>
        Readable,

        /// <summary>
        /// The first line is not the fence, so there is no frontmatter block to read. §4 makes
        /// the whole text body, which is what <see cref="Parse" /> does with it, and what
        /// §11.1 refuses to accept as a concept.
        /// </summary>
        NoFence,

        /// <summary>The fence opened and never closed, so the block's extent is unknown.</summary>
        Unterminated,

        /// <summary>The block is delimited but is not YAML, or is YAML that is not a mapping.</summary>
        Unparseable,
    }

    /// <summary>The <c>human:</c> actor prefix that marks human review (§7, §5.3).</summary>
    public const string HumanActorPrefix = "human:";

    /// <summary>
    /// The frontmatter keys every concept must carry. OKF v0.2 §11: <c>type</c> is the
    /// only always-required key.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredFrontmatterKeys = ["type"];

    /// <summary>Initializes an empty document.</summary>
    public OkfDocument()
        : this(new OkfMapping(), string.Empty)
    {
    }

    /// <summary>Initializes a document.</summary>
    /// <param name="frontmatter">The frontmatter mapping.</param>
    /// <param name="body">The markdown body.</param>
    public OkfDocument(OkfMapping frontmatter, string body)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);
        ArgumentNullException.ThrowIfNull(body);
        Frontmatter = frontmatter;
        Body = body;
    }

    /// <summary>The document's frontmatter, with unknown producer keys and key order preserved.</summary>
    public OkfMapping Frontmatter { get; }

    /// <summary>The document's markdown body.</summary>
    public string Body { get; set; }

    /// <summary>
    /// Splits markdown text into frontmatter and body (§4). Text whose first line is
    /// not the fence parses as empty frontmatter with the whole text as body.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="OkfDocumentException">
    /// The frontmatter block is unterminated, is not well-formed YAML, or is a
    /// non-empty non-mapping.
    /// </exception>
    public static OkfDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = SplitLines(text);
        if (lines.Count == 0 || !IsDelimiter(lines[0]))
        {
            return new OkfDocument(new OkfMapping(), text);
        }

        int end = ClosingDelimiterIndex(lines);
        if (end < 0)
        {
            throw new OkfDocumentException("Unterminated YAML frontmatter block");
        }

        return new OkfDocument(
            ParseFrontmatter(string.Join('\n', lines.GetRange(1, end - 1))),
            BodyAfter(lines, end));
    }

    /// <summary>
    /// Renders the document back to markdown: the frontmatter block, a blank line,
    /// then the body with a trailing newline.
    /// </summary>
    /// <returns>The serialized document.</returns>
    public string Serialize()
    {
        string frontmatterText = YamlBridge.Emit(Frontmatter).TrimEnd();
        string body = Body.EndsWith('\n') ? Body : Body + "\n";
        return $"{FrontmatterDelimiter}\n{frontmatterText}\n{FrontmatterDelimiter}\n\n{body}";
    }

    /// <summary>
    /// Asks whether a file's frontmatter can be read at all, and if not, why (§4). This is the
    /// question <see cref="Parse" /> refuses to answer: its leniency is its contract — text with
    /// no fence is body, which is what lets <c>okf lint</c> report a missing block as a finding
    /// about a file rather than as a parse crash, and what lets <c>okf search</c> index a
    /// fenceless document's prose. So this asks a second way, without touching that leniency.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>
    /// <see cref="OkfFrontmatterRead.Readable" /> exactly when <see cref="Parse" /> returns a
    /// document carrying frontmatter that came from a delimited block; otherwise the cause.
    /// </returns>
    /// <remarks>
    /// A falsy block — <c>---</c> then <c>---</c>, or a block holding only comments — is
    /// <see cref="OkfFrontmatterRead.Readable" />, because the reference implementation's
    /// <c>safe_load(fm_text) or {}</c> treats it as empty frontmatter and §11 then reports the
    /// missing <c>type</c>. This asks whether a readable block was FOUND, not whether it holds
    /// anything: those are different findings and <c>okf lint</c> already owns the second.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text" /> is null.</exception>
    internal static OkfFrontmatterRead FrontmatterReadOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = SplitLines(text);
        if (lines.Count == 0 || !IsDelimiter(lines[0]))
        {
            return OkfFrontmatterRead.NoFence;
        }

        int end = ClosingDelimiterIndex(lines);
        if (end < 0)
        {
            return OkfFrontmatterRead.Unterminated;
        }

        // One parse, and only its outcome is consulted: a second YAML reading of the same bytes
        // would be a second answer to "is this a mapping", and AD-6 puts that answer here.
        try
        {
            ParseFrontmatter(string.Join('\n', lines.GetRange(1, end - 1)));
            return OkfFrontmatterRead.Readable;
        }
        catch (OkfDocumentException)
        {
            return OkfFrontmatterRead.Unparseable;
        }
    }

    /// <summary>
    /// The cause-specific words for a frontmatter that cannot be read, as the finding a report
    /// prints. <see cref="OkfFrontmatterRead.Readable" /> has no message and throws rather than
    /// returning an empty string, because a caller that reaches it has a logic bug, not a file
    /// to name.
    /// </summary>
    /// <param name="read">The cause.</param>
    /// <returns>The message, without trailing punctuation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="read" /> is <see cref="OkfFrontmatterRead.Readable" />.</exception>
    internal static string MessageFor(OkfFrontmatterRead read) => read switch
    {
        OkfFrontmatterRead.NoFence => "File has no YAML frontmatter block",
        OkfFrontmatterRead.Unterminated => "Unterminated YAML frontmatter block",
        OkfFrontmatterRead.Unparseable => "Frontmatter does not parse",
        _ => throw new ArgumentOutOfRangeException(nameof(read), read, "A readable block has no message."),
    };

    /// <summary>Checks OKF v0.2 §11 conformance for a single document.</summary>
    /// <exception cref="OkfDocumentException">A required frontmatter key is missing or empty.</exception>
    public void Validate()
    {
        List<string> missing = RequiredFrontmatterKeys
            .Where(key => !(Frontmatter.TryGetValue(key, out OkfValue? value) && value.IsTruthy))
            .ToList();

        if (missing.Count > 0)
        {
            throw new OkfDocumentException($"Missing required frontmatter keys: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// Returns the <c>verified</c> events as a list (§5.2). A single verifier MAY be
    /// written as one <c>{ by, at }</c> mapping without the list dash; consumers MUST
    /// treat a bare mapping as a one-element list.
    /// </summary>
    /// <param name="frontmatter">The frontmatter to read.</param>
    /// <returns>The verification events; empty when there are none.</returns>
    public static IReadOnlyList<OkfMapping> NormalizeVerified(OkfMapping frontmatter)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);

        if (!frontmatter.TryGetValue("verified", out OkfValue? verified) || (verified is OkfScalar s && s.IsNull))
        {
            return [];
        }

        return verified switch
        {
            OkfMapping mapping => [mapping],
            OkfSequence sequence => sequence.OfType<OkfMapping>().ToArray(),
            _ => [],
        };
    }

    /// <summary>Derives a concept's trust tier from <c>verified</c> (§5.3).</summary>
    /// <param name="frontmatter">The frontmatter to read.</param>
    /// <returns>The derived tier.</returns>
    public static OkfTrustTier TrustTier(OkfMapping frontmatter)
    {
        IReadOnlyList<OkfMapping> events = NormalizeVerified(frontmatter);
        if (events.Count == 0)
        {
            return OkfTrustTier.Unverified;
        }

        foreach (OkfMapping verification in events)
        {
            if (Actor(verification).StartsWith(HumanActorPrefix, StringComparison.Ordinal))
            {
                return OkfTrustTier.HumanReviewed;
            }
        }

        return OkfTrustTier.MachineConfirmed;
    }

    /// <summary>
    /// Whether a concept is stale per <c>stale_after</c> (§5.5): stale when
    /// <c>today &gt;= stale_after</c>. An absent, empty, or unparseable value is never
    /// stale and never an error.
    /// </summary>
    /// <param name="frontmatter">The frontmatter to read.</param>
    /// <param name="today">The date to compare against — injected, never read from the clock here.</param>
    /// <returns><see langword="true" /> when the concept is stale.</returns>
    public static bool IsStale(OkfMapping frontmatter, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);

        if (!frontmatter.TryGetValue("stale_after", out OkfValue? raw) || !raw.IsTruthy || raw is not OkfScalar scalar)
        {
            return false;
        }

        // Only the date part is considered, so a YAML-native date, an ISO date
        // string, and a datetime all compare identically (PRD CORE-7).
        string text = scalar.Value;
        string head = text.Length >= 10 ? text[..10] : text;
        if (!DateOnly.TryParseExact(head, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly staleAfter))
        {
            return false;
        }

        return today >= staleAfter;
    }

    /// <summary>Whether a concept is stale as of a clock's current local date (§5.5).</summary>
    /// <param name="frontmatter">The frontmatter to read.</param>
    /// <param name="timeProvider">The clock supplying "today".</param>
    /// <returns><see langword="true" /> when the concept is stale.</returns>
    public static bool IsStale(OkfMapping frontmatter, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return IsStale(frontmatter, DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));
    }

    private static string Actor(OkfMapping verification) =>
        verification.TryGetValue("by", out OkfValue? by) && by is OkfScalar scalar && scalar.IsTruthy
            ? scalar.Value
            : string.Empty;

    private static bool IsDelimiter(string line) => line.Trim() == FrontmatterDelimiter;

    private static int ClosingDelimiterIndex(List<string> lines)
    {
        for (int i = 1; i < lines.Count; i++)
        {
            if (IsDelimiter(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static OkfMapping ParseFrontmatter(string frontmatterText)
    {
        OkfValue? loaded = YamlBridge.Load(frontmatterText);

        // The reference implementation writes `yaml.safe_load(fm_text) or {}`, so any
        // falsy document — null, `false`, `0`, `[]`, `{}`, "" — becomes empty
        // frontmatter, and only a *truthy* non-mapping is an error.
        if (loaded is null || !loaded.IsTruthy)
        {
            return new OkfMapping();
        }

        return loaded as OkfMapping
            ?? throw new OkfDocumentException("Frontmatter must be a YAML mapping");
    }

    private static string BodyAfter(List<string> lines, int frontmatterEndIndex)
    {
        string body = string.Join(
            '\n',
            lines.GetRange(frontmatterEndIndex + 1, lines.Count - frontmatterEndIndex - 1));

        return body.StartsWith('\n') ? body[1..] : body;
    }

    // Mirrors Python's str.splitlines() for the line terminators that occur in real
    // markdown (LF, CRLF, CR); the exotic ones Python also splits on (\v, \f, U+2028,
    // U+0085, ...) are deliberately treated as ordinary characters.
    private static List<string> SplitLines(string text)
    {
        List<string> lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int terminator = TerminatorLength(text, i);
            if (terminator == 0)
            {
                continue;
            }

            lines.Add(text[start..i]);
            i += terminator - 1;
            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    private static int TerminatorLength(string text, int index) => text[index] switch
    {
        '\r' => index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1,
        '\n' => 1,
        _ => 0,
    };
}
