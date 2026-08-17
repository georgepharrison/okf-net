using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Okf.Core.Documents;

/// <summary>
/// Presentation style of a scalar, preserved across a parse/serialize round trip so
/// that quoting survives (OKF v0.2 §4.1: consumers SHOULD preserve unknown keys —
/// okf-net extends that to the values' original form).
/// </summary>
public enum OkfScalarStyle
{
    /// <summary>Unspecified; the emitter picks a style.</summary>
    Any = 0,

    /// <summary>Unquoted, e.g. <c>type: Metric</c>.</summary>
    Plain,

    /// <summary>Single quoted, e.g. <c>type: 'Metric'</c>.</summary>
    SingleQuoted,

    /// <summary>Double quoted, e.g. <c>okf_version: "0.2"</c>.</summary>
    DoubleQuoted,

    /// <summary>Literal block scalar (<c>|</c>).</summary>
    Literal,

    /// <summary>Folded block scalar (<c>&gt;</c>).</summary>
    Folded,
}

/// <summary>Presentation style of a sequence or mapping.</summary>
public enum OkfCollectionStyle
{
    /// <summary>Unspecified; the emitter picks a style.</summary>
    Any = 0,

    /// <summary>Block style, one entry per line.</summary>
    Block,

    /// <summary>Flow style, e.g. <c>tags: [a, b]</c>.</summary>
    Flow,
}

/// <summary>
/// A YAML value inside an OKF frontmatter block. okf-net models frontmatter as a
/// structure-preserving tree rather than deserializing into typed objects, so unknown
/// producer keys, key order, and scalar form all survive a round trip (PRD CORE-2).
/// </summary>
public abstract class OkfValue
{
    private protected OkfValue()
    {
    }

    /// <summary>
    /// Whether the value is truthy under the reference implementation's rules
    /// (Python truthiness over <c>yaml.safe_load</c> output): a null, <c>false</c>,
    /// zero, empty string, empty sequence, or empty mapping is falsy.
    /// </summary>
    /// <remarks>
    /// Internal, not public: this is the reference implementation's vocabulary, and the
    /// only thing it decides is whether a key counts as present under §11. Exposing it
    /// would freeze a Python concept onto a C# surface at 1.0.
    /// </remarks>
    internal abstract bool IsTruthy { get; }

    /// <summary>Creates a scalar value.</summary>
    /// <param name="value">The scalar's text.</param>
    /// <param name="style">The scalar's presentation style.</param>
    /// <returns>The new scalar.</returns>
    public static OkfScalar Scalar(string value, OkfScalarStyle style = OkfScalarStyle.Any) =>
        new(value, style);
}

/// <summary>A scalar YAML value, carried as its source text plus presentation style.</summary>
public sealed class OkfScalar : OkfValue
{
    private static readonly string[] NullTexts = ["", "~", "null", "Null", "NULL"];

    // PyYAML resolves YAML 1.1 booleans, so `no` and `off` are false, not strings.
    // This is exactly PyYAML's bool resolver pattern: the single letters `y`/`n`
    // that YAML 1.1 also lists are *not* included, so `type: n` is the string "n"
    // and therefore a non-empty `type` under §11.
    private static readonly string[] FalseTexts =
    [
        "false", "False", "FALSE",
        "no", "No", "NO",
        "off", "Off", "OFF",
    ];

