using System.Text.RegularExpressions;

namespace Okf.Core;

/// <summary>A markdown link, with the 1-based file line it was found on.</summary>
/// <param name="Target">The link destination exactly as written.</param>
/// <param name="Line">The 1-based line in the file.</param>
internal sealed record MarkdownLink(string Target, int Line);

/// <summary>A footnote label, the join key into <c>sources[].id</c> (spec §5.1).</summary>
/// <param name="Label">The label, without the <c>^</c>.</param>
/// <param name="Line">The 1-based line in the file.</param>
/// <param name="IsDefinition">Whether this occurrence is the <c>[^label]: ...</c> definition.</param>
internal sealed record MarkdownFootnote(string Label, int Line, bool IsDefinition);

/// <summary>An ATX heading.</summary>
/// <param name="Level">The number of leading <c>#</c> characters.</param>
/// <param name="Text">The heading text.</param>
/// <param name="Line">The 1-based line in the file.</param>
internal sealed record MarkdownHeading(int Level, string Text, int Line);

/// <summary>A list bullet.</summary>
/// <param name="Text">The bullet's text, without the marker.</param>
/// <param name="Line">The 1-based line in the file.</param>
internal sealed record MarkdownBullet(string Text, int Line);

/// <summary>Everything the linter needs from a markdown body.</summary>
internal sealed class MarkdownScan
{
    /// <summary>The inline and reference-definition links found outside code fences.</summary>
    public List<MarkdownLink> Links { get; } = [];

    /// <summary>The footnote references and definitions found outside code fences.</summary>
    public List<MarkdownFootnote> Footnotes { get; } = [];

    /// <summary>The ATX headings found outside code fences.</summary>
    public List<MarkdownHeading> Headings { get; } = [];

    /// <summary>The list bullets found outside code fences.</summary>
    public List<MarkdownBullet> Bullets { get; } = [];

    /// <summary>Whether the body has any content beyond whitespace.</summary>
    public bool HasContent { get; set; }
}

/// <summary>
/// A deliberately small markdown reader: enough structure for the lint rules (links,
/// footnote labels, headings, bullets) and nothing more. Fenced code blocks are skipped
/// so a SQL sample carrying bracket syntax never produces a diagnostic.
/// </summary>
internal static partial class MarkdownScanner
{
    /// <summary>Scans a markdown body.</summary>
    /// <param name="body">The body text.</param>
    /// <param name="firstLineNumber">The 1-based file line the body's first line sits on.</param>
    /// <returns>The scan results, with file line numbers.</returns>
    public static MarkdownScan Scan(string body, int firstLineNumber)
    {
        var scan = new MarkdownScan();
        var lines = body.Split('\n');
        var fence = (string?)null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var lineNumber = firstLineNumber + index;
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                var marker = trimmed[..3];
                if (fence is null)
                {
                    fence = marker;
                }
                else if (string.Equals(fence, marker, StringComparison.Ordinal))
                {
                    fence = null;
                }

                continue;
            }

            if (fence is not null)
            {
                continue;
            }

            if (trimmed.Length > 0)
            {
                scan.HasContent = true;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                // A heading is scanned for links and footnote labels like any other line:
                // CommonMark allows both inline, and stopping here made a `[^id]` written
                // in a heading invisible, which let OKF0102 call a genuinely cited source
                // uncited on a bundle okf-net did not produce (PRD ACC-1).
                scan.Headings.Add(new MarkdownHeading(
                    heading.Groups["hashes"].Value.Length,
                    heading.Groups["text"].Value.Trim(),
                    lineNumber));
            }

            var bullet = BulletRegex().Match(line);
            if (bullet.Success)
            {
                scan.Bullets.Add(new MarkdownBullet(bullet.Groups["text"].Value.Trim(), lineNumber));
            }

            foreach (var match in FootnoteRegex().Matches(line).Cast<Match>())
            {
                // A definition is `[^label]:` opening a line. The colon is what makes it
                // one: `[^label]` alone on its own line is a reference — the form Google's
                // ga4 bundle uses to cite a source under a SQL block — and counting it as
                // a definition would let OKF0102 call a genuinely cited source uncited.
                var isDefinition = line[..match.Index].Trim().Length == 0
                    && match.Index + match.Length < line.Length
                    && line[match.Index + match.Length] == ':';
                scan.Footnotes.Add(new MarkdownFootnote(match.Groups["label"].Value, lineNumber, isDefinition));
            }

            foreach (var match in InlineLinkRegex().Matches(line).Cast<Match>())
            {
                var destination = match.Groups["dest"].Value;
                if (destination.Length > 0)
                {
                    scan.Links.Add(new MarkdownLink(Unbracket(destination), lineNumber));
                }
            }

            var definition = LinkDefinitionRegex().Match(line);
            if (definition.Success)
            {
                scan.Links.Add(new MarkdownLink(Unbracket(definition.Groups["dest"].Value), lineNumber));
            }
        }

        return scan;
    }

    private static string Unbracket(string destination) =>
        destination.Length >= 2 && destination[0] == '<' && destination[^1] == '>'
            ? destination[1..^1]
            : destination;

    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})(?<text>\s.*|)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^ {0,3}(?:[*+-]|\d{1,9}[.)])\s+(?<text>.*)$")]
    private static partial Regex BulletRegex();

    // A footnote reference or definition label (§5.1). `\s` is excluded so a stray
    // `[^` in prose cannot swallow a line.
    [GeneratedRegex(@"\[\^(?<label>[^\]\s]+)\]")]
    private static partial Regex FootnoteRegex();

    // Inline links and images. The destination stops at whitespace so an optional title
    // (`[t](d "title")`) is not mistaken for part of the path.
    [GeneratedRegex(@"(?<!\])\[(?!\^)(?<text>[^\[\]]*)\]\(\s*(?<dest><[^>\s]*>|[^()\s]*)(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)")]
    private static partial Regex InlineLinkRegex();

    // A link reference definition, `[label]: destination`. Footnote definitions are
    // excluded by the `[^` guard: they carry prose, not a path.
    [GeneratedRegex(@"^ {0,3}\[(?!\^)(?<label>[^\]]+)\]:\s*(?<dest><[^>\s]*>|\S+)")]
    private static partial Regex LinkDefinitionRegex();
}
