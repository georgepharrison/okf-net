using System.Text;

namespace Okf.Core;

/// <summary>How a result set was matched (decisions.md Q7).</summary>
public enum OkfSearchMatchMode
{
    /// <summary>Every query term matched every result.</summary>
    All = 0,

    /// <summary>
    /// No concept matched every term, so the search fell back to matching any of them.
    /// </summary>
    Any,

    /// <summary>
    /// The query carried filters but no terms, so every concept passing the filters is a
    /// result and nothing was scored.
    /// </summary>
    Filter,
}

/// <summary>
/// One ranked concept match (PRD CORE-11). Deliberately links-first: it points at a
/// concept and says enough to judge it — never its body (decisions.md Q7).
/// </summary>
public sealed class OkfSearchResult
{
    /// <summary>The concept ID: the bundle-relative path without its <c>.md</c> suffix (spec §2).</summary>
    public required string Id { get; init; }

    /// <summary>The bundle-relative path, with <c>/</c> separators.</summary>
    public required string Path { get; init; }

    /// <summary>The concept's absolute path.</summary>
    public required string AbsolutePath { get; init; }

    /// <summary>The absolute root of the bundle the concept lives in.</summary>
    public required string Bundle { get; init; }

    /// <summary>The bundle's directory name.</summary>
    public required string BundleName { get; init; }

    /// <summary>
    /// The frontmatter <c>title</c>, falling back to the filename stem when absent — the
    /// same fallback generated indexes use (PRD CORE-9).
    /// </summary>
    public required string Title { get; init; }

    /// <summary>The frontmatter <c>type</c>, or <see langword="null" /> when absent.</summary>
    public required string? Type { get; init; }

    /// <summary>The frontmatter <c>description</c>, or <see langword="null" /> when absent.</summary>
    public required string? Description { get; init; }

    /// <summary>The frontmatter <c>tags</c>, in order; empty when absent.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// The relevance score: opaque, higher is better, and zero for a filter-only query.
    /// Nothing in the contract says how it is computed, so a different engine can supply
    /// it later (decisions.md Q7).
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// A bounded one-line extract with the matched terms marked <c>**like this**</c>: the
    /// best body window, or the <c>description</c> when the match was frontmatter-only.
    /// </summary>
    public required string Snippet { get; init; }

    /// <summary>The derived trust tier (§5.3, PRD CORE-6).</summary>
    public required OkfTrustTier TrustTier { get; init; }

    /// <summary>Whether the concept is stale (§5.5, PRD CORE-7).</summary>
    public required bool Stale { get; init; }

    /// <summary>The query terms this concept matched, in query order.</summary>
    public required IReadOnlyList<string> MatchedTerms { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Path} ({Score.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})";
}

/// <summary>The outcome of one search: the ranked results plus what was searched to get them.</summary>
public sealed class OkfSearchOutcome
{
    /// <summary>The results, ranked and already limited.</summary>
    public required IReadOnlyList<OkfSearchResult> Results { get; init; }

    /// <summary>The query that produced them, filters included.</summary>
    public required OkfSearchQuery Query { get; init; }

    /// <summary>How the results were matched.</summary>
    public required OkfSearchMatchMode MatchMode { get; init; }

    /// <summary>How many concepts matched before <see cref="OkfSearchOptions.Limit" /> was applied.</summary>
    public required int TotalMatches { get; init; }

    /// <summary>How many concepts were searched.</summary>
    public required int ConceptCount { get; init; }

    /// <summary>How many bundles were searched.</summary>
    public required int BundleCount { get; init; }

    /// <summary>
    /// How many files were skipped because their frontmatter does not parse. They are
    /// already <c>OKF0001</c> errors; <c>okf lint</c> is the surface that reports them.
    /// </summary>
    public required int SkippedCount { get; init; }

