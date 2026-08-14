namespace Okf.Core;

/// <summary>
/// The process environment discovery and configuration read from: the working directory,
/// <c>OKF_HOME</c>, and the home/config directories. Injected rather than read from
/// statics so vault resolution is testable without mutating the real process (PRD
/// CORE-13).
/// </summary>
public sealed class OkfEnvironment
{
    /// <summary>The environment variable that overrides the personal vault location (decisions.md §6).</summary>
    public const string HomeVariable = "OKF_HOME";

    private readonly Dictionary<string, string> variables;

    /// <summary>Initializes an environment.</summary>
    /// <param name="currentDirectory">The working directory commands resolve from.</param>
    /// <param name="variables">The environment variables visible to okf-net.</param>
    public OkfEnvironment(string currentDirectory, IEnumerable<KeyValuePair<string, string>>? variables = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentDirectory);
        CurrentDirectory = System.IO.Path.GetFullPath(currentDirectory);
        this.variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in variables ?? [])
        {
            this.variables[entry.Key] = entry.Value;
        }
    }

    /// <summary>The working directory commands resolve from.</summary>
    public string CurrentDirectory { get; }

    /// <summary>
    /// The user's home directory: <c>HOME</c> when set, else the platform's user-profile
    /// folder.
    /// </summary>
    public string HomeDirectory =>
        GetVariable("HOME") is { Length: > 0 } home
            ? home
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// okf's configuration directory: <c>$XDG_CONFIG_HOME/okf</c> when set, else
    /// <c>~/.config/okf</c> (PRD CLI-4).
    /// </summary>
    public string ConfigDirectory =>
        GetVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? System.IO.Path.Combine(xdg, "okf")
            : System.IO.Path.Combine(HomeDirectory, ".config", "okf");

    /// <summary>The global configuration file, <c>okf.json</c> in <see cref="ConfigDirectory" />.</summary>
    public string GlobalConfigPath => System.IO.Path.Combine(ConfigDirectory, OkfDiscovery.ConfigFileName);

    /// <summary>
    /// The personal vault: <c>OKF_HOME</c> when set, else <c>~/okf</c> — visible, not
    /// hidden (decisions.md §6). <c>OKF_HOME</c> moves the vault only; it never affects
    /// diagnostic severity.
    /// </summary>
    public string PersonalVault =>
        GetVariable(HomeVariable) is { Length: > 0 } okfHome
            ? System.IO.Path.GetFullPath(okfHome)
            : System.IO.Path.Combine(HomeDirectory, "okf");

    /// <summary>Captures the real process environment.</summary>
    /// <returns>An environment reading the current process's working directory and variables.</returns>
    public static OkfEnvironment FromProcess()
    {
        var variables = new List<KeyValuePair<string, string>>();
        foreach (var name in (string[])["HOME", "XDG_CONFIG_HOME", HomeVariable, "USERPROFILE"])
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                variables.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        return new OkfEnvironment(Directory.GetCurrentDirectory(), variables);
    }

    /// <summary>Reads one environment variable.</summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>Its value, or <see langword="null" /> when unset.</returns>
    public string? GetVariable(string name) => this.variables.GetValueOrDefault(name);
}
