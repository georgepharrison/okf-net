namespace Okf.Core.Documents;

/// <summary>
/// Reading values back out of parsed frontmatter, under §11's rule about what counts as
/// present.
/// </summary>
internal static class FrontmatterValues
{
    /// <summary>
    /// The scalar under a key, or <see langword="null" /> when the key is absent, holds
    /// something other than a scalar, or holds a scalar §11 does not count as present —
    /// the reference implementation's truthiness, which okf-net follows for interop
    /// (ACC-2). Every reader of a concept's frontmatter asks this same question, so a key
    /// counts as present the same way for lint, index, search, the inbox and the site.
    /// </summary>
    /// <param name="mapping">The frontmatter, or a mapping nested in it.</param>
    /// <param name="key">The key to read.</param>
    /// <returns>The value, or <see langword="null" />.</returns>
    public static string? Scalar(OkfMapping mapping, string key) =>
        mapping.TryGetValue(key, out var value) && value is OkfScalar scalar && scalar.IsTruthy
            ? scalar.Value
            : null;

    /// <summary>
    /// The <c>tags</c> list (§5.4). A scalar is one tag, not a list to guess a separator
    /// for; a sequence contributes every scalar item §11 counts as present; anything else
    /// contributes nothing.
    /// </summary>
    /// <param name="mapping">The frontmatter.</param>
    /// <returns>The tags, in the order they were written.</returns>
    public static IReadOnlyList<string> Tags(OkfMapping mapping)
    {
        if (!mapping.TryGetValue("tags", out var value))
        {
            return [];
        }

        return value switch
        {
            OkfScalar scalar when scalar.IsTruthy => [scalar.Value],
            OkfSequence sequence =>
                [.. sequence.OfType<OkfScalar>().Where(item => item.IsTruthy).Select(item => item.Value)],
            _ => [],
        };
    }
}
