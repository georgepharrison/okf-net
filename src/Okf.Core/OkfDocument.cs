using System.Globalization;

namespace Okf.Core;

/// <summary>
/// An OKF v0.2 concept document: a YAML frontmatter block plus a markdown body
/// (§4). Ported from the reference implementation's
/// <c>reference_agent/bundle/document.py</c>.
/// </summary>
public sealed class OkfDocument
{
    /// <summary>The frontmatter fence, <c>---</c> on a line of its own (§4).</summary>
    public const string FrontmatterDelimiter = "---";

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

        var lines = SplitLines(text);
        if (lines.Count == 0 || !IsDelimiter(lines[0]))
        {
            return new OkfDocument(new OkfMapping(), text);
        }

        var end = -1;
        for (var i = 1; i < lines.Count; i++)
        {
            if (IsDelimiter(lines[i]))
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            throw new OkfDocumentException("Unterminated YAML frontmatter block");
        }

        var frontmatterText = string.Join('\n', lines.GetRange(1, end - 1));
        var loaded = YamlBridge.Load(frontmatterText);

        // The reference implementation writes `yaml.safe_load(fm_text) or {}`, so any
        // falsy document — null, `false`, `0`, `[]`, `{}`, "" — becomes empty
        // frontmatter, and only a *truthy* non-mapping is an error.
        OkfMapping frontmatter;
        if (loaded is null || !loaded.IsTruthy)
        {
            frontmatter = new OkfMapping();
        }
        else if (loaded is OkfMapping mapping)
        {
            frontmatter = mapping;
        }
        else
        {
            throw new OkfDocumentException("Frontmatter must be a YAML mapping");
        }

        var body = string.Join('\n', lines.GetRange(end + 1, lines.Count - end - 1));
        if (body.StartsWith('\n'))
        {
            body = body[1..];
        }

        return new OkfDocument(frontmatter, body);
    }

    /// <summary>
    /// Renders the document back to markdown: the frontmatter block, a blank line,
    /// then the body with a trailing newline.
    /// </summary>
    /// <returns>The serialized document.</returns>
    public string Serialize()
    {
        var frontmatterText = YamlBridge.Emit(Frontmatter).TrimEnd();
        var body = Body.EndsWith('\n') ? Body : Body + "\n";
        return $"{FrontmatterDelimiter}\n{frontmatterText}\n{FrontmatterDelimiter}\n\n{body}";
    }

    /// <summary>Checks OKF v0.2 §11 conformance for a single document.</summary>
    /// <exception cref="OkfDocumentException">A required frontmatter key is missing or empty.</exception>
    public void Validate()
    {
        var missing = RequiredFrontmatterKeys
            .Where(key => !(Frontmatter.TryGetValue(key, out var value) && value.IsTruthy))
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

        if (!frontmatter.TryGetValue("verified", out var verified) || (verified is OkfScalar s && s.IsNull))
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
        var events = NormalizeVerified(frontmatter);
        if (events.Count == 0)
        {
            return OkfTrustTier.Unverified;
        }

        foreach (var verification in events)
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

        if (!frontmatter.TryGetValue("stale_after", out var raw) || !raw.IsTruthy || raw is not OkfScalar scalar)
        {
            return false;
        }

        // Only the date part is considered, so a YAML-native date, an ISO date
        // string, and a datetime all compare identically (PRD CORE-7).
        var text = scalar.Value;
        var head = text.Length >= 10 ? text[..10] : text;
        if (!DateOnly.TryParseExact(head, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var staleAfter))
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
        verification.TryGetValue("by", out var by) && by is OkfScalar scalar && scalar.IsTruthy
            ? scalar.Value
            : string.Empty;

    private static bool IsDelimiter(string line) => line.Trim() == FrontmatterDelimiter;

    // Mirrors Python's str.splitlines() for the line terminators that occur in real
    // markdown (LF, CRLF, CR); the exotic ones Python also splits on (\v, \f, U+2028,
    // U+0085, ...) are deliberately treated as ordinary characters.
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                lines.Add(text[start..i]);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
            else if (text[i] == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }
}
