using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Okf.Core.Documents;

/// <summary>
/// The only place okf-net touches a YAML library. Loading goes through YamlDotNet's
/// reflection-free representation model and emitting goes through its event emitter,
/// so no serializer, type resolver, or reflection is involved and the code is
/// NativeAOT-safe (decisions.md, Q10).
/// </summary>
internal static class YamlBridge
{
    /// <summary>
    /// Loads a single YAML document into the okf value model.
    /// </summary>
    /// <param name="yaml">The YAML text (an OKF frontmatter block, without its <c>---</c> fences).</param>
    /// <returns>The document's root value, or <see langword="null" /> when the text holds no document.</returns>
    /// <exception cref="OkfDocumentException">The text is not a single well-formed YAML document.</exception>
    public static OkfValue? Load(string yaml)
    {
        YamlStream stream = new YamlStream();
        try
        {
            using StringReader reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            throw new OkfDocumentException($"Invalid YAML in frontmatter: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            return null;
        }

        if (stream.Documents.Count > 1)
        {
            // PyYAML's safe_load raises ComposerError("expected a single document
            // in the stream") for this; the reference implementation surfaces it as
            // an invalid-frontmatter error.
            throw new OkfDocumentException("Invalid YAML in frontmatter: expected a single document in the stream");
        }

        return Convert(stream.Documents[0].RootNode, new Dictionary<YamlNode, OkfValue>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>Emits a value as YAML text, preserving key order and scalar style.</summary>
    /// <param name="value">The value to emit.</param>
    /// <returns>The YAML text, with LF line endings.</returns>
    /// <exception cref="OkfDocumentException">The value graph contains a cycle.</exception>
    public static string Emit(OkfValue value)
    {
        StringWriter writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };

        // bestWidth is unbounded so long scalars are never line-wrapped: wrapping
        // would make byte-stable output depend on column arithmetic (PRD ACC-7).
        Emitter emitter = new Emitter(writer, new EmitterSettings(
            bestIndent: 2,
            bestWidth: int.MaxValue,
            isCanonical: false,
            maxSimpleKeyLength: 1024));

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart(null, null, isImplicit: true));
        EmitValue(value, emitter, new HashSet<OkfValue>(ReferenceEqualityComparer.Instance));
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());

        return writer.ToString();
    }

    private static OkfValue Convert(YamlNode node, Dictionary<YamlNode, OkfValue> seen)
    {
        if (seen.TryGetValue(node, out OkfValue? already))
        {
            return already;
        }

        return node switch
        {
            YamlScalarNode scalar => ConvertScalar(scalar, seen),
            YamlSequenceNode sequence => ConvertSequence(sequence, seen),
            YamlMappingNode mapping => ConvertMapping(mapping, seen),
            _ => throw new OkfDocumentException(
                $"Invalid YAML in frontmatter: unsupported node {node.GetType().Name}"),
        };
    }

    private static OkfScalar ConvertScalar(YamlScalarNode node, Dictionary<YamlNode, OkfValue> seen)
    {
        OkfScalar converted = new OkfScalar(node.Value ?? string.Empty, FromYaml(node.Style));
        seen[node] = converted;
        return converted;
    }

    // The node is registered in `seen` BEFORE its children are converted, so a graph that
    // points back at this collection resolves to the same instance instead of recursing.
    private static OkfSequence ConvertSequence(YamlSequenceNode node, Dictionary<YamlNode, OkfValue> seen)
    {
        OkfSequence converted = new OkfSequence { Style = FromYaml(node.Style) };
        seen[node] = converted;
        foreach (YamlNode child in node.Children)
        {
            converted.Add(Convert(child, seen));
        }

        return converted;
    }

    private static OkfMapping ConvertMapping(YamlMappingNode node, Dictionary<YamlNode, OkfValue> seen)
    {
        OkfMapping converted = new OkfMapping { Style = FromYaml(node.Style) };
        seen[node] = converted;
        foreach (KeyValuePair<YamlNode, YamlNode> entry in node.Children)
        {
            converted.Add(Convert(entry.Key, seen), Convert(entry.Value, seen));
        }

        return converted;
    }

    private static void EmitValue(OkfValue value, IEmitter emitter, HashSet<OkfValue> path)
    {
        switch (value)
        {
            case OkfScalar scalar:
                EmitScalar(scalar, emitter);
                return;

            case OkfSequence sequence:
                EmitSequence(sequence, emitter, path);
                return;

            case OkfMapping mapping:
                EmitMapping(mapping, emitter, path);
                return;

            default:
                throw new OkfDocumentException($"Cannot serialize unsupported value {value.GetType().Name}");
        }
    }

