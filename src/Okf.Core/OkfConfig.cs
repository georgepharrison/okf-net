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
    private OkfConfig(string source, bool globalLayer)
    {
        Source = source;
        IsGlobalLayer = globalLayer;
        Severities = new OkfSeverityLayer(source);
    }

    /// <summary>The file this configuration came from, used in <c>--verbose</c> output.</summary>
    public string Source { get; }

    /// <summary>
    /// Whether this file is the global, per-machine layer. Settings that describe the
    /// machine rather than the work — <c>autoRegister</c> — are accepted there and refused
    /// in a committed project file, because a repository must not be able to change what
    /// another person's machine does behind their back.
    /// </summary>
    public bool IsGlobalLayer { get; }

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

    /// <summary>
    /// The <c>search.scope</c> setting — which vaults <c>okf search</c> and <c>okf mcp</c>
    /// look at (PRD CLI-3) — or <see langword="null" /> when this file does not speak to
    /// it. Settable in either layer, project over global, and a <c>--scope</c> flag over
    /// both (AD-31).
    /// </summary>
    public OkfScopeKind? SearchScope { get; private set; }

    /// <summary>
    /// The <c>autoRegister</c> setting, accepted in the global file only, or
    /// <see langword="null" /> when unset. <b>Recorded and validated; no behaviour is
    /// attached</b> — nothing in okf-net auto-registers anything, and registration stays
    /// the explicit <c>okf register</c> (decisions.md §6, PRD CLI-2).
    /// </summary>
    public bool? AutoRegister { get; private set; }

    /// <summary>Reads a configuration file, returning <see langword="null" /> when it does not exist.</summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <param name="globalLayer">Whether this is the per-machine global file.</param>
    /// <returns>The parsed configuration, or <see langword="null" /> when there is no such file.</returns>
    /// <exception cref="OkfConfigException">The file exists but is not valid okf configuration.</exception>
    public static OkfConfig? TryLoad(string path, bool globalLayer = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return File.Exists(path) ? Load(path, globalLayer) : null;
    }

    /// <summary>Reads a configuration file.</summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <param name="globalLayer">Whether this is the per-machine global file.</param>
    /// <returns>The parsed configuration.</returns>
    /// <exception cref="OkfConfigException">The file is missing or is not valid okf configuration.</exception>
    public static OkfConfig Load(string path, bool globalLayer = false)
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

        return Parse(text, path, globalLayer);
    }

    /// <summary>Parses configuration text.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="source">How to describe the origin in messages and <c>--verbose</c> output.</param>
    /// <param name="globalLayer">Whether this is the per-machine global file.</param>
    /// <returns>The parsed configuration.</returns>
    /// <exception cref="OkfConfigException">The text is not valid okf configuration.</exception>
    public static OkfConfig Parse(string json, string source, bool globalLayer = false)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(source);

        var config = new OkfConfig(source, globalLayer);
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

        if (root.TryGetProperty("search", out var search))
        {
            ReadSearch(config, search, source);
        }

        if (root.TryGetProperty("autoRegister", out var autoRegister))
        {
            ReadAutoRegister(config, autoRegister, source, globalLayer);
        }

        return config;
    }

    private static void ReadSearch(OkfConfig config, JsonElement search, string source)
    {
        if (search.ValueKind != JsonValueKind.Object)
        {
            throw new OkfConfigException($"Config file '{source}': `search` must be an object.");
        }

        if (!search.TryGetProperty("scope", out var scope))
        {
            return;
        }

        if (scope.ValueKind != JsonValueKind.String || !OkfScopeKindExtensions.TryParse(scope.GetString(), out var parsed))
        {
            throw new OkfConfigException(
                $"Config file '{source}': `search.scope` must be one of " +
                string.Join(", ", OkfScopeKindExtensions.Names.Select(name => $"\"{name}\"")) + ".");
        }

        config.SearchScope = parsed;
    }

    private static void ReadAutoRegister(OkfConfig config, JsonElement autoRegister, string source, bool globalLayer)
    {
        if (!globalLayer)
        {
            // A committed project file that could turn auto-registration on would let a
            // repository write to a contributor's machine-wide registry by being cloned.
            throw new OkfConfigException(
                $"Config file '{source}': `autoRegister` is a global setting and is not read from a project " +
                "config. Move it to okf's global config file.");
        }

        config.AutoRegister = autoRegister.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new OkfConfigException($"Config file '{source}': `autoRegister` must be a boolean."),
        };
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
