namespace Okf.Core.Tests;

/// <summary>
/// Line bookkeeping: a diagnostic's line must agree with what a reader sees in the file,
/// and the body's first line must agree with what <see cref="OkfDocument" /> parsed.
/// </summary>
public class FileLayoutTests
{
    private const string Concept = """
        ---
        type: Metric
        title: Revenue
        stale_after: 2026-12-31
        ---

        # Revenue

        Body.
        """;

    [Fact]
    public void FindsFrontmatterKeyLines()
    {
        var layout = FileLayout.Of(Concept);

        Assert.True(layout.HasFrontmatter);
        Assert.Equal(2, layout.FrontmatterKeyLine("type"));
        Assert.Equal(4, layout.FrontmatterKeyLine("stale_after"));
        Assert.Null(layout.FrontmatterKeyLine("description"));
    }

    [Fact]
    public void TheBodyStartsWhereTheReaderSeesIt()
    {
        var layout = FileLayout.Of(Concept);

        Assert.Equal(7, layout.BodyFirstLine);
        Assert.Equal(OkfDocument.Parse(Concept).Body, layout.Body);
    }

    [Fact]
    public void ABodyWithNoBlankSeparatorStartsOneLineEarlier()
    {
        const string Text = "---\ntype: Metric\n---\n# Revenue\n";
        var layout = FileLayout.Of(Text);

        Assert.Equal(4, layout.BodyFirstLine);
        Assert.Equal(OkfDocument.Parse(Text).Body, layout.Body);
    }

    [Fact]
    public void AFileWithoutFrontmatterIsAllBodyFromLineOne()
    {
        var layout = FileLayout.Of("# Title\n\nProse.\n");

        Assert.False(layout.HasFrontmatter);
        Assert.Equal(1, layout.BodyFirstLine);
        Assert.Equal("# Title\n\nProse.\n", layout.Body);
    }

    [Fact]
    public void AnUnterminatedFenceHasNoFrontmatterAndNoBody()
    {
        var layout = FileLayout.Of("---\ntype: Metric\n");

        Assert.False(layout.HasFrontmatter);
        Assert.Equal(string.Empty, layout.Body);
    }

    [Fact]
    public void CarriageReturnsDoNotShiftLines()
    {
        var layout = FileLayout.Of("---\r\ntype: Metric\r\n---\r\n\r\n# Revenue\r\n");

        Assert.Equal(2, layout.FrontmatterKeyLine("type"));
        Assert.Equal(5, layout.BodyFirstLine);
    }

    /// <summary>
    /// SPEC §4 allows a document that is nothing but its frontmatter block. The closing
    /// fence is then the last line, so the "is the next line blank?" lookahead has no next
    /// line to read.
    /// </summary>
    [Fact]
    public void FrontmatterMayBeTheWholeFile()
    {
        const string Text = "---\ntype: Metric\n---\n";
        var layout = FileLayout.Of(Text);

        Assert.True(layout.HasFrontmatter);
        Assert.Equal(string.Empty, layout.Body);
        Assert.Equal(4, layout.BodyFirstLine);
        Assert.Equal(OkfDocument.Parse(Text).Body, layout.Body);
    }

    /// <summary>
    /// §4 closes the frontmatter at the FIRST fence after the opening one; a later
    /// <c>---</c> line is a thematic break in the body, not a second closing fence.
    /// </summary>
    [Fact]
    public void AThematicBreakInTheBodyIsNotTheClosingFence()
    {
        const string Text = "---\ntype: Metric\n---\n\n# Revenue\n\n---\n\nTail.\n";
        var layout = FileLayout.Of(Text);

        Assert.Equal(5, layout.BodyFirstLine);
        Assert.Equal("# Revenue\n\n---\n\nTail.", layout.Body);
        Assert.Equal(OkfDocument.Parse(Text).Body, layout.Body);
    }

    /// <summary>
    /// A key line is <c>key:</c>; a frontmatter line that is the bare word alone declares
    /// no key, and reading one character past it must not be attempted.
    /// </summary>
    [Fact]
    public void ABareWordInFrontmatterIsNotAKeyLine()
    {
        var layout = FileLayout.Of("---\ntype\ntitle: Revenue\n---\n");

        Assert.Null(layout.FrontmatterKeyLine("type"));
        Assert.Equal(3, layout.FrontmatterKeyLine("title"));
    }
}
