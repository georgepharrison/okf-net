namespace Okf.Core;

/// <summary>
/// Where a markdown file's frontmatter and body sit in its lines, so a diagnostic can
/// name the line a key or a link is on (PRD CLI-15). This is line bookkeeping only;
/// <see cref="OkfDocument.Parse(string)" /> remains the single parser.
/// </summary>
internal sealed class FileLayout
{
    private readonly string[] lines;
    private readonly int frontmatterEndIndex;

    private FileLayout(string text, string[] lines, int frontmatterEndIndex, string body, int bodyFirstLine)
    {
        Text = text;
        this.lines = lines;
        this.frontmatterEndIndex = frontmatterEndIndex;
        Body = body;
        BodyFirstLine = bodyFirstLine;
    }

    /// <summary>The file's full text.</summary>
    public string Text { get; }

    /// <summary>The body, exactly as <see cref="OkfDocument" /> would split it.</summary>
    public string Body { get; }

    /// <summary>The 1-based file line the body's first line sits on.</summary>
    public int BodyFirstLine { get; }

    /// <summary>Whether the file opens with a frontmatter fence.</summary>
    public bool HasFrontmatter => this.frontmatterEndIndex > 0;

    /// <summary>Locates a file's frontmatter block and body.</summary>
    /// <param name="text">The file's full text.</param>
    /// <returns>The layout.</returns>
    public static FileLayout Of(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        // A trailing newline terminates the last line rather than starting an empty one,
        // matching how OkfDocument splits the same text.
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        if (lines.Length == 0 || lines[0].Trim() != OkfDocument.FrontmatterDelimiter)
        {
            return new FileLayout(text, lines, 0, text, 1);
        }

        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == OkfDocument.FrontmatterDelimiter)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            // Unterminated: OkfDocument.Parse reports it; there is no body to line up.
            return new FileLayout(text, lines, 0, string.Empty, 1);
        }

        var body = string.Join('\n', lines.Skip(end + 1));

        // OkfDocument.Parse consumes a single newline after the closing fence, so the
        // body's first line is the one after the blank separator when there is one.
        var consumesBlank = end + 1 < lines.Length && lines[end + 1].Length == 0;
        if (consumesBlank)
        {
            body = body[1..];
        }

        return new FileLayout(text, lines, end, body, end + (consumesBlank ? 3 : 2));
    }

    /// <summary>
    /// The 1-based line a top-level frontmatter key sits on, or <see langword="null" />
    /// when the key is absent (a missing key has no line to point at).
    /// </summary>
    /// <param name="key">The frontmatter key.</param>
    /// <returns>The line number, or <see langword="null" />.</returns>
    public int? FrontmatterKeyLine(string key)
    {
        for (var i = 1; i < this.frontmatterEndIndex; i++)
        {
            var line = this.lines[i];
            if (line.Length > key.Length
                && line.StartsWith(key, StringComparison.Ordinal)
                && line[key.Length] == ':')
            {
                return i + 1;
            }
        }

        return null;
    }
}
