using Okf.Core;

namespace Okf.Cli.Upgrade;

/// <summary>The parsed form of <c>okf upgrade</c>'s command line.</summary>
internal sealed class UpgradeArguments
{
    private UpgradeArguments()
    {
    }

    /// <summary>Whether to report only, exiting 1 when an upgrade is available.</summary>
    public bool Check { get; private set; }

    /// <summary>Whether to resolve and report without downloading anything.</summary>
    public bool DryRun { get; private set; }

    /// <summary>Whether the report is JSON.</summary>
    public bool Json { get; private set; }

    /// <summary>The version to pin to, without a leading <c>v</c>.</summary>
    public string? Version { get; private set; }

    /// <summary>The channel asked for.</summary>
    public OkfUpgradeChannel Channel { get; private set; }

    /// <summary>Whether <c>--channel</c> was given at all, which is what earns the reserved-channel note.</summary>
    public bool ChannelWasGiven { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
        ["--help", "-h", "--check", "--dry-run", "--json", "--format", "--version", "--channel"];

    /// <summary>Parses <c>okf upgrade</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>upgrade</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static UpgradeArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var parsed = new UpgradeArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--check":
                    parsed.Check = true;
                    break;

                case "--dry-run":
                    parsed.DryRun = true;
                    break;

                case "--json":
                    parsed.Json = true;
                    break;

                case "--format":
                    parsed.Json = CliArguments.ParseFormat(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--version":
                    if (parsed.Version is not null)
                    {
                        throw new OkfConfigException("Option '--version' was given more than once.");
                    }

                    // A leading `v` is stripped here and nowhere else, exactly as install.sh
                    // does it: a person reads `v1.0.0` off a tag or a release page, and the
                    // manifest path is built from the bare version.
                    parsed.Version = (inlineValue ?? CliArguments.Next(args, ref index, name)).TrimStart('v');
                    if (parsed.Version.Length == 0)
                    {
                        throw new OkfConfigException("Option '--version' requires a value.");
                    }

                    break;

                case "--channel":
                    if (parsed.ChannelWasGiven)
                    {
                        throw new OkfConfigException("Option '--channel' was given more than once.");
                    }

                    parsed.Channel = (inlineValue ?? CliArguments.Next(args, ref index, name)) switch
                    {
                        "stable" => OkfUpgradeChannel.Stable,
                        "rc" => OkfUpgradeChannel.Rc,
                        var value => throw new OkfConfigException(
                            $"Unknown --channel value '{value}'; expected 'stable' or 'rc'."),
                    };
                    parsed.ChannelWasGiven = true;
                    break;

                default:
                    throw new OkfConfigException($"Unknown option '{argument}'.");
            }
        }

        if (parsed.ShowHelp)
        {
            return parsed;
        }

        // `--version X --channel Y` is a contradiction, not a refinement: a pinned version
        // names one release directory, and the channel would decide nothing.
        if (parsed.Version is not null && parsed.ChannelWasGiven)
        {
            throw new OkfConfigException(
                "'--version' pins one release, so '--channel' has nothing left to choose; give one or the other.");
        }

        return parsed;
    }
}
