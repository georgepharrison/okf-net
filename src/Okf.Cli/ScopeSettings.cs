using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// The effective search scope and the configuration layer that set it (PRD CLI-3, CLI-4).
/// One helper rather than two, because <c>okf search</c> and <c>okf mcp</c> must agree
/// about scope down to the layer they read it from (PRD MCP-3).
/// </summary>
/// <param name="Scope">The scope in force.</param>
/// <param name="Layer">Where it came from, as <c>--verbose</c> reports it.</param>
internal sealed record ScopeSettings(OkfScopeKind Scope, string Layer)
{
    /// <summary>The layer name reported when nothing set a scope.</summary>
    public const string DefaultsLayerName = "built-in defaults";

    /// <summary>
    /// Resolves the scope through AD-31's chain: built-in default (<c>project</c>) →
    /// global config → project config → the command line.
    /// </summary>
    /// <param name="commandLine">The <c>--scope</c> flag, or <see langword="null" />.</param>
    /// <param name="environment">The environment configuration resolves against.</param>
    /// <returns>The effective scope and its layer.</returns>
    /// <exception cref="OkfConfigException">A configuration file is malformed.</exception>
    public static ScopeSettings Resolve(OkfScopeKind? commandLine, OkfEnvironment environment)
    {
        if (commandLine is { } flag)
        {
            return new ScopeSettings(flag, "command line");
        }

        // The project config sits at the vault root, so finding it means resolving the
        // project scope first. An unresolvable project vault is not an error here — it just
        // means there is no project layer to read.
        string? projectConfigPath = null;
        try
        {
            projectConfigPath = OkfDiscovery.Resolve(null, environment).ProjectConfigPath;
        }
        catch (OkfDiscoveryException)
        {
            // No project vault; the global layer and the default still apply.
        }

        var project = projectConfigPath is null ? null : OkfConfig.TryLoad(projectConfigPath);
        if (project?.SearchScope is { } fromProject)
        {
            return new ScopeSettings(fromProject, project.Source);
        }

        var global = OkfConfig.TryLoad(environment.GlobalConfigPath, globalLayer: true);
        return global?.SearchScope is { } fromGlobal
            ? new ScopeSettings(fromGlobal, global.Source)
            : new ScopeSettings(OkfScopeKind.Project, DefaultsLayerName);
    }
}
