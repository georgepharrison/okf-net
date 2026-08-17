namespace Okf.Core.Lint;

/// <summary>
/// One layer of severity configuration — the built-in defaults, the global config file,
/// the project config file, or the CLI arguments. Layers are combined by
/// <see cref="OkfSeverityResolver" /> in precedence order (PRD CLI-4).
/// </summary>
public sealed class OkfSeverityLayer
{
    /// <summary>Initializes a layer.</summary>
    /// <param name="name">
    /// How the layer is described in <c>--verbose</c> output, e.g. a config file path or
    /// <c>command line</c>.
    /// </param>
    public OkfSeverityLayer(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>How the layer is described in <c>--verbose</c> output.</summary>
    public string Name { get; }

    /// <summary>Per-rule severity overrides, keyed by <c>OKF####</c> identifier.</summary>
    public IDictionary<string, OkfSeverity> Severities { get; } =
        new Dictionary<string, OkfSeverity>(StringComparer.Ordinal);

    /// <summary>
    /// Whether this layer promotes every rule that resolves to
    /// <see cref="OkfSeverity.Warning" /> to <see cref="OkfSeverity.Error" />;
    /// <see langword="null" /> when the layer does not speak to it.
    /// </summary>
    public bool? TreatAllWarningsAsErrors { get; set; }

    /// <summary>Whether the layer carries no settings at all.</summary>
    public bool IsEmpty => Severities.Count == 0 && TreatAllWarningsAsErrors is null;
}

/// <summary>
/// Resolves the effective severity of every rule from an ordered stack of configuration
/// layers: built-in defaults, then each supplied layer in increasing precedence (PRD
/// CLI-4, CLI-6). <c>OKF_HOME</c> is deliberately absent — it moves the personal vault
/// and never changes a severity.
/// </summary>
public sealed class OkfSeverityResolver
{
    /// <summary>The layer name reported for a rule no configuration layer touched.</summary>
    public const string DefaultsLayerName = "built-in defaults";

    private readonly List<OkfSeverityLayer> layers;

    /// <summary>Initializes a resolver.</summary>
    /// <param name="layers">
    /// The configuration layers, lowest precedence first. A later layer overrides an
    /// earlier one for the rules it names; rules it does not name keep the earlier value.
    /// </param>
    /// <exception cref="OkfConfigException">A layer names a rule identifier okf-net does not ship (PRD CLI-6).</exception>
    public OkfSeverityResolver(IEnumerable<OkfSeverityLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        this.layers = [.. layers];

        var unknown = this.layers
            .SelectMany(layer => layer.Severities.Keys.Select(id => (Layer: layer, Id: id)))
            .Where(entry => !OkfRules.IsKnown(entry.Id))
            .ToList();

        if (unknown.Count > 0)
        {
            var detail = string.Join(
                "; ",
                unknown.Select(entry => $"'{entry.Id}' (from {entry.Layer.Name})"));
            throw new OkfConfigException(
                $"Unknown diagnostic id in severity configuration: {detail}. " +
                "A typo must not silently disable a rule; use an OKF#### id from `okf lint --list-rules`.");
        }
    }

    /// <summary>A resolver with no configuration: every rule keeps its built-in default.</summary>
    public static OkfSeverityResolver Default { get; } = new([]);

    /// <summary>Whether warnings are promoted to errors, after all layers are applied.</summary>
    public bool TreatAllWarningsAsErrors =>
        this.layers.LastOrDefault(layer => layer.TreatAllWarningsAsErrors is not null)?.TreatAllWarningsAsErrors
        ?? false;

    /// <summary>The layer that set <see cref="TreatAllWarningsAsErrors" />.</summary>
    public string TreatAllWarningsAsErrorsSource =>
        this.layers.LastOrDefault(layer => layer.TreatAllWarningsAsErrors is not null)?.Name ?? DefaultsLayerName;

    /// <summary>Resolves the effective severity of a rule.</summary>
    /// <param name="ruleId">The <c>OKF####</c> identifier.</param>
    /// <returns>The severity to report the rule at.</returns>
    /// <exception cref="ArgumentException">No such rule is shipped.</exception>
    public OkfSeverity Resolve(string ruleId) => ResolveWithSource(ruleId).Severity;

    /// <summary>Resolves a rule's severity together with the layer that decided it.</summary>
    /// <param name="ruleId">The <c>OKF####</c> identifier.</param>
    /// <returns>The effective severity and the name of the layer that set it.</returns>
    /// <exception cref="ArgumentException">No such rule is shipped.</exception>
    public (OkfSeverity Severity, string Source) ResolveWithSource(string ruleId)
    {
        var rule = OkfRules.Get(ruleId);
        var severity = rule.DefaultSeverity;
        var source = DefaultsLayerName;

        foreach (var layer in this.layers)
        {
            if (layer.Severities.TryGetValue(ruleId, out var configured))
            {
                severity = configured;
                source = layer.Name;
            }
        }

        // `treatAllWarningsAsErrors` promotes what is *currently* at warning severity, so
        // a rule explicitly demoted to info or hidden stays where the consumer put it, and
        // broken internal links (info by default) are untouched (PRD CLI-6).
        if (severity == OkfSeverity.Warning && TreatAllWarningsAsErrors)
        {
            return (OkfSeverity.Error, $"{TreatAllWarningsAsErrorsSource} (treatAllWarningsAsErrors)");
        }

        return (severity, source);
    }
}
