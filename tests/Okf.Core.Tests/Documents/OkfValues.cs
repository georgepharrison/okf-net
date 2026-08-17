namespace Okf.Core.Tests.Documents;

/// <summary>Small helpers shared by the document tests.</summary>
internal static class OkfValues
{
    /// <summary>The text of a scalar value, asserting that it is one.</summary>
    public static string Text(OkfValue? value)
    {
        var scalar = Assert.IsType<OkfScalar>(value);
        return scalar.Value;
    }

    /// <summary>The text of a mapping's scalar value at <paramref name="key" />.</summary>
    public static string Text(OkfMapping mapping, string key) => Text(mapping[key]);

    /// <summary>The scalar texts of a sequence value, asserting that it is one.</summary>
    public static string[] Texts(OkfValue? value)
    {
        var sequence = Assert.IsType<OkfSequence>(value);
        return [.. sequence.Select(Text)];
    }

    /// <summary>The keys of a mapping, in order.</summary>
    public static string[] Keys(OkfMapping mapping) => [.. mapping.Entries.Select(e => Text(e.Key))];

    /// <summary>
    /// Structural equality over the value model: keys, key order, and scalar text all
    /// count, which is what "round-tripping preserves the frontmatter" means (PRD
    /// CORE-2). Presentation style is not compared, because an emitter may legally
    /// quote a scalar the source left plain (e.g. <c>file://x.py</c> inside a flow
    /// mapping) without changing its value; the style assertions live in the
    /// dedicated round-trip tests instead.
    /// </summary>
    public static bool DeepEquals(OkfValue? left, OkfValue? right)
    {
        switch (left, right)
        {
            case (null, null):
                return true;

            case (OkfScalar a, OkfScalar b):
                return a.Value == b.Value;

            case (OkfSequence a, OkfSequence b):
                return a.Count == b.Count && a.Zip(b).All(pair => DeepEquals(pair.First, pair.Second));

            case (OkfMapping a, OkfMapping b):
                return a.Count == b.Count && a.Entries.Zip(b.Entries).All(pair =>
                    DeepEquals(pair.First.Key, pair.Second.Key) && DeepEquals(pair.First.Value, pair.Second.Value));

            default:
                return false;
        }
    }

    /// <summary>Asserts <see cref="DeepEquals" />.</summary>
    public static void AssertDeepEqual(OkfValue? expected, OkfValue? actual) =>
        Assert.True(DeepEquals(expected, actual), "values differ structurally");
}
