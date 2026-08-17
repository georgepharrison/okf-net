using System.Globalization;
using Okf.Core;

namespace Okf.Cli.Bundle;

/// <summary>The parsed form of <c>okf bundle</c>'s command line.</summary>
internal sealed class BundleArguments
{
    private readonly List<string> _bundles = [];

    private BundleArguments()
    {
    }

    /// <summary>The explicit vault or bundle path, or <see langword="null" /> to discover one.</summary>
    public string? Path { get; private set; }

    /// <summary>Where the distribution is written.</summary>
    public string? Output { get; private set; }

    /// <summary>The shape to write, or <see langword="null" /> to infer it from <see cref="Output" />.</summary>
    public OkfDistributionFormat? Format { get; private set; }

    /// <summary>The bundle names to package; empty means every bundle in the working set.</summary>
    public IReadOnlyList<string> Bundles => _bundles;

    /// <summary>An archive or directory to re-hash against its own manifest, instead of packaging.</summary>
    public string? Verify { get; private set; }

    /// <summary>Whether to lint the packaged result the way a consumer would see it.</summary>
    public bool Lint { get; private set; }

    /// <summary>
    /// The instant to stamp the manifest with, or <see langword="null" /> for now. Pinning
    /// it is what makes a release artifact reproducible: it is the only clock reading in
    /// the packaging path.
    /// </summary>
    public DateTimeOffset? GeneratedAt { get; private set; }

    /// <summary>Whether to report resolution and the plan on stderr.</summary>
    public bool Verbose { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>Every option <see cref="Parse" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags =
    [
        "--help",
        "-h",
        "--verbose",
        "-v",
        "--lint",
        "--out",
        "-o",
        "--verify",
        "--bundle",
        "--format",
        "--generated-at",
    ];

    /// <summary>Parses <c>okf bundle</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>bundle</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, repeated, or contradicts another.</exception>
    public static BundleArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        BundleArguments parsed = new BundleArguments();

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            (string name, string? inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--verbose" or "-v":
                    parsed.Verbose = true;
                    break;

                case "--lint":
                    parsed.Lint = true;
                    break;

                case "--out" or "-o":
                    parsed.Output = Once(parsed.Output, name, inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--verify":
                    parsed.Verify = Once(parsed.Verify, name, inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--bundle":
                    parsed._bundles.Add(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--format":
                    parsed.Format = ParseFormat(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                case "--generated-at":
                    parsed.GeneratedAt = ParseInstant(inlineValue ?? CliArguments.Next(args, ref index, name));
                    break;

                default:
                    parsed.TakePath(argument);
                    break;
            }
        }

        parsed.Validate();
        return parsed;
    }

    /// <summary>The shape to write: what <c>--format</c> said, else what <c>--out</c> implies.</summary>
    /// <returns>The format.</returns>
    public OkfDistributionFormat EffectiveFormat() => Format ?? OkfBundler.FormatFor(Output ?? string.Empty);

    private static OkfDistributionFormat ParseFormat(string value) => value switch
    {
        "tar.gz" or "tgz" => OkfDistributionFormat.TarGz,
        "zip" => OkfDistributionFormat.Zip,
        "dir" or "directory" => OkfDistributionFormat.Directory,
        _ => throw new OkfConfigException(
            $"Unknown --format value '{value}'; expected 'tar.gz', 'zip', or 'dir'."),
    };

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset instant)
            ? instant
            : throw new OkfConfigException(
                $"'{value}' is not a readable instant; --generated-at takes an RFC 3339 timestamp " +
                "such as 2026-08-15T14:00:00Z.");

    private static string Once(string? existing, string option, string value) =>
        existing is null
            ? value
            : throw new OkfConfigException($"Option '{option}' was given twice ('{existing}' and '{value}').");

    private void TakePath(string argument)
    {
        if (argument.StartsWith('-') && argument.Length > 1)
        {
            throw new OkfConfigException($"Unknown option '{argument}'.");
        }

        if (Path is not null)
        {
            throw new OkfConfigException(
                $"`okf bundle` takes at most one path; got '{Path}' and '{argument}'.");
        }

        Path = argument;
    }

    private void Validate()
    {
        if (ShowHelp)
        {
            return;
        }

        if (Verify is not null)
        {
            RefuseVerifyConflicts();
            return;
        }

        if (Output is null)
        {
            throw new OkfConfigException(
                "`okf bundle` needs `--out <file-or-directory>` (or `--verify <archive-or-directory>`).");
        }
    }

    /// <summary>
    /// Verification reads a finished distribution; every packaging option would be
    /// describing a run that is not happening.
    /// </summary>
    private void RefuseVerifyConflicts()
    {
        List<string> conflicting = VerifyConflicts();
        if (conflicting.Count > 0)
        {
            throw new OkfConfigException(
                $"`--verify` checks a finished distribution and packages nothing, so it cannot be combined with " +
                $"{string.Join(" or ", conflicting)}.");
        }
    }

    private List<string> VerifyConflicts()
    {
        List<string> conflicting = new List<string>();
        AddConflict(conflicting, Output is not null, "--out");
        AddConflict(conflicting, Format is not null, "--format");
        AddConflict(conflicting, _bundles.Count > 0, "--bundle");
        AddConflict(conflicting, Lint, "--lint");
        AddConflict(conflicting, Path is not null, $"a path ('{Path}')");
        return conflicting;
    }

    private static void AddConflict(List<string> conflicting, bool present, string option)
    {
        if (present)
        {
            conflicting.Add(option);
        }
    }
}
