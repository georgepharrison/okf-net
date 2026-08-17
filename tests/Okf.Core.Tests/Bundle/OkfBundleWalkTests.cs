namespace Okf.Core.Tests.Bundle;

/// <summary>
/// What the one bundle walk counts as content (work item #42). Every surface — lint,
/// index, search, MCP, the site, the bundler — reads
/// <see cref="OkfBundle.MarkdownFiles" /> or <see cref="OkfBundle.ContentFiles" />, so
/// this is the only place the question is answered.
/// </summary>
/// <remarks>
/// The expectations come from spec §11, which conforms EVERY non-reserved <c>.md</c> file
/// in the tree: a dot-prefixed name is a concept like any other, and the walk skips only
/// the names on <see cref="OkfBundle.IgnoredMetadataNames" />.
/// </remarks>
public class OkfBundleWalkTests
{
    private const string CleanConcept = """
        ---
        type: Reference
        title: Clean
        description: A concept that trips nothing.
        tags: [fixture]
        ---

        # Clean
        """;

    /// <summary>
    /// The ignore list is the whole deviation from "§11 means every file", so it is
    /// pinned literally: an addition to it is a decision, and a decision that arrives
    /// without this test changing is one nobody reviewed.
    /// </summary>
    [Fact]
    public void The_ignored_names_are_exactly_the_documented_set()
    {
        Assert.Equal(
            [
                ".DS_Store",
                ".git",
                ".hg",
                ".idea",
                ".obsidian",
                ".svn",
                ".vscode",
                "Thumbs.db",
                "desktop.ini",
            ],
            OkfBundle.IgnoredMetadataNames.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The match is case-insensitive so a bundle reads the same on every platform: macOS
    /// and Windows filesystems are case-insensitive, so <c>.Git</c> and <c>.git</c> are
    /// one directory there and two here. A case-sensitive match would make conformance
    /// depend on which machine ran the linter, and on Linux it would let a
    /// <c>.GIT</c> escape the list entirely.
    /// </summary>
    [Theory]
    [InlineData(".GIT")]
    [InlineData(".Obsidian")]
    [InlineData(".VSCode")]
    public void An_ignored_metadata_name_is_matched_whatever_its_case(string name)
    {
        using var tree = new TempBundle();
        tree.Add("visible.md", CleanConcept)
            .Add($"{name}/notes.md", "# no frontmatter\n")
            .Add("THUMBS.DB", "explorer state")
            .Add("Desktop.INI", "[.ShellClassInfo]");
        var bundle = tree.Bundle;

        Assert.Equal(["visible.md"], bundle.ContentFiles().Select(bundle.RelativePath));
    }

    [Fact]
    public void A_dot_prefixed_markdown_file_is_walked()
    {
        using var tree = new TempBundle();
        tree.Add(".hidden.md", CleanConcept).Add("visible.md", CleanConcept);
        var bundle = tree.Bundle;

        Assert.Equal(
            [".hidden.md", "visible.md"],
            bundle.MarkdownFiles().Select(bundle.RelativePath));
    }

    [Fact]
    public void A_markdown_file_inside_a_dot_directory_is_walked()
    {
        using var tree = new TempBundle();
        tree.Add(".drafts/nested/deep.md", CleanConcept).Add("visible.md", CleanConcept);
        var bundle = tree.Bundle;

        Assert.Equal(
            [".drafts/nested/deep.md", "visible.md"],
            bundle.MarkdownFiles().Select(bundle.RelativePath));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".hg")]
    [InlineData(".svn")]
    [InlineData(".obsidian")]
    [InlineData(".idea")]
    [InlineData(".vscode")]
    public void An_ignored_metadata_directory_is_not_walked(string name)
    {
        using var tree = new TempBundle();
        tree.Add("visible.md", CleanConcept)
            .Add($"{name}/notes.md", "# no frontmatter\n")
            .Add($"{name}/nested/deeper.md", "# no frontmatter\n")
            .Add($"{name}/state.json", "{}")
            .Add($"references/{name}/notes.md", "# no frontmatter\n");
        var bundle = tree.Bundle;

        Assert.Equal(["visible.md"], bundle.MarkdownFiles().Select(bundle.RelativePath));
        Assert.Equal(["visible.md"], bundle.ContentFiles().Select(bundle.RelativePath));
    }

    /// <summary>
    /// The three names on the list that are files rather than directories: nobody
    /// authored them, an operating system dropped them.
    /// </summary>
    [Fact]
    public void An_ignored_metadata_file_is_not_walked()
    {
        using var tree = new TempBundle();
        tree.Add("visible.md", CleanConcept)
            .Add(".DS_Store", "finder state")
            .Add("Thumbs.db", "explorer state")
            .Add("desktop.ini", "[.ShellClassInfo]")
            .Add("references/.DS_Store", "finder state");
        var bundle = tree.Bundle;

        Assert.Equal(["visible.md"], bundle.ContentFiles().Select(bundle.RelativePath));
    }

    /// <summary>
    /// The list is matched on the whole name, not on the leading dot: a dot-prefixed file
    /// a producer wrote is a producer's file, and the bundler ships it.
    /// </summary>
    [Fact]
    public void A_dot_prefixed_file_that_is_not_on_the_list_is_content()
    {
        using var tree = new TempBundle();
        tree.Add("visible.md", CleanConcept)
            .Add(".gitignore", "artifacts/\n")
            .Add(".editorconfig", "root = true\n");
        var bundle = tree.Bundle;

        Assert.Equal(
            [".editorconfig", ".gitignore", "visible.md"],
            bundle.ContentFiles().Select(bundle.RelativePath));
    }

    /// <summary>
    /// Descending dot-directories does not weaken the symlink rule: a link is refused for
    /// being a link, and its name never enters the decision.
    /// </summary>
    [SkippableFact]
    public void A_symlinked_dot_directory_is_still_refused()
    {
        using var tree = new TempBundle();
        tree.Add("visible.md", CleanConcept).Add("sub/concept.md", CleanConcept);

        using var outside = new TempBundle("outside");
        outside.Add("stranger.md", CleanConcept);

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(tree.Root, ".back"), tree.Root);
            Directory.CreateSymbolicLink(Path.Combine(tree.Root, ".elsewhere"), outside.Root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SkipException($"This platform will not create directory symlinks: {exception.Message}");
        }

        var bundle = tree.Bundle;

        Assert.Equal(
            ["sub/concept.md", "visible.md"],
            bundle.MarkdownFiles().Select(bundle.RelativePath));
    }
}
