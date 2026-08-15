namespace Okf.Core.Tests;

/// <summary>
/// The small markdown reader the lint rules sit on: links, footnote labels, headings, and
/// bullets, with fenced code blocks excluded.
/// </summary>
public class MarkdownScannerTests
{
    [Fact]
    public void FindsInlineLinksImagesAndReferenceDefinitions()
    {
        var scan = MarkdownScanner.Scan(
            """
            See [one](/a.md) and ![two](img/b.png "a title").

            [three]: ../c.md
            """,
            1);

        Assert.Equal(["/a.md", "img/b.png", "../c.md"], scan.Links.Select(link => link.Target));
        Assert.Equal([1, 1, 3], scan.Links.Select(link => link.Line));
    }

    [Fact]
    public void SkipsFencedCodeBlocks()
    {
        var scan = MarkdownScanner.Scan(
            """
            Before [real](/real.md).

            ```sql
            -- [not a link](/fake.md) and a [^fake] footnote
            SELECT 1;
            ```

            After.
            """,
            1);

        Assert.Equal(["/real.md"], scan.Links.Select(link => link.Target));
        Assert.Empty(scan.Footnotes);
    }

    [Fact]
    public void SeparatesFootnoteReferencesFromDefinitions()
    {
        var scan = MarkdownScanner.Scan(
            """
            A claim.[^src]

            [^src]: The source.
            """,
            1);

        Assert.Equal(["src", "src"], scan.Footnotes.Select(note => note.Label));
        Assert.False(scan.Footnotes[0].IsDefinition);
        Assert.True(scan.Footnotes[1].IsDefinition);

        // A footnote definition is prose, not a link destination.
        Assert.Empty(scan.Links);
    }

    [Fact]
    public void AFootnoteAloneOnALineIsAReferenceUnlessItCarriesTheColon()
    {
        var scan = MarkdownScanner.Scan(
            """
            SELECT 1;

            [^src]

            [^src]: The source.
            """,
            1);

        // The colon is what makes a definition (GFM/pandoc footnote syntax, and the form
        // `[^src]` alone on a line is how Google's ga4 bundle cites a source under a SQL
        // block). Reading a bare label as a definition would make a cited source look
        // uncited to OKF0102.
        Assert.Equal([false, true], scan.Footnotes.Select(note => note.IsDefinition));
        Assert.Equal([3, 5], scan.Footnotes.Select(note => note.Line));
    }

    [Fact]
    public void RecordsHeadingLevelsAndBullets()
    {
        var scan = MarkdownScanner.Scan(
            """
            # Section

            ## 2026-05-22

            * [Entry](entry.md) - description
            - dash bullet
            """,
            10);

        Assert.Equal([(1, "Section", 10), (2, "2026-05-22", 12)], scan.Headings.Select(h => (h.Level, h.Text, h.Line)));
        Assert.Equal(["[Entry](entry.md) - description", "dash bullet"], scan.Bullets.Select(b => b.Text));
    }

    [Fact]
    public void LineNumbersAreOffsetByTheBodyStart()
    {
        var scan = MarkdownScanner.Scan("first\n\n[link](/a.md)\n", 12);

        Assert.Equal(14, scan.Links.Single().Line);
    }

    [Fact]
    public void AnEmptyBodyHasNoContent()
    {
        Assert.False(MarkdownScanner.Scan("\n   \n", 1).HasContent);
        Assert.True(MarkdownScanner.Scan("something\n", 1).HasContent);
    }
}
