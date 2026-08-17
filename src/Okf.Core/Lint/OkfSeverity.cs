namespace Okf.Core.Lint;

/// <summary>
/// How loudly a diagnostic is reported. okf-net adopts Roslyn's four-level model for
/// every diagnostic (decisions.md Q5, PRD CLI-6); any rule is reconfigurable to any
/// level, with no exemptions.
/// </summary>
public enum OkfSeverity
{
    /// <summary>
    /// Computed but never printed and never affecting the exit code — the opt-in
    /// position for rules that are off by default.
    /// </summary>
    Hidden = 0,

    /// <summary>Reported, but informational: never affects the exit code.</summary>
    Info,

    /// <summary>Reported as a warning: never affects the exit code unless promoted.</summary>
    Warning,

    /// <summary>Reported as an error: the command exits non-zero.</summary>
    Error,
}

/// <summary>Conversions for <see cref="OkfSeverity" />.</summary>
public static class OkfSeverityExtensions
{
    /// <summary>
    /// Renders the severity as the lowercase token used in configuration files, CLI
    /// flags, and diagnostic output.
    /// </summary>
    /// <param name="severity">The severity to render.</param>
    /// <returns>One of <c>hidden</c>, <c>info</c>, <c>warning</c>, <c>error</c>.</returns>
    public static string ToConfigString(this OkfSeverity severity) => severity switch
    {
        OkfSeverity.Hidden => "hidden",
        OkfSeverity.Info => "info",
        OkfSeverity.Warning => "warning",
        OkfSeverity.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    /// <summary>Parses a configuration token into a severity, case-insensitively.</summary>
    /// <param name="text">The token, e.g. <c>warning</c>.</param>
    /// <param name="severity">The parsed severity when the token is recognized.</param>
    /// <returns><see langword="true" /> when the token is one of the four severities.</returns>
    public static bool TryParse(string? text, out OkfSeverity severity)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "hidden":
            case "none":
            case "off":
                severity = OkfSeverity.Hidden;
                return true;
            case "info":
            case "suggestion":
                severity = OkfSeverity.Info;
                return true;
            case "warning":
            case "warn":
                severity = OkfSeverity.Warning;
                return true;
            case "error":
                severity = OkfSeverity.Error;
                return true;
            default:
                severity = OkfSeverity.Hidden;
                return false;
        }
    }
}