    private static void EmitScalar(OkfScalar scalar, IEmitter emitter)
    {
        // An unquoted empty scalar is YAML null (`key:` with no value). YamlDotNet's
        // emitter cannot write an empty *plain* scalar and falls back to `''`, which
        // would silently retype a null into an empty string on round trip (PRD CORE-2).
        // Emit the explicit `null` PyYAML's dumper uses instead. A code-built
        // `Scalar("")` keeps style `Any` and still emits `''`, because that one really
        // is an empty string.
        string text = scalar is { Style: OkfScalarStyle.Plain, Value.Length: 0 } ? "null" : scalar.Value;
        emitter.Emit(new Scalar(
            AnchorName.Empty,
            TagName.Empty,
            text,
            ToYaml(scalar.Style),
            isPlainImplicit: true,
            isQuotedImplicit: true));
    }

    private static void EmitSequence(OkfSequence sequence, IEmitter emitter, HashSet<OkfValue> path)
    {
        Enter(sequence, path);
        emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, isImplicit: true, ToYaml(sequence.Style)));
        foreach (OkfValue item in sequence)
        {
            EmitValue(item, emitter, path);
        }

        emitter.Emit(new SequenceEnd());
        path.Remove(sequence);
    }

    private static void EmitMapping(OkfMapping mapping, IEmitter emitter, HashSet<OkfValue> path)
    {
        Enter(mapping, path);
        emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, isImplicit: true, ToYamlMapping(mapping.Style)));
        foreach (KeyValuePair<OkfValue, OkfValue> entry in mapping)
        {
            EmitValue(entry.Key, emitter, path);
            EmitValue(entry.Value, emitter, path);
        }

        emitter.Emit(new MappingEnd());
        path.Remove(mapping);
    }

    private static void Enter(OkfValue value, HashSet<OkfValue> path)
    {
        if (!path.Add(value))
        {
            // PyYAML emits anchors and aliases for shared nodes; okf-net does not
            // model anchors, so a self-referential graph is rejected instead of
            // being emitted incorrectly (or recursing forever).
            throw new OkfDocumentException("Cannot serialize frontmatter containing a recursive structure");
        }
    }

    private static OkfScalarStyle FromYaml(ScalarStyle style) => style switch
    {
        ScalarStyle.Plain => OkfScalarStyle.Plain,
        ScalarStyle.SingleQuoted => OkfScalarStyle.SingleQuoted,
        ScalarStyle.DoubleQuoted => OkfScalarStyle.DoubleQuoted,
        ScalarStyle.Literal => OkfScalarStyle.Literal,
        ScalarStyle.Folded => OkfScalarStyle.Folded,
        _ => OkfScalarStyle.Any,
    };

    private static ScalarStyle ToYaml(OkfScalarStyle style) => style switch
    {
        OkfScalarStyle.Plain => ScalarStyle.Plain,
        OkfScalarStyle.SingleQuoted => ScalarStyle.SingleQuoted,
        OkfScalarStyle.DoubleQuoted => ScalarStyle.DoubleQuoted,
        OkfScalarStyle.Literal => ScalarStyle.Literal,
        OkfScalarStyle.Folded => ScalarStyle.Folded,
        _ => ScalarStyle.Any,
    };

    private static OkfCollectionStyle FromYaml(SequenceStyle style) => style switch
    {
        SequenceStyle.Block => OkfCollectionStyle.Block,
        SequenceStyle.Flow => OkfCollectionStyle.Flow,
        _ => OkfCollectionStyle.Any,
    };

    private static OkfCollectionStyle FromYaml(MappingStyle style) => style switch
    {
        MappingStyle.Block => OkfCollectionStyle.Block,
        MappingStyle.Flow => OkfCollectionStyle.Flow,
        _ => OkfCollectionStyle.Any,
    };

    private static SequenceStyle ToYaml(OkfCollectionStyle style) => style switch
    {
        OkfCollectionStyle.Block => SequenceStyle.Block,
        OkfCollectionStyle.Flow => SequenceStyle.Flow,
        _ => SequenceStyle.Any,
    };

    private static MappingStyle ToYamlMapping(OkfCollectionStyle style) => style switch
    {
        OkfCollectionStyle.Block => MappingStyle.Block,
        OkfCollectionStyle.Flow => MappingStyle.Flow,
        _ => MappingStyle.Any,
    };
}
