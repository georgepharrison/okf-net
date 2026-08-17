using System.Text.Json;

namespace Okf.Core.Documents;

/// <summary>
/// Reading the strict-JSON documents okf writes and reads back — <c>okf-bundle.json</c>,
/// <c>raw/manifest.json</c>, <c>latest.json</c> — which are read with
/// <see cref="JsonDocument" /> rather than a deserializer, because NativeAOT forbids
/// reflective serialization and a foreign document must be tolerated rather than rejected
/// (AD-4, AD-8).
/// </summary>
internal static class StrictJson
{
    /// <summary>
    /// The string under a property, or <see langword="null" /> when the property is absent
    /// or holds anything that is not a string. A document okf did not write is data, not a
    /// fault: a reader asks what is there and reports what it found.
    /// </summary>
    /// <param name="element">The object to read.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null" />.</returns>
    public static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
