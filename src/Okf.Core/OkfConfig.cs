using System.Text.Json;

namespace Okf.Core;

/// <summary>
/// Raised when a configuration file, or a configuration override on the command line, is
/// malformed or names something okf-net does not ship. Callers map it to the CLI's
/// usage/environment exit code (PRD CLI-14, exit 2).
/// </summary>
public class OkfConfigException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OkfConfigException" /> class.</summary>
    public OkfConfigException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfConfigException" /> class.</summary>
    /// <param name="message">The error message.</param>
    public OkfConfigException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfConfigException" /> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public OkfConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One okf configuration file's contents. The global file lives at
/// <c>$XDG_CONFIG_HOME/okf/okf.json</c> (default <c>~/.config/okf/okf.json</c>, PRD
/// CLI-4); the project file lives at the vault root, <c>&lt;project&gt;/okf/okf.json</c>.
/// </summary>
/// <remarks>
/// Parsing goes through <see cref="JsonDocument" /> rather than a deserializer, so no
/// reflection is involved and the type is NativeAOT-safe (decisions.md Q10). Unknown
/// top-level keys are ignored for forward compatibility; unknown <em>rule</em> ids are
/// not, because a typo must never silently disable a rule (PRD CLI-6).
/// </remarks>
public sealed class OkfConfig
{
    private OkfConfig(string source)
    {
        Source = source;
        Severities = new OkfSeverityLayer(source);
    }

    /// <summary>The file this configuration came from, used in <c>--verbose</c> output.</summary>
    public string Source { get; }

    /// <summary>The severity settings this file contributes.</summary>
    public OkfSeverityLayer Severities { get; }

    /// <summary>
    /// The bundle's tag registry (<c>lint.tagRegistry</c>), or <see langword="null" />
    /// when the file does not define one. Without a registry the unregistered-tag rule
    /// (<c>OKF0305</c>) has nothing to check and stays silent.
    /// </summary>
    public IReadOnlyList<string>? TagRegistry { get; private set; }

    /// <summary>The <c>verify.actor</c> identity used by <c>okf verify</c> (PRD CLI-13, Q6).</summary>
    public string? VerifyActor { get; private set; }

    /// <summary>Reads a configuration file, returning <see langword="null" /> when it does not exist.</summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <returns>The parsed configuration, or <see langword="null" /> when there is no such file.</returns>
    /// <exception cref="OkfConfigException">The file exists but is not valid okf configuration.</exception>
    public static OkfConfig? TryLoad(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return File.Exists(path) ? Load(path) : null;
    }

    /// <summary>Reads a configuration file.</summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <returns>The parsed configuration.</returns>
    /// <exception cref="OkfConfigException">The file is missing or is not valid okf configuration.</exception>
    public static OkfConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new OkfConfigException($"Cannot read config file '{path}': {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new OkfConfigException($"Cannot read config file '{path}': {ex.Message}", ex);
        }

        return Parse(text, path);
    }

    /// <summary>Parses configuration text.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="source">How to describe the origin in messages and <c>--verbose</c> output.</param>
    /// <returns>The parsed configuration.</returns>
    /// <exception cref="OkfConfigException">The text is not valid okf configuration.</exception>
    public static OkfConfig Parse(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(source);

        var config = new OkfConfig(source);
        if (string.IsNullOrWhiteSpace(json))
        {
            return config;
        }

        using var document = ParseDocument(json, source);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new OkfConfigException($"Config file '{source}' must contain a JSON object at its root.");
        }

        if (root.TryGetProperty("lint", out var lint))
        {
            ReadLint(config, lint, source);
        }

        if (root.TryGetProperty("verify", out var verify))
        {
            if (verify.ValueKind != JsonValueKind.Object)
            {
                throw new OkfConfigException($"Config file '{source}': `verify` must be an object.");
            }

            if (verify.TryGetProperty("actor", out var actor))
            {
                config.VerifyActor = actor.ValueKind == JsonValueKind.String
                    ? actor.GetString()
                    : throw new OkfConfigException($"Config file '{source}': `verify.actor` must be a string.");
            }
        }

        return config;
    }

    private static JsonDocument ParseDocument(string json, string source)
    {
        try
        {
            return JsonDocument.Parse(
                json,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new OkfConfigException($"Config file '{source}' is not valid JSON: {ex.Message}", ex);
        }
    }

    private static void ReadLint(OkfConfig config, JsonElement lint, string source)
    {
        if (lint.ValueKind != JsonValueKind.Object)
        {
            throw new OkfConfigException($"Config file '{source}': `lint` must be an object.");
        }

        if (lint.TryGetProperty("severities", out var severities))
        {
            if (severities.ValueKind != JsonValueKind.Object)
            {
                throw new OkfConfigException($"Config file '{source}': `lint.severities` must be an object.");
            }

            foreach (var entry in severities.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String
                    || !OkfSeverityExtensions.TryParse(entry.Value.GetString(), out var severity))
                {
                    throw new OkfConfigException(
                        $"Config file '{source}': `lint.severities.{entry.Name}` must be one of " +
                        "\"hidden\", \"info\", \"warning\", \"error\".");
                }

                config.Severities.Severities[entry.Name] = severity;
            }
        }

        if (lint.TryGetProperty("treatAllWarningsAsErrors", out var promote))
        {
            config.Severities.TreatAllWarningsAsErrors = promote.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new OkfConfigException(
                    $"Config file '{source}': `lint.treatAllWarningsAsErrors` must be a boolean."),
            };
        }

        if (lint.TryGetProperty("tagRegistry", out var registry))
        {
            if (registry.ValueKind != JsonValueKind.Array)
            {
                throw new OkfConfigException($"Config file '{source}': `lint.tagRegistry` must be an array of strings.");
            }

            var tags = new List<string>();
            foreach (var tag in registry.EnumerateArray())
            {
                tags.Add(tag.ValueKind == JsonValueKind.String
                    ? tag.GetString()!
                    : throw new OkfConfigException(
                        $"Config file '{source}': `lint.tagRegistry` must be an array of strings."));
            }

            config.TagRegistry = tags;
        }
    }
}