    /// <summary>Initializes a new scalar.</summary>
    /// <param name="value">The scalar's text.</param>
    /// <param name="style">The scalar's presentation style.</param>
    public OkfScalar(string value, OkfScalarStyle style = OkfScalarStyle.Any)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Style = style;
    }

    /// <summary>The scalar's text, exactly as written (quotes excluded).</summary>
    public string Value { get; }

    /// <summary>The scalar's presentation style.</summary>
    public OkfScalarStyle Style { get; }

    /// <summary>Whether the scalar was quoted, and therefore is unambiguously a string.</summary>
    public bool IsQuoted =>
        Style is OkfScalarStyle.SingleQuoted or OkfScalarStyle.DoubleQuoted
            or OkfScalarStyle.Literal or OkfScalarStyle.Folded;

    /// <summary>Whether the scalar resolves to YAML null (<c>~</c>, <c>null</c>, or empty).</summary>
    public bool IsNull => !IsQuoted && Array.IndexOf(NullTexts, Value) >= 0;

    /// <inheritdoc />
    internal override bool IsTruthy
    {
        get
        {
            if (IsQuoted)
            {
                return Value.Length > 0;
            }

            return !IsNull && Array.IndexOf(FalseTexts, Value) < 0 && !IsZeroNumber(Value);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    // Deliberately narrower than PyYAML's YAML 1.1 int resolver: sexagesimal
    // (`1:30`) numbers are not recognized. They cannot be zero-valued in any
    // realistic frontmatter, and the only consumer of truthiness is the
    // non-empty-`type` conformance check (§11).
    private static bool IsZeroNumber(string text)
    {
        string unseparated = text.Replace("_", string.Empty, StringComparison.Ordinal);
        if (unseparated.Length == 0)
        {
            return false;
        }

        string digits = unseparated[0] is '+' or '-' ? unseparated[1..] : unseparated;
        if (HasRadixPrefix(digits))
        {
            return digits[2..].All(c => c == '0');
        }

        return SpellsAFloatPyYamlWouldResolve(digits)
            && double.TryParse(unseparated, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            && d == 0;
    }

    // PyYAML's YAML 1.1 int resolver spells octal as a bare leading zero (there is no
    // `0o` form) and takes only a lowercase `0x`/`0b` prefix, so `0o0`, `0X0` and `0B0`
    // are ordinary *strings*, not the number zero.
    private static bool HasRadixPrefix(string digits) =>
        digits.Length > 2 && digits[0] == '0' && digits[1] is 'x' or 'b';

    // PyYAML's YAML 1.1 float resolver requires a `.` in the mantissa and an explicitly
    // signed exponent, so `0e0` and `0e+0` are strings while `.0e+0` is the number zero.
    // .NET's parser is laxer than that. An exponent-free spelling is left to .NET.
    private static bool SpellsAFloatPyYamlWouldResolve(string digits)
    {
        int exponent = digits.IndexOfAny(['e', 'E']);
        if (exponent < 0)
        {
            return true;
        }

        int point = digits.IndexOf('.', StringComparison.Ordinal);
        return point >= 0
            && point < exponent
            && exponent + 1 < digits.Length
            && digits[exponent + 1] is '+' or '-';
    }
}

/// <summary>An ordered YAML sequence.</summary>
public sealed class OkfSequence : OkfValue, IEnumerable<OkfValue>
{
    private readonly List<OkfValue> _items = [];

    /// <summary>The sequence's presentation style.</summary>
    public OkfCollectionStyle Style { get; set; }

    /// <summary>The sequence's items, in order.</summary>
    public IList<OkfValue> Items => _items;

    /// <summary>The number of items in the sequence.</summary>
    public int Count => _items.Count;

    /// <inheritdoc />
    internal override bool IsTruthy => _items.Count > 0;

    /// <summary>Gets or sets the item at <paramref name="index" />.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The item at that index.</returns>
    public OkfValue this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <summary>Appends a value.</summary>
    /// <param name="value">The value to append.</param>
    public void Add(OkfValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _items.Add(value);
    }

    /// <summary>Appends a scalar with the given text.</summary>
    /// <param name="value">The scalar text to append.</param>
    public void Add(string value) => Add(Scalar(value));

    /// <inheritdoc />
    public IEnumerator<OkfValue> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// An ordered YAML mapping. Order is insertion order and is never sorted, so a
/// round trip does not alphabetize a producer's frontmatter (PRD CORE-2).
/// </summary>
public sealed class OkfMapping : OkfValue, IEnumerable<KeyValuePair<OkfValue, OkfValue>>
{
    private readonly List<KeyValuePair<OkfValue, OkfValue>> _entries = [];

    /// <summary>The mapping's presentation style.</summary>
    public OkfCollectionStyle Style { get; set; }

    /// <summary>The mapping's entries, in insertion order.</summary>
    public IReadOnlyList<KeyValuePair<OkfValue, OkfValue>> Entries => _entries;

    /// <summary>The number of entries in the mapping.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    internal override bool IsTruthy => _entries.Count > 0;

    /// <summary>
    /// Gets or sets the value for a scalar key. Reading an absent key yields
    /// <see langword="null" />; duplicate keys resolve last-wins, matching PyYAML.
    /// </summary>
    /// <param name="key">The scalar key's text.</param>
    /// <returns>The value, or <see langword="null" /> when the key is absent.</returns>
    public OkfValue? this[string key]
    {
        get => TryGetValue(key, out OkfValue? value) ? value : null;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            int index = LastIndexOf(key);
            if (index < 0)
            {
                Add(key, value);
            }
            else
            {
                _entries[index] = new KeyValuePair<OkfValue, OkfValue>(_entries[index].Key, value);
            }
        }
    }

    /// <summary>Appends an entry.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="value">The entry's value.</param>
    public void Add(OkfValue key, OkfValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _entries.Add(new KeyValuePair<OkfValue, OkfValue>(key, value));
    }

    /// <summary>Appends an entry with a scalar key.</summary>
    /// <param name="key">The entry's key text.</param>
    /// <param name="value">The entry's value.</param>
    public void Add(string key, OkfValue value) => Add(Scalar(key), value);

    /// <summary>Appends an entry with a scalar key and a scalar value.</summary>
    /// <param name="key">The entry's key text.</param>
    /// <param name="value">The entry's value text.</param>
    public void Add(string key, string value) => Add(Scalar(key), Scalar(value));

    /// <summary>Whether the mapping has an entry with the given scalar key.</summary>
    /// <param name="key">The scalar key's text.</param>
    /// <returns><see langword="true" /> when present.</returns>
    public bool ContainsKey(string key) => LastIndexOf(key) >= 0;

    /// <summary>Looks up the value for a scalar key, last-wins on duplicates.</summary>
    /// <param name="key">The scalar key's text.</param>
    /// <param name="value">The value when found.</param>
    /// <returns><see langword="true" /> when the key is present.</returns>
    public bool TryGetValue(string key, [NotNullWhen(true)] out OkfValue? value)
    {
        int index = LastIndexOf(key);
        if (index < 0)
        {
            value = null;
            return false;
        }

        value = _entries[index].Value;
        return true;
    }

    /// <summary>Removes every entry with the given scalar key.</summary>
    /// <param name="key">The scalar key's text.</param>
    /// <returns><see langword="true" /> when at least one entry was removed.</returns>
    public bool Remove(string key) =>
        _entries.RemoveAll(e => e.Key is OkfScalar s && string.Equals(s.Value, key, StringComparison.Ordinal)) > 0;

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<OkfValue, OkfValue>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int LastIndexOf(string key)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Key is OkfScalar scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