    /// <summary>
    /// Whether an AND search matched nothing and the OR fallback produced these results.
    /// Commands say so in their output rather than silently widening the query.
    /// </summary>
    public bool UsedFallback => MatchMode == OkfSearchMatchMode.Any;

    /// <summary>Whether results were dropped by the limit.</summary>
    public bool Truncated => TotalMatches > Results.Count;
}

/// <summary>Everything <see cref="OkfSearchEngine" /> needs beyond the bundles and the query.</summary>
public sealed class OkfSearchOptions
{
    /// <summary>
    /// How many results to return at most. Zero or negative means no limit. The CLI
    /// defaults to 10 (PRD CLI-11).
    /// </summary>
    public int Limit { get; set; } = 10;

    /// <summary>
    /// The date staleness is judged against (§5.5). Injected, never read from the clock
    /// here, so a search is deterministic (PRD CORE-7, ACC-7).
    /// </summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Supplies a file's text. Returning <see langword="null" /> falls back to reading the
    /// file — the seam a caller with a warm cache (the MCP server, a future incremental
    /// index) uses to avoid a second read.
    /// </summary>
    public Func<string, string?>? ReadText { get; set; }
}

/// <summary>
/// Deterministic lexical search over bundle trees (PRD CORE-11; decisions.md Q7). It lives
/// in <c>Okf.Core</c> so <c>okf search</c> and the MCP <c>search</c> tool are two renderings
/// of one result set (decisions.md §4, PRD MCP-3).
/// </summary>
/// <remarks>
/// <para><b>Corpus.</b> Every concept in the resolved bundles: the spec §3.1 reserved files
/// (<c>index.md</c>, <c>log.md</c>) are excluded because an index is a view of the concepts
/// and indexing it would rank navigation above content, and <c>raw/</c> is never reached
/// because it lives outside every bundle root (decisions.md Q3). A file whose frontmatter
/// does not parse is not a corpus entry; it is already an <c>OKF0001</c> error.</para>
/// <para><b>Ranking.</b> BM25 over field-weighted term frequencies — title ×3, <c>tags</c>
/// ×2, <c>type</c> ×2, <c>description</c> ×2, body ×1 — with the standard parameters
/// <c>k1 = 1.2</c> and <c>b = 0.75</c>, and the non-negative IDF variant so a term present
/// in most of the corpus can never subtract from a score. Collection statistics are taken
/// over the whole corpus rather than the filtered candidates, so a filter changes which
/// concepts come back but never how the survivors rank.</para>
/// <para><b>Determinism.</b> No clock, no network, no model call, and a total order on
/// results: score descending, then bundle root, then bundle-relative path, both ordinal.
/// The same tree and the same query always produce the same bytes (PRD ACC-7).</para>
/// </remarks>
public static class OkfSearchEngine
{
    /// <summary>BM25's term-frequency saturation parameter.</summary>
    public const double K1 = 1.2;

    /// <summary>BM25's length-normalization parameter.</summary>
    public const double B = 0.75;

    /// <summary>The weight given to tokens from the frontmatter <c>title</c>.</summary>
    public const double TitleWeight = 3;

    /// <summary>The weight given to tokens from the frontmatter <c>tags</c>.</summary>
    public const double TagWeight = 2;

    /// <summary>
    /// The weight given to tokens from the frontmatter <c>type</c>. It sits with
    /// <see cref="TagWeight" /> because PRD CORE-11 requires <c>type</c> to be matchable
    /// and it is the same kind of categorical metadata.
    /// </summary>
    public const double TypeWeight = 2;

    /// <summary>The weight given to tokens from the frontmatter <c>description</c>.</summary>
    public const double DescriptionWeight = 2;

    /// <summary>The weight given to tokens from the markdown body.</summary>
    public const double BodyWeight = 1;

    /// <summary>How many characters a snippet spans at most.</summary>
    public const int SnippetLength = 160;

    /// <summary>How many characters of lead-in a snippet keeps before its first match.</summary>
    private const int SnippetLeadIn = 32;

