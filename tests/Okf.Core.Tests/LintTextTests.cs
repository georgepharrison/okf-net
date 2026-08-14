namespace Okf.Core.Tests;

/// <summary>Link resolution (spec §6.1, §6.2) and the §8 index-entry form.</summary>
public class LintTextTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "okf-link-tests", "bundle");
    private static readonly string SubDirectory = Path.Combine(Root, "tables");

    [Fact]
    public void BundleRootedLinksResolveAgainstTheBundleRoot()
    {
        Assert.True(LintText.TryResolveLink("/tables/orders.md", Root, SubDirectory, out var resolved));
        Assert.Equal(Path.Combine(Root, "tables", "orders.md"), resolved);
    }

    [Fact]
    public void RelativeLinksResolveAgainstTheLinkingDocument()
    {
        Assert.True(LintText.TryResolveLink("./orders.md", Root, SubDirectory, out var here));
        Assert.Equal(Path.Combine(Root, "tables", "orders.md"), here);

        Assert.True(LintText.TryResolveLink("../metrics/revenue.md", Root, SubDirectory, out var sibling));
        Assert.Equal(Path.Combine(Root, "metrics", "revenue.md"), sibling);
    }

    [Fact]
    public void FragmentsAndQueriesAreStrippedBeforeResolving()
    {
        Assert.True(LintText.TryResolveLink("orders.md#schema", Root, SubDirectory, out var fragment));
        Assert.Equal(Path.Combine(Root, "tables", "orders.md"), fragment);

        Assert.True(LintText.TryResolveLink("orders.md?v=2", Root, SubDirectory, out var query));
        Assert.Equal(Path.Combine(Root, "tables", "orders.md"), query);
    }

    [Fact]
    public void PercentEncodingIsDecoded()
    {
        Assert.True(LintText.TryResolveLink("customer%20orders.md", Root, SubDirectory, out var resolved));
        Assert.Equal(Path.Combine(Root, "tables", "customer orders.md"), resolved);
    }

    [Fact]
    public void ADirectoryLinkResolvesToTheDirectory()
    {
        Assert.True(LintText.TryResolveLink("subdir/", Root, SubDirectory, out var resolved));
        Assert.Equal(Path.Combine(Root, "tables", "subdir"), Path.TrimEndingDirectorySeparator(resolved));
    }

    [Theory]
    [InlineData("https://example.invalid/page")]
    [InlineData("http://example.invalid")]
    [InlineData("mailto:someone@example.invalid")]
    [InlineData("bq://project.dataset.table")]
    [InlineData("#in-page-anchor")]
    [InlineData("")]
    [InlineData("../../outside-the-bundle.md")]
    [InlineData("//example.invalid/protocol-relative")]
    public void NonBundleInternalTargetsAreNotChecked(string target) =>
        Assert.False(LintText.TryResolveLink(target, Root, SubDirectory, out _));

    [Theory]
    [InlineData("[Title](link.md) - description")]
    [InlineData("[Title](link.md)")]
    [InlineData("[Sub directory](subdir/)")]
    [InlineData("[Title](link.md) — an em dash separator")]
    public void WellFormedIndexEntriesAreAccepted(string entry) =>
        Assert.True(LintText.IsIndexEntry(entry));

    [Theory]
    [InlineData("just prose")]
    [InlineData("[Title] (link.md)")]
    [InlineData("[Title](link.md) trailing words without a separator")]
    [InlineData("`code`")]
    public void MalformedIndexEntriesAreRejected(string entry) =>
        Assert.False(LintText.IsIndexEntry(entry));
}
