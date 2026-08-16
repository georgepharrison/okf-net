namespace Okf.Core.Tests;

/// <summary>
/// The frontmatter mapping's own contract: insertion order is the document's key order
/// (PRD CORE-2), and duplicate scalar keys resolve last-wins the way PyYAML reads them.
/// </summary>
public class OkfMappingTests
{
    private static OkfMapping Frontmatter() => new()
    {
        { "type", "Metric" },
        { "title", "Revenue" },
    };

    [Fact]
    public void AssigningAnExistingKeyReplacesItInPlace()
    {
        var mapping = Frontmatter();

        mapping["type"] = OkfValue.Scalar("Reference");

        Assert.Equal(["type", "title"], OkfValues.Keys(mapping));
        Assert.Equal("Reference", OkfValues.Text(mapping, "type"));
    }

    [Fact]
    public void AssigningAnAbsentKeyAppendsItAfterTheExistingKeys()
    {
        var mapping = Frontmatter();

        mapping["description"] = OkfValue.Scalar("Monthly revenue.");

        Assert.Equal(["type", "title", "description"], OkfValues.Keys(mapping));
    }

    [Fact]
    public void ContainsKeyAnswersForTheFirstKeyAsWellAsALaterOne()
    {
        var mapping = Frontmatter();

        Assert.True(mapping.ContainsKey("type"));
        Assert.True(mapping.ContainsKey("title"));
        Assert.False(mapping.ContainsKey("description"));
    }

    [Fact]
    public void RemoveTakesEveryEntryUnderTheKeyAndNothingElse()
    {
        var mapping = Frontmatter();
        mapping.Add("title", "Revenue, restated");

        Assert.True(mapping.Remove("title"));
        Assert.Equal(["type"], OkfValues.Keys(mapping));

        Assert.False(mapping.Remove("title"));
        Assert.Equal(["type"], OkfValues.Keys(mapping));
    }

    [Fact]
    public void ReadingADuplicatedKeyTakesTheLastOne()
    {
        var mapping = Frontmatter();
        mapping.Add("title", "Revenue, restated");

        Assert.Equal("Revenue, restated", OkfValues.Text(mapping, "title"));
    }
}
