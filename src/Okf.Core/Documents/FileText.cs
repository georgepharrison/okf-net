using System.Text;

namespace Okf.Core.Documents;

/// <summary>
/// How okf puts text on disk: always UTF-8 with no byte-order mark, and — where a
/// half-written file would be worse than no write at all — through a temporary file
/// renamed over the target.
/// </summary>
internal static class FileText
{
    /// <summary>
    /// UTF-8 without a byte-order mark. PRD ACC-7: the bytes must not depend on the
    /// machine that wrote them, so nothing okf writes grows a BOM and a digest of an
    /// okf-written file is comparable across machines.
    /// </summary>
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes text so a reader never finds half of it: the bytes go to a sibling temporary
    /// file, which is then renamed over the target — within one directory that is the
    /// operation that leaves the path holding either all of the old file or all of the new
    /// one. The temporary is named as a dotfile with a random suffix, so a walk that runs
    /// during the write does not see it and two writers cannot pick the same name, and it
    /// is deleted whether the write finished or threw.
    /// </summary>
    /// <param name="path">The file to write. Its directory is created if it does not exist.</param>
    /// <param name="text">The text to write.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public static void WriteAtomic(string path, string text)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new IOException($"'{path}' has no directory to write into.");

        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Path.GetRandomFileName()}");

        try
        {
            File.WriteAllText(temporary, text, Utf8NoBom);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
