using System.Security.Cryptography;
using System.Text;
using Okf.Core.Bundle;
using Okf.Core.Documents;
using Okf.Core.Search;
using Okf.Core.Vault;

namespace Okf.Spike.Vectorization;

/// <summary>One concept, as the vector layer would see it.</summary>
internal sealed record CorpusItem(
    string Bundle,
    string BundleName,
    string RelativePath,
    string Title,
    string Text,
    string Sha256);

/// <summary>
/// Loads concepts exactly the way <see cref="OkfSearchEngine" /> does — same discovery,
/// same reserved-file exclusion, same "unparseable frontmatter is not a corpus entry"
/// rule — so the vector index and the lexical index can never disagree about what the
/// corpus is.
/// </summary>
internal static class Corpus
{
    public static IReadOnlyList<CorpusItem> Load(string? path)
    {
        var environment = OkfEnvironment.FromProcess();
        var workingSet = OkfDiscovery.Resolve(path, environment);
        var items = new List<CorpusItem>();

        foreach (var bundle in workingSet.Bundles)
        {
            foreach (var file in bundle.MarkdownFiles())
            {
                if (OkfBundle.IsReservedFile(file))
                {
                    continue;
                }

                var raw = File.ReadAllText(file);
                OkfDocument document;
                try
                {
                    document = OkfDocument.Parse(raw);
                }
                catch (OkfDocumentException)
                {
                    continue;
                }

                var title = Scalar(document, "title") ?? Path.GetFileNameWithoutExtension(file);
                var description = Scalar(document, "description");
                var type = Scalar(document, "type");
                var tags = Sequence(document, "tags");

                // The embedded text is the same signal the BM25 fields carry, flattened:
                // title, type, tags, description, then body. No field weighting — a
                // transformer has no notion of one.
                var text = new StringBuilder()
                    .AppendLine(title)
                    .AppendLine(type ?? string.Empty)
                    .AppendLine(string.Join(", ", tags))
                    .AppendLine(description ?? string.Empty)
                    .Append(document.Body)
                    .ToString();

                items.Add(new CorpusItem(
                    bundle.Root,
                    bundle.Name,
                    bundle.RelativePath(file),
                    title,
                    text,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()));
            }
        }

        return items;
    }

    private static string? Scalar(OkfDocument document, string key) =>
        document.Frontmatter.TryGetValue(key, out var value) && value is OkfScalar scalar
            ? scalar.Value
            : null;

    private static IReadOnlyList<string> Sequence(OkfDocument document, string key)
    {
        if (!document.Frontmatter.TryGetValue(key, out var value) || value is not OkfSequence sequence)
        {
            return [];
        }

        return [.. sequence.OfType<OkfScalar>().Select(entry => entry.Value)];
    }
}
