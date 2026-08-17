using System.Text;

namespace Okf.Core.Search;

/// <summary>
/// The tokenizer both the corpus and the query go through (PRD CORE-11, decisions.md Q7).
/// </summary>
/// <remarks>
/// <para>The rule is deliberately small enough to restate in one sentence: lowercase the
/// text and split it on everything that is not a letter or a digit. It is Unicode-aware —
/// enumeration is by <see cref="Rune" />, so a surrogate pair is one character and not
/// two — and case folding is <c>ToLowerInvariant</c>, which is full Unicode simple case
/// mapping even under the CLI's <c>InvariantGlobalization</c>.</para>
/// <para>A hyphenated word therefore splits on the hyphen, in the corpus and in the query
/// alike: <c>sqlite-vec</c> is the two terms <c>sqlite</c> and <c>vec</c> in both, so it
/// still matches. There is no stemming, no stopword list, and no minimum token length in
/// v1 — a deliberate omission, recorded in decisions.md: a stemmer is per-language state
/// that a <c>cat</c>-readable format should not need, and the first thing another
/// implementation of the format's tooling would have to reproduce exactly.</para>
/// </remarks>
public static class OkfTokenizer
{
    /// <summary>Splits text into search tokens.</summary>
    /// <param name="text">The text to tokenize; <see langword="null" /> yields no tokens.</param>
    /// <returns>The tokens, lowercased, in order of appearance.</returns>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        var builder = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                Append(builder, rune);
            }
            else if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Splits text into tokens, keeping where each one started so a snippet can mark the
    /// matched words in the original text.
    /// </summary>
    /// <param name="text">The text to tokenize.</param>
    /// <returns>Each token with its start offset and length in <paramref name="text" />.</returns>
    internal static List<(string Token, int Start, int Length)> TokenizeWithOffsets(string text)
    {
        var tokens = new List<(string, int, int)>();
        var builder = new StringBuilder();
        var start = 0;
        var index = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var width = rune.Utf16SequenceLength;
            if (Rune.IsLetterOrDigit(rune))
            {
                if (builder.Length == 0)
                {
                    start = index;
                }

                Append(builder, rune);
            }
            else if (builder.Length > 0)
            {
                tokens.Add((builder.ToString(), start, index - start));
                builder.Clear();
            }

            index += width;
        }

        if (builder.Length > 0)
        {
            tokens.Add((builder.ToString(), start, index - start));
        }

        return tokens;
    }

    private static void Append(StringBuilder builder, Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        var written = Rune.ToLowerInvariant(rune).EncodeToUtf16(buffer);
        builder.Append(buffer[..written]);
    }
}

/// <summary>
/// A parsed search query: the bare terms that score, plus the <c>type:</c> and <c>tag:</c>
/// filters that restrict which concepts may be scored at all (decisions.md Q7).
/// </summary>
/// <remarks>
/// Filters are compared case-insensitively against the whole frontmatter value, never
/// against tokens, so <c>type:Reference</c> and <c>type:reference</c> are one filter and
/// <c>tag:cost-optimization</c> never matches a concept tagged <c>cost</c>. Repetition
/// folds by the arity of the field: repeated <c>type:</c> is OR (a concept has exactly one
/// <c>type</c>, so AND-ing two would always return nothing) and repeated <c>tag:</c> is
/// AND (a concept has many tags, so a second tag filter narrows, which is what it is for).
/// </remarks>
public sealed class OkfSearchQuery
{
    /// <summary>The prefix that marks a tag filter in query text.</summary>
    public const string TagPrefix = "tag:";

    /// <summary>The prefix that marks a type filter in query text.</summary>
    public const string TypePrefix = "type:";

    private readonly List<string> terms = [];
    private readonly List<string> types = [];
    private readonly List<string> tags = [];

    /// <summary>Initializes an empty query.</summary>
    public OkfSearchQuery()
    {
    }

    /// <summary>The bare terms, tokenized and lowercased, in the order they were written.</summary>
    public IReadOnlyList<string> Terms => this.terms;

    /// <summary>The <c>type:</c> filters, in the order they were written (OR-ed).</summary>
    public IReadOnlyList<string> Types => this.types;

    /// <summary>The <c>tag:</c> filters, in the order they were written (AND-ed).</summary>
    public IReadOnlyList<string> Tags => this.tags;

    /// <summary>
    /// Whether the query asks for nothing at all — no terms and no filters. The CLI treats
    /// this as a usage failure rather than as "every concept" (PRD CLI-14, exit 2).
    /// </summary>
    public bool IsEmpty => this.terms.Count == 0 && this.types.Count == 0 && this.tags.Count == 0;

    /// <summary>
    /// Parses query text: whitespace-separated words, where a word beginning <c>tag:</c> or
    /// <c>type:</c> (in any case) is a filter and everything else is tokenized into terms.
    /// A filter with an empty value contributes nothing.
    /// </summary>
    /// <param name="text">The query text.</param>
    /// <returns>The parsed query.</returns>
    public static OkfSearchQuery Parse(string? text)
    {
        var query = new OkfSearchQuery();
        if (string.IsNullOrWhiteSpace(text))
        {
            return query;
        }

        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                query.AddTagFilter(word[TagPrefix.Length..]);
            }
            else if (word.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase))
            {
                query.AddTypeFilter(word[TypePrefix.Length..]);
            }
            else
            {
                query.terms.AddRange(OkfTokenizer.Tokenize(word));
            }
        }

        return query;
    }

    /// <summary>
    /// Adds a <c>type:</c> filter, as <c>--type</c> does. Blank values and duplicates
    /// (case-insensitively) are ignored.
    /// </summary>
    /// <param name="value">The type to restrict to.</param>
    public void AddTypeFilter(string? value) => Add(this.types, value);

    /// <summary>
    /// Adds a <c>tag:</c> filter, as <c>--tag</c> does. Blank values and duplicates
    /// (case-insensitively) are ignored.
    /// </summary>
    /// <param name="value">The tag to require.</param>
    public void AddTagFilter(string? value) => Add(this.tags, value);

    /// <summary>
    /// Renders the query in its canonical form — terms first, then <c>type:</c> and
    /// <c>tag:</c> filters — for messages and <c>--verbose</c> output.
    /// </summary>
    /// <returns>The canonical query text.</returns>
    public override string ToString() => string.Join(
        ' ',
        this.terms
            .Concat(this.types.Select(type => TypePrefix + type))
            .Concat(this.tags.Select(tag => TagPrefix + tag)));

    private static void Add(List<string> values, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || values.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        values.Add(trimmed);
    }
}
