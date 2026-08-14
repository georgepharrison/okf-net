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
}