    /// <summary>The character marking a snippet that was cut at either end.</summary>
    private const char Ellipsis = '…';

    /// <summary>Searches a set of bundles.</summary>
    /// <param name="bundles">The bundles to search, in the order they were resolved.</param>
    /// <param name="query">The parsed query.</param>
    /// <param name="options">The limit, the staleness date, and where to read files from.</param>
    /// <returns>The ranked results and what was searched to get them.</returns>
    /// <exception cref="IOException">A file in a bundle could not be read.</exception>
    public static OkfSearchOutcome Search(
        IEnumerable<OkfBundle> bundles,
        OkfSearchQuery query,
        OkfSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(query);
        options ??= new OkfSearchOptions();

        var bundleList = bundles.ToList();

        if (query.IsEmpty)
        {
            // A query with neither terms nor filters asks for nothing, and "nothing" is not
            // a synonym for "the whole vault": listing a bundle is what an index is for
            // (CORE-10). Nothing is read.
            return new OkfSearchOutcome
            {
                Results = [],
                Query = query,
                MatchMode = OkfSearchMatchMode.Filter,
                TotalMatches = 0,
                ConceptCount = 0,
                BundleCount = bundleList.Count,
                SkippedCount = 0,
            };
        }

        var skipped = 0;
        var corpus = new List<Concept>();

        foreach (var bundle in bundleList)
        {
            foreach (var file in bundle.MarkdownFiles())
            {
                // Reserved files are not concepts (spec §3.1); everything else in the tree
                // is, which is what makes a foreign bundle searchable without cooperation.
                if (OkfBundle.IsReservedFile(file))
                {
                    continue;
                }

                var text = options.ReadText?.Invoke(file) ?? File.ReadAllText(file);
                OkfDocument document;
                try
                {
                    document = OkfDocument.Parse(text);
                }
                catch (OkfDocumentException)
                {
                    skipped++;
                    continue;
                }

                corpus.Add(Concept.Of(bundle, file, document));
            }
        }

        var terms = query.Terms;
        var statistics = Statistics.Of(corpus, terms);
        var candidates = corpus.Where(concept => concept.Passes(query)).ToList();

        var (matched, mode) = Match(candidates, terms);

        var ranked = matched
            .Select(concept => (Concept: concept, Score: Score(concept, terms, statistics)))
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Concept.Bundle.Root, StringComparer.Ordinal)
            .ThenBy(scored => scored.Concept.RelativePath, StringComparer.Ordinal)
            .ToList();

        var limited = options.Limit > 0 ? ranked.Take(options.Limit) : ranked;

