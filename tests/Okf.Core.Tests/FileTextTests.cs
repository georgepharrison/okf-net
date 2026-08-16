using System.Text;

namespace Okf.Core.Tests;

/// <summary>
/// The one way okf puts text on disk (PRD ACC-7). Expectations come from the requirement
/// and from Unicode's definition of the UTF-8 byte-order mark, not from the code under
/// test: the file's bytes must be exactly the text's UTF-8 encoding, and the directory
/// must hold the file and nothing else once the write returns.
/// </summary>
public sealed class FileTextTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());

    /// <summary>A BOM is EF BB BF, and no okf-written file starts with one.</summary>
    [Fact]
    public void WritesTheTextsOwnBytesWithNoByteOrderMark()
    {
        var path = Path.Combine(this.root, "concept.md");

        FileText.WriteAtomic(path, "# tîtle\n");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal("# tîtle\n"u8.ToArray(), bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("# tîtle\n", File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
    }

    /// <summary>
    /// The rename replaces the whole file rather than overwriting the front of it, and the
    /// temporary the write went through is gone — a leftover would be picked up by the next
    /// bundle walk as a file the vault did not put there.
    /// </summary>
    [Fact]
    public void ReplacesTheFileAndLeavesNothingBesideIt()
    {
        var path = Path.Combine(this.root, "manifest.json");
        FileText.WriteAtomic(path, "{ \"captures\": [1, 2, 3] }");

        FileText.WriteAtomic(path, "{}");

        Assert.Equal("{}", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(this.root));
    }

    /// <summary>A caller writing into a directory that is not there yet gets it created.</summary>
    [Fact]
    public void CreatesTheDirectoryItWritesInto()
    {
        var path = Path.Combine(this.root, "nested", "deeper", "index.md");

        FileText.WriteAtomic(path, "x");

        Assert.True(File.Exists(path));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing to clean up.
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
