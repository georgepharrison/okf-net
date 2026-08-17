using System.Text;

namespace Okf.Core.Search;

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

        List<OkfBundle> bundleList = bundles.ToList();
        if (query.IsEmpty)
        {
            return NothingSearched(query, bundleList.Count);
        }

        (List<Concept> corpus, int skipped) = ReadCorpus(bundleList, options);
        Ranking ranking = Rank(corpus, query, options.Limit);

        return new OkfSearchOutcome
        {
            Results = [.. ranking.Ranked.Select(
                scored => scored.Concept.ToResult(scored.Score, query.Terms, options.Today))],
            Query = query,
            MatchMode = ranking.Mode,
            TotalMatches = ranking.TotalMatches,
            ConceptCount = corpus.Count,
            BundleCount = bundleList.Count,
            SkippedCount = skipped,
        };
    }

    /// <summary>
    /// A query with neither terms nor filters asks for nothing, and "nothing" is not a
    /// synonym for "the whole vault": listing a bundle is what an index is for (CORE-10).
    /// Nothing is read.
    /// </summary>
    private static OkfSearchOutcome NothingSearched(OkfSearchQuery query, int bundleCount) => new OkfSearchOutcome
    {
        Results = [],
        Query = query,
        MatchMode = OkfSearchMatchMode.Filter,
        TotalMatches = 0,
        ConceptCount = 0,
        BundleCount = bundleCount,
        SkippedCount = 0,
    };

    /// <summary>
    /// Every concept in the bundles, parsed and indexed, plus how many files were skipped
    /// because their frontmatter does not parse. Reserved files are not concepts (spec
    /// §3.1); everything else in the tree is, which is what makes a foreign bundle
    /// searchable without cooperation.
    /// </summary>
    private static (List<Concept> Corpus, int Skipped) ReadCorpus(
        List<OkfBundle> bundles,
        OkfSearchOptions options)
    {
        List<Concept> corpus = new List<Concept>();
        int skipped = 0;

        foreach (OkfBundle bundle in bundles)
        {
            foreach (string file in bundle.MarkdownFiles().Where(file => !OkfBundle.IsReservedFile(file)))
            {
                string text = options.ReadText?.Invoke(file) ?? File.ReadAllText(file);

                // Only the parse is guarded. Indexing what parsed cannot raise this, and
                // if it ever did, counting it as an unreadable file would hide the bug.
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

        return (corpus, skipped);
    }

    /// <summary>
    /// Filters the corpus, scores what is left, and puts it in the total order results are
    /// reported in: score descending, then bundle root, then bundle-relative path, both
    /// ordinal. Collection statistics are taken over the whole corpus rather than over the
    /// filtered candidates, so a filter changes which concepts come back but never how the
    /// survivors rank.
    /// </summary>
    private static Ranking Rank(List<Concept> corpus, OkfSearchQuery query, int limit)
    {
        IReadOnlyList<string> terms = query.Terms;
        Statistics statistics = Statistics.Of(corpus, terms);
        List<Concept> candidates = corpus.Where(concept => concept.Passes(query)).ToList();

        (List<Concept> matched, OkfSearchMatchMode mode) = Match(candidates, terms);

        List<(Concept Concept, double Score)> ranked = matched
            .Select(concept => (Concept: concept, Score: Score(concept, terms, statistics)))
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Concept.Bundle.Root, StringComparer.Ordinal)
            .ThenBy(scored => scored.Concept.RelativePath, StringComparer.Ordinal)
            .ToList();

        return new Ranking(limit > 0 ? ranked.Take(limit).ToList() : ranked, ranked.Count, mode);
    }

    /// <summary>What ranking a corpus against a query produced.</summary>
    /// <param name="Ranked">The survivors in report order, already limited.</param>
    /// <param name="TotalMatches">How many matched before the limit was applied.</param>
    /// <param name="Mode">How they were matched.</param>
    private readonly record struct Ranking(
        List<(Concept Concept, double Score)> Ranked,
        int TotalMatches,
        OkfSearchMatchMode Mode);

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

        List<Concept> all = candidates.Where(concept => terms.All(concept.Matches)).ToList();
        if (all.Count > 0 || terms.Count == 1)
        {
            // With one term, AND and OR are the same query, so there is no fallback to
            // report and an empty result stays an empty AND result.
            return (all, OkfSearchMatchMode.All);
        }

        List<Concept> any = candidates.Where(concept => terms.Any(concept.Matches)).ToList();
        return any.Count > 0 ? (any, OkfSearchMatchMode.Any) : (all, OkfSearchMatchMode.All);
    }

    private static double Score(Concept concept, IReadOnlyList<string> terms, Statistics statistics)
    {
        double score = 0.0;
        foreach (string term in terms)
        {
            double frequency = concept.Weighted.GetValueOrDefault(term);
            if (frequency <= 0)
            {
                continue;
            }

            double normalization = K1 * (1 - B + (B * concept.Length / statistics.AverageLength));
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
        (string Text, bool Matched) body = Extract(concept.Body, matchedTerms);
        if (body.Matched)
        {
            return body.Text;
        }

        (string Text, bool Matched) description = Extract(concept.Description, matchedTerms);
        return description.Matched || description.Text.Length > 0 ? description.Text : body.Text;
    }

    private static (string Text, bool Matched) Extract(string? source, IReadOnlyList<string> terms)
    {
        string flat = Flatten(source);
        if (flat.Length == 0)
        {
            return (string.Empty, false);
        }

        List<(string Token, int Start, int Length)> tokens = OkfTokenizer.TokenizeWithOffsets(flat);
        HashSet<string> wanted = new HashSet<string>(terms, StringComparer.Ordinal);
        List<(string Token, int Start, int Length)> hits = tokens.Where(token => wanted.Contains(token.Token)).ToList();
        if (hits.Count == 0)
        {
            return (Head(flat), false);
        }

        SnippetWindow window = Window(flat, tokens, hits[BestAnchor(hits)]);
        return (Marked(flat, hits, window), true);
    }

    /// <summary>The span of flattened text a snippet shows.</summary>
    /// <param name="Start">The offset the window opens at.</param>
    /// <param name="End">The offset it closes at, exclusive.</param>
    private readonly record struct SnippetWindow(int Start, int End);

    /// <summary>The window built around one hit.</summary>
    private static SnippetWindow Window(
        string flat,
        List<(string Token, int Start, int Length)> tokens,
        (string Token, int Start, int Length) anchor)
    {
        int start = WindowStart(tokens, anchor);
        return new SnippetWindow(start, WindowEnd(flat, tokens, anchor, start));
    }

    /// <summary>
    /// Where the window opens: a little lead-in makes it readable, but never at the cost of
    /// a half word, so the start snaps forward to the next token boundary.
    /// </summary>
    private static int WindowStart(
        List<(string Token, int Start, int Length)> tokens,
        (string Token, int Start, int Length) anchor)
    {
        int start = Math.Max(0, anchor.Start - SnippetLeadIn);
        if (start == 0)
        {
            return 0;
        }

        (string Token, int Start, int Length) first = tokens.FirstOrDefault(token => token.Start >= start, anchor);
        return Math.Min(first.Start, anchor.Start);
    }

    /// <summary>
    /// Where the window closes: at the end of the last whole token inside the length limit,
    /// or at the limit itself when snapping back to a token would cut the anchor away.
    /// </summary>
    private static int WindowEnd(
        string flat,
        List<(string Token, int Start, int Length)> tokens,
        (string Token, int Start, int Length) anchor,
        int start)
    {
        int end = Math.Min(flat.Length, start + SnippetLength);
        if (end >= flat.Length)
        {
            return end;
        }

        (string Token, int Start, int Length) last = tokens.LastOrDefault(token => token.Start + token.Length <= end);
        int trimmed = last.Length > 0 ? last.Start + last.Length : end;

        // A token end is already a character boundary; the raw limit is not, so it is
        // snapped back off the tail of a surrogate pair.
        return trimmed > anchor.Start ? trimmed : SnapToCharacter(flat, end);
    }

    /// <summary>
    /// The window as the snippet reads it: every hit inside it wrapped in <c>**</c>, and an
    /// ellipsis at each end that was cut.
    /// </summary>
    private static string Marked(
        string flat,
        List<(string Token, int Start, int Length)> hits,
        SnippetWindow window)
    {
        StringBuilder builder = new StringBuilder();
        if (window.Start > 0)
        {
            builder.Append(Ellipsis);
        }

        AppendMarkedHits(builder, flat, hits, window);
        if (window.End < flat.Length)
        {
            builder.Append(Ellipsis);
        }

        return builder.ToString();
    }

    private static void AppendMarkedHits(
        StringBuilder builder,
        string flat,
        List<(string Token, int Start, int Length)> hits,
        SnippetWindow window)
    {
        int cursor = window.Start;
        foreach ((string Token, int Start, int Length) hit in hits)
        {
            if (hit.Start < cursor || hit.Start + hit.Length > window.End)
            {
                continue;
            }

            builder.Append(flat, cursor, hit.Start - cursor)
                .Append("**").Append(flat, hit.Start, hit.Length).Append("**");
            cursor = hit.Start + hit.Length;
        }

        builder.Append(flat, cursor, window.End - cursor);
    }

    /// <summary>
    /// Picks the hit to build the window around: the one whose window covers the most
    /// distinct matched terms, earliest first on a tie.
    /// </summary>
    private static int BestAnchor(List<(string Token, int Start, int Length)> hits)
    {
        int best = 0;
        int bestCount = 0;

        for (int i = 0; i < hits.Count; i++)
        {
            int end = hits[i].Start + SnippetLength;
            HashSet<string> distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int j = i; j < hits.Count && hits[j].Start + hits[j].Length <= end; j++)
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
        int limit = SnapToCharacter(flat, SnippetLength);
        int cut = limit;
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

        StringBuilder plain = new StringBuilder(text.Length);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int start = ContentStart(line);
            if (IsLinkReferenceDefinition(line, start))
            {
                continue;
            }

            AppendFlattened(plain, line, start);
            plain.Append(' ');
        }

        return TextWhitespace.Collapse(plain.ToString());
    }

    /// <summary>
    /// Where a line's prose begins: past its heading marker and past one leading block
    /// marker, with the spaces between them.
    /// </summary>
    /// <param name="line">The trimmed line.</param>
    /// <returns>The offset the content starts at.</returns>
    private static int ContentStart(string line)
    {
        int start = AfterHeadingMarker(line);
        while (start < line.Length && line[start] == ' ')
        {
            start++;
        }

        return start < line.Length && OpensBlock(line, start) ? start + 1 : start;
    }

    /// <summary>
    /// Where a heading's text begins — after up to six <c>#</c> followed by a space or by
    /// nothing — or zero when the line is not a heading.
    /// </summary>
    private static int AfterHeadingMarker(string line)
    {
        int hashes = 0;
        while (hashes < line.Length && line[hashes] == '#')
        {
            hashes++;
        }

        return hashes is > 0 and <= 6 && (hashes == line.Length || line[hashes] == ' ') ? hashes : 0;
    }

    /// <summary>
    /// Whether a block marker sits at the offset: a block quote's <c>&gt;</c>, or a list
    /// item's dash, star or plus followed by a space.
    /// </summary>
    private static bool OpensBlock(string line, int start) =>
        line[start] == '>'
        || (line[start] is '-' or '*' or '+' && start + 1 < line.Length && line[start + 1] == ' ');

    /// <summary>
    /// Whether the line is a link reference definition, <c>[label]: destination</c> — a
    /// line the snippet drops whole, because every character of it is address rather than
    /// prose and the address is what a snippet must not show. A footnote definition
    /// (<c>[^label]: …</c>) is deliberately not one: it is the note itself.
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

        int close = line.IndexOf(']', start + 1);
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
        for (int i = start; i < line.Length; i++)
        {
            char character = line[i];
            if (character == '*')
            {
                continue;
            }

            if (character is '[' or '!' && TryLink(line, i, out string? text, out int end))
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

        int open = line[index] == '!' ? index + 1 : index;
        if (!OpensLabel(line, open))
        {
            return false;
        }

        int close = MatchingBracket(line, open);
        if (close < 0 || close + 1 >= line.Length || TargetEnd(line, close) is not { } target)
        {
            return false;
        }

        text = line[(open + 1)..close];
        end = target;
        return true;
    }

    /// <summary>
    /// Whether a link label opens at the offset: a <c>[</c> that is not the <c>[^</c> of a
    /// footnote reference.
    /// </summary>
    private static bool OpensLabel(string line, int open) =>
        open < line.Length && line[open] == '[' && (open + 1 >= line.Length || line[open + 1] != '^');

    /// <summary>
    /// The offset of the <c>]</c> closing the label opened at <paramref name="open" />,
    /// counting nesting so an image inside a link does not close it early; -1 when the
    /// label never closes on this line.
    /// </summary>
    private static int MatchingBracket(string line, int open)
    {
        int depth = 0;
        for (int i = open; i < line.Length; i++)
        {
            if (line[i] == '[')
            {
                depth++;
            }
            else if (line[i] == ']' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The offset of the character closing the link's target — the <c>)</c> of an inline
    /// link, the <c>]</c> of a reference one — or <see langword="null" /> when what follows
    /// the label opens neither, or opens one that never closes.
    /// </summary>
    private static int? TargetEnd(string line, int close)
    {
        int target = line[close + 1] switch
        {
            '(' => line.IndexOf(')', close + 2),
            '[' => line.IndexOf(']', close + 2),
            _ => -1,
        };

        return target < 0 ? null : target;
    }

    /// <summary>The collection statistics BM25 needs, taken over the whole corpus.</summary>
    private sealed class Statistics
    {
        private readonly Dictionary<string, double> _idf = new(StringComparer.Ordinal);

        private Statistics(double averageLength) => AverageLength = averageLength;

        public double AverageLength { get; }

        public static Statistics Of(List<Concept> corpus, IReadOnlyList<string> terms)
        {
            double total = corpus.Sum(concept => concept.Length);
            double average = corpus.Count > 0 && total > 0 ? total / corpus.Count : 1;
            Statistics statistics = new Statistics(average);

            foreach (string term in terms)
            {
                double documentFrequency = (double)corpus.Count(concept => concept.Matches(term));

                // The non-negative IDF variant: a term in every document scores ~0 rather
                // than pushing the score down.
                statistics._idf[term] = Math.Log(
                    1 + ((corpus.Count - documentFrequency + 0.5) / (documentFrequency + 0.5)));
            }

            return statistics;
        }

        public double InverseDocumentFrequency(string term) => _idf.GetValueOrDefault(term);
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
            Type = FrontmatterValues.Scalar(frontmatter, "type");
            Description = FrontmatterValues.Scalar(frontmatter, "description");
            Tags = FrontmatterValues.Tags(frontmatter);
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

        /// <summary>The concept ID: the bundle-relative path without its <c>.md</c> suffix (spec §2).</summary>
        private string ConceptId => RelativePath.EndsWith(".md", StringComparison.Ordinal)
            ? RelativePath[..^3]
            : RelativePath;

        /// <summary>
        /// The frontmatter <c>title</c>, falling back to the filename stem when absent —
        /// the same fallback generated indexes use (PRD CORE-9).
        /// </summary>
        private string DisplayTitle => FrontmatterValues.Scalar(Frontmatter, "title") is { Length: > 0 } title
            ? title
            : System.IO.Path.GetFileNameWithoutExtension(Path);

        public static Concept Of(OkfBundle bundle, string path, OkfDocument document)
        {
            OkfMapping frontmatter = document.Frontmatter;
            Dictionary<string, double> weighted = new Dictionary<string, double>(StringComparer.Ordinal);
            double length = 0.0;

            Index(weighted, ref length, FrontmatterValues.Scalar(frontmatter, "title"), TitleWeight);
            Index(weighted, ref length, FrontmatterValues.Scalar(frontmatter, "type"), TypeWeight);
            Index(weighted, ref length, FrontmatterValues.Scalar(frontmatter, "description"), DescriptionWeight);
            foreach (string tag in FrontmatterValues.Tags(frontmatter))
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
            List<string> matchedTerms = terms.Where(Matches).Distinct(StringComparer.Ordinal).ToList();

            return new OkfSearchResult
            {
                Id = ConceptId,
                Path = RelativePath,
                AbsolutePath = Path,
                Bundle = Bundle.Root,
                BundleName = Bundle.Name,
                Title = DisplayTitle,
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
            foreach (string token in OkfTokenizer.Tokenize(text))
            {
                weighted[token] = weighted.GetValueOrDefault(token) + weight;
                length += weight;
            }
        }
    }
}
