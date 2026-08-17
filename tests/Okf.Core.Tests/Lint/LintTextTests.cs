namespace Okf.Core.Tests.Lint;

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
    [InlineData("?v=2")]
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

    /// <summary>
    /// §5.1 lets <c>sources[].resource</c> be a population or scope descriptor rather than
    /// a path, and §6.2 lets it be an absolute URL, so only a value that unambiguously
    /// reads as a filesystem path is resolved against the tree. Expectations come from
    /// those two spec rules, not from the classifier.
    /// </summary>
    /// <param name="resource">The <c>resource</c> value as an author would write it.</param>
    /// <param name="expected">The <c>SourceResource</c> name §5.1/§6.2 make of it.</param>
    [Theory]
    [InlineData("", "NotAPath")]
    [InlineData("all queries in project X", "NotAPath")]
    [InlineData("quarterly notes.md", "NotAPath")]
    [InlineData("https://example.invalid/page.md", "NotAPath")]
    [InlineData("//example.invalid/page.md", "NotAPath")]
    [InlineData("project.dataset.table", "NotAPath")]
    [InlineData("dashboards/exec-revenue", "NotAPath")]
    [InlineData("team./roster", "NotAPath")]
    [InlineData("/dashboards/exec-revenue", "Unresolved")]
    [InlineData("./exec-revenue", "Unresolved")]
    [InlineData("../exec-revenue", "Unresolved")]
    [InlineData("exec-revenue.md", "Unresolved")]
    [InlineData("tables/orders.sql", "Unresolved")]
    public void AResourceIsOnlyResolvedWhenItUnambiguouslyReadsAsAPath(string resource, string expected) =>
        Assert.Equal(
            Enum.Parse<SourceResource>(expected),
            LintText.ClassifyResource(resource, Root, SubDirectory));

    /// <summary>§6.2: a relative <c>resource</c> that names a real file resolves.</summary>
    [Fact]
    public void AResourceNamingAFileInTheBundleResolves()
    {
        using var bundle = new TempBundle();
        bundle.Add("tables/orders.md", "# Orders\n");
        var directory = Path.Combine(bundle.Root, "tables");

        Assert.Equal(SourceResource.Resolved, LintText.ClassifyResource("orders.md", bundle.Root, directory));
        Assert.Equal(SourceResource.Resolved, LintText.ClassifyResource("tables/orders.md", bundle.Root, directory));
        Assert.Equal(SourceResource.Unresolved, LintText.ClassifyResource("tables/orders.sql", bundle.Root, directory));
    }
}