        return new OkfSearchOutcome
        {
            Results = [.. limited.Select(scored => scored.Concept.ToResult(scored.Score, terms, options.Today))],
            Query = query,
            MatchMode = mode,
            TotalMatches = ranked.Count,
            ConceptCount = corpus.Count,
            BundleCount = bundleList.Count,
            SkippedCount = skipped,
        };
    }

    private static (List<Concept> Matched, OkfSearchMatchMode Mode) Match(
        List<Concept> candidates,
        IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            // Filters with no terms are a listing, not a search: everything that passes is
            // a result, and nothing is scored.
            return (candidates, OkfSearchMatchMode.Filter);
        }

        var all = candidates.Where(concept => terms.All(concept.Matches)).ToList();
        if (all.Count > 0 || terms.Count == 1)
        {
            // With one term, AND and OR are the same query, so there is no fallback to
            // report and an empty result stays an empty AND result.
            return (all, OkfSearchMatchMode.All);
        }

        var any = candidates.Where(concept => terms.Any(concept.Matches)).ToList();
        return any.Count > 0 ? (any, OkfSearchMatchMode.Any) : (all, OkfSearchMatchMode.All);
    }

    private static double Score(Concept concept, IReadOnlyList<string> terms, Statistics statistics)
    {
        var score = 0.0;
        foreach (var term in terms)
        {
            var frequency = concept.Weighted.GetValueOrDefault(term);
            if (frequency <= 0)
            {
                continue;
            }

            var normalization = K1 * (1 - B + (B * concept.Length / statistics.AverageLength));
            score += statistics.InverseDocumentFrequency(term) * frequency * (K1 + 1) / (frequency + normalization);
        }

        return score;
    }

    /// <summary>
    /// The best snippet for a concept: the body window covering the most distinct matched
    /// terms, or the description when the match was frontmatter-only.
    /// </summary>
    private static string Snippet(Concept concept, IReadOnlyList<string> matchedTerms)
    {
        var body = Extract(concept.Body, matchedTerms);
        if (body.Matched)
        {
            return body.Text;
        }

        var description = Extract(concept.Description, matchedTerms);
        return description.Matched || description.Text.Length > 0 ? description.Text : body.Text;
    }

    private static (string Text, bool Matched) Extract(string? source, IReadOnlyList<string> terms)
    {
        var flat = Flatten(source);
        if (flat.Length == 0)
        {
            return (string.Empty, false);
        }

        var tokens = OkfTokenizer.TokenizeWithOffsets(flat);
        var wanted = new HashSet<string>(terms, StringComparer.Ordinal);
        var hits = tokens.Where(token => wanted.Contains(token.Token)).ToList();
        if (hits.Count == 0)
        {
            return (Head(flat), false);
        }

        var anchor = BestAnchor(hits);

        // A little lead-in makes the window readable, but never at the cost of a half
        // word: the start snaps forward to the next token boundary.
        var start = Math.Max(0, hits[anchor].Start - SnippetLeadIn);
        if (start > 0)
        {
            var first = tokens.FirstOrDefault(token => token.Start >= start, hits[anchor]);
            start = Math.Min(first.Start, hits[anchor].Start);
        }

        var end = Math.Min(flat.Length, start + SnippetLength);
        if (end < flat.Length)
        {
            var last = tokens.LastOrDefault(token => token.Start + token.Length <= end);
            var trimmed = last.Length > 0 ? last.Start + last.Length : end;

            // A token end is already a character boundary; the raw limit is not, so it is
            // snapped back off the tail of a surrogate pair.
            end = trimmed > hits[anchor].Start ? trimmed : SnapToCharacter(flat, end);
        }

        var builder = new StringBuilder();
        if (start > 0)
        {
            builder.Append(Ellipsis);
        }

        var cursor = start;
        foreach (var hit in hits)
        {
            if (hit.Start < cursor || hit.Start + hit.Length > end)
            {
                continue;
            }

            builder.Append(flat, cursor, hit.Start - cursor)
                .Append("**").Append(flat, hit.Start, hit.Length).Append("**");
            cursor = hit.Start + hit.Length;
        }

        builder.Append(flat, cursor, end - cursor);
        if (end < flat.Length)
        {
            builder.Append(Ellipsis);
        }

        return (builder.ToString(), true);
    }

    /// <summary>
    /// Picks the hit to build the window around: the one whose window covers the most
    /// distinct matched terms, earliest first on a tie.
    /// </summary>
    private static int BestAnchor(List<(string Token, int Start, int Length)> hits)
    {
        var best = 0;
        var bestCount = 0;

        for (var i = 0; i < hits.Count; i++)
        {
            var end = hits[i].Start + SnippetLength;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            for (var j = i; j < hits.Count && hits[j].Start + hits[j].Length <= end; j++)
            {
                distinct.Add(hits[j].Token);
            }

            if (distinct.Count > bestCount)
            {
                best = i;
                bestCount = distinct.Count;
            }
        }

        return best;
    }

    private static string Head(string flat) =>
        flat.Length <= SnippetLength ? flat : TrimToWord(flat) + Ellipsis;

    private static string TrimToWord(string flat)
    {
        var limit = SnapToCharacter(flat, SnippetLength);
        var cut = limit;
        while (cut > 0 && !char.IsWhiteSpace(flat[cut]))
        {
            cut--;
        }

        // A word longer than the whole window — an unspaced script, a long URL — leaves no
        // boundary to cut back to, so the window is cut at its own limit instead.
        return (cut == 0 ? flat[..limit] : flat[..cut]).TrimEnd();
    }

    /// <summary>
    /// Moves a cut offset back off the tail of a surrogate pair, so a window never ends
    /// between the two halves of one character. Tokenization is by <see cref="Rune" />
    /// (decisions.md Q7), so every token boundary already satisfies this; only the raw
    /// <see cref="SnippetLength" /> limit can land inside a character.
    /// </summary>
    /// <param name="text">The text being cut.</param>
    /// <param name="offset">The proposed cut offset.</param>
    /// <returns>The offset, moved back by one when it splits a surrogate pair.</returns>
    private static int SnapToCharacter(string text, int offset) =>
        offset > 0 && offset < text.Length && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1])
            ? offset - 1
            : offset;

    /// <summary>
    /// Reduces markdown to the one line a snippet is made of: heading markers, one leading
    /// list or block-quote marker per line, every emphasis asterisk, and every link target
    /// are dropped, and runs of whitespace collapse to one space. Only the snippet is
    /// transformed — the concept file is untouched and scoring reads the raw body.
    /// Asterisks go because the snippet marks its own matches with <c>**</c>, and a body
    /// that already carries bold would otherwise render as <c>****term****</c>. Link
    /// targets go for the same reason one step further out: a window that lands inside
    /// <c>[text](target)</c> emits half a link, and marking a term inside a URL
    /// (<c>[The **custodian** model](**custodian**-model.md)</c>) is both unreadable and
    /// invalid markdown. The link *text* is prose and stays.
    /// </summary>
    private static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var plain = new StringBuilder(text.Length);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var start = 0;

            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
            {
                hashes++;
            }

            if (hashes is > 0 and <= 6 && (hashes == line.Length || line[hashes] == ' '))
            {
                start = hashes;
            }

            while (start < line.Length && line[start] == ' ')
            {
                start++;
            }

            if (start < line.Length
                && (line[start] == '>'
                    || (line[start] is '-' or '*' or '+' && start + 1 < line.Length && line[start + 1] == ' ')))
            {
                start++;
            }

            if (IsLinkReferenceDefinition(line, start))
            {
                // `[label]: ../path.md` is address, not prose: every character of it is
                // the target a snippet must not show. A footnote definition
                // (`[^label]: …`) is the opposite — it is the note itself — and is kept.
                continue;
            }

            AppendFlattened(plain, line, start);
            plain.Append(' ');
        }

        var builder = new StringBuilder(plain.Length);
        var pendingSpace = false;
        foreach (var character in plain.ToString())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the line is a link reference definition, <c>[label]: destination</c>. A
    /// footnote definition is deliberately not one: it carries prose.
    /// </summary>
    /// <param name="line">The trimmed line.</param>
    /// <param name="start">Where the line's content begins.</param>
    /// <returns><see langword="true" /> when the whole line is a link address.</returns>
    private static bool IsLinkReferenceDefinition(string line, int start)
    {
        if (start >= line.Length || line[start] != '[' || (start + 1 < line.Length && line[start + 1] == '^'))
        {
            return false;
        }

        var close = line.IndexOf(']', start + 1);
        return close > start + 1 && close + 1 < line.Length && line[close + 1] == ':';
    }

    /// <summary>
    /// Appends one line's content with emphasis asterisks and link targets removed. A
    /// link is recognized only in its complete single-line form — <c>[text](target)</c>,
    /// <c>[text][label]</c>, and the image forms with a leading <c>!</c> — so a lone
    /// bracket in prose, a footnote reference (<c>[^id]</c>), and a link split across
    /// lines all survive as written rather than being guessed at.
    /// </summary>
    /// <param name="plain">The buffer to append to.</param>
    /// <param name="line">The trimmed line.</param>
    /// <param name="start">Where the line's content begins, after its block markers.</param>
    private static void AppendFlattened(StringBuilder plain, string line, int start)
    {
        for (var i = start; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '*')
            {
                continue;
            }

            if (character is '[' or '!' && TryLink(line, i, out var text, out var end))
            {
                // The text is flattened in turn: it may carry emphasis, and a nested
                // image (`[![alt](img)](href)`) is a link inside a link.
                AppendFlattened(plain, text, 0);
                i = end;
                continue;
            }

            plain.Append(character);
        }
    }

    /// <summary>
    /// Matches a complete inline or reference link starting at <paramref name="index" />.
    /// </summary>
    /// <param name="line">The line being read.</param>
    /// <param name="index">The offset of the <c>[</c>, or of the <c>!</c> of an image.</param>
    /// <param name="text">The link's text, when one was matched.</param>
    /// <param name="end">The offset of the link's last character, when one was matched.</param>
    /// <returns><see langword="true" /> when a link was matched.</returns>
    private static bool TryLink(string line, int index, out string text, out int end)
    {
        text = string.Empty;
        end = index;

        var open = line[index] == '!' ? index + 1 : index;
        if (open >= line.Length || line[open] != '[' || (open + 1 < line.Length && line[open + 1] == '^'))
        {
            return false;
        }

        var depth = 0;
        var close = -1;
        for (var i = open; i < line.Length; i++)
        {
            if (line[i] == '[')
            {
                depth++;
            }
            else if (line[i] == ']' && --depth == 0)
            {
                close = i;
                break;
            }
        }

        if (close < 0 || close + 1 >= line.Length)
        {
            return false;
        }

        var opener = line[close + 1];
        var closer = opener switch { '(' => ')', '[' => ']', _ => '\0' };
        if (closer == '\0')
        {
            return false;
        }

        var target = line.IndexOf(closer, close + 2);
        if (target < 0)
        {
            return false;
        }

        text = line[(open + 1)..close];
        end = target;
        return true;
    }

    private static string? Scalar(OkfMapping frontmatter, string key) =>
        frontmatter.TryGetValue(key, out var value) && value is OkfScalar scalar && scalar.IsTruthy
            ? scalar.Value
            : null;

    private static IReadOnlyList<string> TagValues(OkfMapping frontmatter)
    {
        if (!frontmatter.TryGetValue("tags", out var value))
        {
            return [];
        }

        return value switch
        {
            // A scalar `tags` is one tag, not a list to guess a separator for.
            OkfScalar scalar when scalar.IsTruthy => [scalar.Value],
            OkfSequence sequence =>
                [.. sequence.OfType<OkfScalar>().Where(item => item.IsTruthy).Select(item => item.Value)],
            _ => [],
        };
    }

    /// <summary>The collection statistics BM25 needs, taken over the whole corpus.</summary>
    private sealed class Statistics
    {
        private readonly Dictionary<string, double> idf = new(StringComparer.Ordinal);

        private Statistics(double averageLength) => AverageLength = averageLength;

        public double AverageLength { get; }

        public static Statistics Of(List<Concept> corpus, IReadOnlyList<string> terms)
        {
            var total = corpus.Sum(concept => concept.Length);
            var average = corpus.Count > 0 && total > 0 ? total / corpus.Count : 1;
            var statistics = new Statistics(average);

            foreach (var term in terms)
            {
                var documentFrequency = (double)corpus.Count(concept => concept.Matches(term));

                // The non-negative IDF variant: a term in every document scores ~0 rather
                // than pushing the score down.
                statistics.idf[term] = Math.Log(
                    1 + ((corpus.Count - documentFrequency + 0.5) / (documentFrequency + 0.5)));
            }

            return statistics;
        }

        public double InverseDocumentFrequency(string term) => this.idf.GetValueOrDefault(term);
    }

    /// <summary>One corpus entry: a parsed concept plus its weighted term frequencies.</summary>
    private sealed class Concept
    {
        private Concept(
            OkfBundle bundle,
            string path,
            OkfMapping frontmatter,
            string body,
            Dictionary<string, double> weighted,
            double length)
        {
            Bundle = bundle;
            Path = path;
            RelativePath = bundle.RelativePath(path);
            Frontmatter = frontmatter;
            Body = body;
            Weighted = weighted;
            Length = length;
            Type = Scalar(frontmatter, "type");
            Description = Scalar(frontmatter, "description");
            Tags = TagValues(frontmatter);
        }

        public OkfBundle Bundle { get; }

        public string Path { get; }

        public string RelativePath { get; }

        public OkfMapping Frontmatter { get; }

        public string Body { get; }

        public string? Type { get; }

        public string? Description { get; }

        public IReadOnlyList<string> Tags { get; }

        public Dictionary<string, double> Weighted { get; }

        public double Length { get; }

        public static Concept Of(OkfBundle bundle, string path, OkfDocument document)
        {
            var frontmatter = document.Frontmatter;
            var weighted = new Dictionary<string, double>(StringComparer.Ordinal);
            var length = 0.0;

            Index(weighted, ref length, Scalar(frontmatter, "title"), TitleWeight);
            Index(weighted, ref length, Scalar(frontmatter, "type"), TypeWeight);
            Index(weighted, ref length, Scalar(frontmatter, "description"), DescriptionWeight);
            foreach (var tag in TagValues(frontmatter))
            {
                Index(weighted, ref length, tag, TagWeight);
            }

            Index(weighted, ref length, document.Body, BodyWeight);

            return new Concept(bundle, path, frontmatter, document.Body, weighted, length);
        }

        public bool Matches(string term) => Weighted.ContainsKey(term);

        /// <summary>
        /// Whether the concept survives the query's filters: any of the <c>type:</c>
        /// filters, and all of the <c>tag:</c> filters (decisions.md Q7).
        /// </summary>
        public bool Passes(OkfSearchQuery query)
        {
            if (query.Types.Count > 0
                && !query.Types.Any(type => string.Equals(type, Type?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return query.Tags.All(
                wanted => Tags.Any(tag => string.Equals(tag.Trim(), wanted, StringComparison.OrdinalIgnoreCase)));
        }

        public OkfSearchResult ToResult(double score, IReadOnlyList<string> terms, DateOnly today)
        {
            var matchedTerms = terms.Where(Matches).Distinct(StringComparer.Ordinal).ToList();

            return new OkfSearchResult
            {
                Id = RelativePath.EndsWith(".md", StringComparison.Ordinal)
                    ? RelativePath[..^3]
                    : RelativePath,
                Path = RelativePath,
                AbsolutePath = Path,
                Bundle = Bundle.Root,
                BundleName = Bundle.Name,
                Title = Scalar(Frontmatter, "title") is { Length: > 0 } title
                    ? title
                    : System.IO.Path.GetFileNameWithoutExtension(Path),
                Type = Type,
                Description = Description,
                Tags = Tags,
                Score = Math.Round(score, 4, MidpointRounding.AwayFromZero),
                Snippet = Snippet(this, matchedTerms),
                TrustTier = OkfDocument.TrustTier(Frontmatter),
                Stale = OkfDocument.IsStale(Frontmatter, today),
                MatchedTerms = matchedTerms,
            };
        }

        private static void Index(
            Dictionary<string, double> weighted,
            ref double length,
            string? text,
            double weight)
        {
            foreach (var token in OkfTokenizer.Tokenize(text))
            {
                weighted[token] = weighted.GetValueOrDefault(token) + weight;
                length += weight;
            }
        }
    }
}
