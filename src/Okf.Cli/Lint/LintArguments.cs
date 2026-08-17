using Okf.Core;

namespace Okf.Cli.Lint;

/// <summary>The parsed form of <c>okf lint</c>'s command line.</summary>
internal sealed class LintArguments
{
    private LintArguments()
    {
    }

    /// <summary>The explicit target path, or <see langword="null" /> to discover one.</summary>
    public string? Path { get; private set; }

    /// <summary>Whether to emit the stable JSON array instead of human-readable lines.</summary>
    public bool Json { get; private set; }

    /// <summary>Whether to report vault resolution and effective configuration.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Whether the command should list the rule catalog and stop.</summary>
    public bool ListRules { get; private set; }

    /// <summary>An explicit project config file, overriding the vault's.</summary>
    public string? ConfigPath { get; private set; }

    /// <summary>The severity layer the command line itself contributes — the highest-precedence layer.</summary>
    public OkfSeverityLayer CommandLine { get; } = new("command line");

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
    [
        "--help",
        "-h",
        "--list-rules",
        "--verbose",
        "-v",
        "--json",
        "--format",
        "--severity",
        "--treat-all-warnings-as-errors",
        "--config",
    ];

    /// <summary>Parses <c>okf lint</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>lint</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static LintArguments Parse(string[] args)
    {
        var parsed = new LintArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--list-rules":
                    parsed.ListRules = true;
                    break;

                case "--verbose" or "-v":
                    parsed.Verbose = true;
                    break;

                case "--json":
                    parsed.Json = true;
                    break;

                case "--format":
                    var format = inlineValue ?? CliArguments.Next(args, ref index, name);
                    parsed.Json = CliArguments.ParseFormat(format);
                    break;

                case "--severity":
                    ApplySeverity(parsed, inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--treat-all-warnings-as-errors":
                    parsed.CommandLine.TreatAllWarningsAsErrors = true;
                    break;

                case "--config":
                    parsed.ConfigPath = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OkfConfigException($"Unknown option '{argument}'.");
                    }

                    if (parsed.Path is not null)
                    {
                        throw new OkfConfigException(
                            $"`okf lint` takes at most one path; got '{parsed.Path}' and '{argument}'.");
                    }

                    parsed.Path = argument;
                    break;
            }
        }

        return parsed;
    }

    private static void ApplySeverity(LintArguments parsed, string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new OkfConfigException(
                $"--severity expects <OKF####>=<hidden|info|warning|error>; got '{value}'.");
        }

        var id = value[..separator];
        if (!OkfSeverityExtensions.TryParse(value[(separator + 1)..], out var severity))
        {
            throw new OkfConfigException(
                $"--severity expects <OKF####>=<hidden|info|warning|error>; got '{value}'.");
        }

        // An unknown id is rejected by OkfSeverityResolver, together with the ones config
        // files contribute, so a typo never silently disables a rule (PRD CLI-6).
        parsed.CommandLine.Severities[id] = severity;
    }
}
