namespace Okf.Core.Tests;

/// <summary>
/// The <c>AGENTS.md</c> / <c>CLAUDE.md</c> context pointer: what it writes, and — the load-
/// bearing half — what it refuses to touch (work item #56).
/// </summary>
public class OkfAgentPointerTests
{
    [Fact]
    public void AnAbsentAgentsMdIsCreatedWithTheBlock()
    {
        using var tree = new TempTree();

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Created, file.Status);
        Assert.Equal(OkfAgentPointer.Block + "\n", File.ReadAllText(file.Path));
    }

    [Fact]
    public void AnAgentsMdWithNoFenceGetsTheBlockAppendedAfterOneBlankLine()
    {
        using var tree = new TempTree();
        var path = tree.Write("AGENTS.md", "# Existing rules\n\nDo the thing.\n");

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);
        var text = File.ReadAllText(path);
        Assert.Equal(
            "# Existing rules\n\nDo the thing.\n\n" + OkfAgentPointer.Block + "\n",
            text);

        // Without the append, the original text would be all there is — the assertion above
        // fails on a build that skips this branch entirely.
        Assert.StartsWith("# Existing rules\n\nDo the thing.\n\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFencePresentAndDifferentIsReplacedByteForByte()
    {
        using var tree = new TempTree();
        var before = "# Rules\n\n<!-- okf:begin -->\nstale block\n<!-- okf:end -->\n\nMore rules.\n";
        var path = tree.Write("AGENTS.md", before);

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);
        var text = File.ReadAllText(path);
        Assert.Equal("# Rules\n\n" + OkfAgentPointer.Block + "\n\nMore rules.\n", text);

        // The region outside the fence is untouched: the heading and the trailing prose
        // survive exactly, which is what proves the write is a splice and not a rewrite.
        Assert.StartsWith("# Rules\n\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n\nMore rules.\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stale block", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondRunOnAnAlreadyCurrentFenceIsUnchangedAndWritesNothing()
    {
        using var tree = new TempTree();
        OkfAgentPointer.WriteAgentsMd(tree.Root);
        var path = Path.Combine(tree.Root, "AGENTS.md");
        var before = File.ReadAllBytes(path);
        var beforeWriteTime = File.GetLastWriteTimeUtc(path);

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Unchanged, file.Status);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(beforeWriteTime, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void RunningTwiceFromAnUnfencedFileConvergesOnTheSameBytesTheThirdTime()
    {
        // Idempotence end to end: create -> no fence yet (first real AGENTS.md a project
        // authored) -> append -> unchanged forever after.
        using var tree = new TempTree();
        tree.Write("AGENTS.md", "Team conventions.\n");

        var first = OkfAgentPointer.WriteAgentsMd(tree.Root);
        var second = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, first.Status);
        Assert.Equal(OkfAgentPointerStatus.Unchanged, second.Status);
    }

    [Fact]
    public void BytesOutsideTheFenceSurviveByteForByteInACrlfFile()
    {
        using var tree = new TempTree();
        var before =
            "# Rules\r\n\r\n<!-- okf:begin -->\r\nstale\r\n<!-- okf:end -->\r\n\r\nTrailer line.\r\n";
        var path = tree.Write("AGENTS.md", before);

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);
        var text = File.ReadAllText(path);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);

        // Every line the write did not own keeps its own CRLF terminator, and so does every
        // freshly written line of the block — the file's newline style, end to end.
        Assert.StartsWith("# Rules\r\n\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\nTrailer line.\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n<!-- okf:begin -->", text, StringComparison.Ordinal);
        Assert.Contains("\r\n<!-- okf:begin -->\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\n<!-- okf:end -->\r\n", text, StringComparison.Ordinal);

        // No lone '\n' anywhere: every line ending in the whole file is '\r\n'.
        Assert.DoesNotContain(text.Replace("\r\n", string.Empty), "\n", StringComparison.Ordinal);
    }

    [Fact]
    public void AFenceClosingAtEndOfFileWithNoTrailingNewlineKeepsItThatWay()
    {
        // The splice reuses the terminator of the line it replaces, so the one file shape
        // that has no terminator at all — a fence closing the last line — stays that way.
        using var tree = new TempTree();
        var path = tree.Write("AGENTS.md", "# Rules\n\n<!-- okf:begin -->\nstale\n<!-- okf:end -->");

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);
        Assert.Equal("# Rules\n\n" + OkfAgentPointer.Block, File.ReadAllText(path));
    }

    [Fact]
    public void AnUnfencedFileWithNoTrailingNewlineStillGetsExactlyOneBlankLineBeforeTheBlock()
    {
        using var tree = new TempTree();
        var path = tree.Write("AGENTS.md", "Team conventions.");

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);
        Assert.Equal("Team conventions.\n\n" + OkfAgentPointer.Block + "\n", File.ReadAllText(path));
    }

    [Fact]
    public void AFileCarryingMoreThanOneFenceIsLeftExactlyAsFound()
    {
        // Bringing one fence current would leave the other saying something stale in the
        // agent's context on every turn, and this writer deletes nothing it did not write
        // in the run doing the deleting: it reports the file and touches nothing.
        using var tree = new TempTree();
        var before =
            "# Rules\n\n<!-- okf:begin -->\nstale one\n<!-- okf:end -->\n\nProse.\n\n" +
            "<!-- okf:begin -->\nstale two\n<!-- okf:end -->\n";
        var path = tree.Write("AGENTS.md", before);

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Refused, file.Status);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void AFenceNestedInsideAnotherIsOneFenceAndIsSplicedCurrent()
    {
        // A begin with no close of its own before the next end marker is inside the region
        // the first fence owns, not a second fence: one machine-owned region, spliced.
        using var tree = new TempTree();
        var path = tree.Write(
            "AGENTS.md",
            "# Rules\n\n<!-- okf:begin -->\nstale\n<!-- okf:begin -->\nstale\n<!-- okf:end -->\n");

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);
        Assert.Equal("# Rules\n\n" + OkfAgentPointer.Block + "\n", File.ReadAllText(path));
    }

    [Fact]
    public void AnAbsentClaudeMdIsCreatedWithTheRepositorysOwnOneLiner()
    {
        using var tree = new TempTree();

        var file = OkfAgentPointer.WriteClaudeMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Created, file.Status);
        Assert.Equal(
            "# Agent Instructions\n\nRead [AGENTS.md](AGENTS.md) and follow it.\n",
            File.ReadAllText(file.Path));
    }

    [Fact]
    public void AnExistingClaudeMdIsLeftExactlyAsFoundWhateverItSays()
    {
        using var tree = new TempTree();
        var path = tree.Write("CLAUDE.md", "# Something else entirely\n");

        var file = OkfAgentPointer.WriteClaudeMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Skipped, file.Status);
        Assert.Equal("# Something else entirely\n", File.ReadAllText(path));
    }

    /// <summary>
    /// A current fence with the file continuing after it is still current. The comparison
    /// reads exactly the fence's own lines, so prose below it can neither be mistaken for
    /// part of the block nor make an unchanged file look stale.
    /// </summary>
    [Fact]
    public void ACurrentFenceWithProseAfterItIsUnchanged()
    {
        using var tree = new TempTree();
        var before = "# Rules\n\n" + OkfAgentPointer.Block + "\n\nMore rules.\n";
        var path = tree.Write("AGENTS.md", before);

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Unchanged, file.Status);
        Assert.Equal(before, File.ReadAllText(path));
    }

    /// <summary>
    /// The doc-comment's rule that an end marker before any begin is not a fence, applied
    /// where it actually happens: a file that already has its block and mentions the closing
    /// marker again on a line of its own lower down still has one fence, and is spliced
    /// rather than refused.
    /// </summary>
    [Fact]
    public void AStrayEndMarkerBelowACompleteFenceIsNotASecondFence()
    {
        using var tree = new TempTree();
        var path = tree.Write(
            "AGENTS.md",
            "<!-- okf:begin -->\nstale\n<!-- okf:end -->\n\nProse.\n\n<!-- okf:end -->\n");

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);
        Assert.Equal(
            OkfAgentPointer.Block + "\n\nProse.\n\n<!-- okf:end -->\n",
            File.ReadAllText(path));
    }

    [Fact]
    public void AFileOpeningWithABlankLineIsSplitFromItsFirstByte()
    {
        using var tree = new TempTree();
        var path = tree.Write("AGENTS.md", "\n# Rules\n");

        var file = OkfAgentPointer.WriteAgentsMd(tree.Root);

        Assert.Equal(OkfAgentPointerStatus.Updated, file.Status);
        Assert.Equal("\n# Rules\n\n" + OkfAgentPointer.Block + "\n", File.ReadAllText(path));
    }

    /// <summary>
    /// CRLF is the file's convention when any line carries it — the final line of a file
    /// with no trailing newline carries no terminator at all, and that must not be read as
    /// the file having gone over to LF.
    /// </summary>
    [Fact]
    public void ACrlfFileWithNoTrailingNewlineKeepsCrlf()
    {
        using var tree = new TempTree();
        var path = tree.Write("AGENTS.md", "# Rules\r\n\r\nDo the thing.");

        OkfAgentPointer.WriteAgentsMd(tree.Root);

        var text = File.ReadAllText(path);
        Assert.Equal(
            "# Rules\r\n\r\nDo the thing.\r\n\r\n"
            + OkfAgentPointer.Block.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n",
            text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// The write is atomic and self-cleaning on both paths: it creates the directory it was
    /// pointed at, writes UTF-8 with no byte-order mark, and leaves no temp file behind even
    /// when the write fails.
    /// </summary>
    [Fact]
    public void TheWriteCreatesItsDirectoryAndLeavesNoBomAndNoTemporaryFile()
    {
        using var tree = new TempTree();
        var project = Path.Combine(tree.Root, "absent", "deeper");

        var file = OkfAgentPointer.WriteAgentsMd(project);

        var bytes = File.ReadAllBytes(file.Path);
        Assert.Equal(OkfAgentPointerStatus.Created, file.Status);
        Assert.NotEqual<byte[]>([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal(["AGENTS.md"], Directory.EnumerateFiles(project).Select(Path.GetFileName));
    }

    [Fact]
    public void AFailedWriteLeavesNoTemporaryFileBehind()
    {
        using var tree = new TempTree();
        var project = tree.CreateDirectory("blocked");
        Directory.CreateDirectory(Path.Combine(project, "AGENTS.md"));

        Assert.ThrowsAny<IOException>(() => OkfAgentPointer.WriteAgentsMd(project));
        Assert.Empty(Directory.EnumerateFiles(project));
    }

    [Fact]
    public void WriteReportsAgentsMdThenClaudeMdInThatOrder()
    {
        using var tree = new TempTree();

        var files = OkfAgentPointer.Write(tree.Root);

        Assert.Equal(2, files.Count);
        Assert.Equal(Path.Combine(tree.Root, "AGENTS.md"), files[0].Path);
        Assert.Equal(Path.Combine(tree.Root, "CLAUDE.md"), files[1].Path);
        Assert.Equal(OkfAgentPointerStatus.Created, files[0].Status);
        Assert.Equal(OkfAgentPointerStatus.Created, files[1].Status);
    }

    [Fact]
    public void TheBlockCarriesBothMarkersOnTheirOwnLines()
    {
        var lines = OkfAgentPointer.Block.Split('\n');

        Assert.Equal(OkfAgentPointer.BeginMarker, lines[0]);
        Assert.Equal(OkfAgentPointer.EndMarker, lines[^1]);
    }
}
