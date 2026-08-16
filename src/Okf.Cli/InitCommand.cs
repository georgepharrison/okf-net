using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// <c>okf init [path]</c> — scaffolds the vault layout decisions.md §2 describes, writing
/// only the files that are missing (PRD CLI-8, CLI-14).
/// </summary>
internal static class InitCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>init</c>.</param>
    /// <param name="environment">The environment to resolve the target against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        InitArguments parsed;
        try
        {
            parsed = InitArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf init --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Initialize(parsed, environment, output, error);
        }
        catch (OkfScaffoldException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
        catch (IOException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
        catch (UnauthorizedAccessException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
    }

    private static int Initialize(
        InitArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        var vault = arguments.Personal
            ? environment.PersonalVault
            : OkfScaffold.ResolveVault(arguments.Path, environment);

        if (arguments.Verbose)
        {
            error.WriteLine(arguments.Personal
                ? $"okf: personal vault '{vault}' (OKF_HOME, else ~/okf)"
                : $"okf: vault '{vault}'");
        }

        var result = OkfScaffold.Initialize(
            vault,
            new OkfScaffoldOptions
            {
                // A project vault's bundle takes the project directory's name, which is
                // the name a reader already associates with the knowledge. The personal
                // vault has no such name — its parent is the home directory, so deriving
                // one there would put the machine's account name on the bundle.
                BundleName = arguments.Name ?? (arguments.Personal ? OkfScaffold.PersonalBundleName : null),
                Now = DateTimeOffset.UtcNow,

                // The tool wrote these files, and `generated.by` says so: a scaffolded
                // concept is machine-written and unverified, and attributing it to whoever
                // happened to run the command would put a human actor on prose no human
                // has read (decisions.md §7).
                Actor = $"okf/{CliApplication.Version}",
            });

        foreach (var file in result.Files)
        {
            output.WriteLine(
                $"{DiagnosticWriter.Display(file.Path, environment.CurrentDirectory)}: {Verb(file.Status)}");
        }

        var bundle = Path.GetFileName(result.BundleRoot);
        var vaultDisplay = DiagnosticWriter.Display(result.VaultRoot, environment.CurrentDirectory);

        if (result.IsNoOp)
        {
            output.WriteLine(
                $"Vault {vaultDisplay} (bundle {bundle}) is already initialized: " +
                $"{DiagnosticWriter.Plural(result.ExistingCount, "file")} already present, 0 written.");
            return CliApplication.ExitSuccess;
        }

        output.WriteLine(
            $"Initialized vault {vaultDisplay} (bundle {bundle}): " +
            $"{DiagnosticWriter.Plural(result.CreatedCount, "file")} written, " +
            $"{result.ExistingCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} already present.");
        output.WriteLine($"Run `okf lint {vaultDisplay}` to check it.");

        // The recipe names its skills as instructions when this project has no copy on
        // disk, and an instruction nobody is told how to follow is a dangling pointer with
        // better manners. Said here, at the one moment the file was written.
        var unresolved = result.SkillPointers.Where(pointer => !pointer.IsPath).ToArray();
        if (unresolved.Length > 0)
        {
            output.WriteLine(
                $"The custodian recipe names {string.Join(" and ", unresolved.Select(pointer => pointer.Name))} " +
                "as instructions rather than paths: this project has no skills/ of its own.");
            output.WriteLine(
                "Run `okf skills install` to put them on this machine — they ship inside this binary — " +
                $"and `okf skills path {unresolved[0].Name}` to print where one landed.");
        }

        return CliApplication.ExitSuccess;
    }

    private static string Verb(OkfScaffoldStatus status) => status switch
    {
        OkfScaffoldStatus.Created => "created",
        _ => "exists, left as found",
    };

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf init [path] [options]

            Scaffolds an OKF vault: a repo-facing README.md outside every bundle root, one
            bundle with a conformant `about-this-bundle.md` and a generated index.md, the
            raw/ drop zone with its capture manifest, the custodian directory, and the
            project config that is your team's committed lint contract.

            Nothing is ever overwritten. Every file is written only when it is missing, so
            running init again on an initialized vault reports what is there, changes
            nothing, and exits 0.

            It refuses to initialize inside a bundle root. init writes a README.md at the
            vault root, and OKF v0.2 does not reserve that name — inside a bundle root it
            would be read as a frontmatter-less concept and fail §11 conformance.

            Arguments:
              path                          A project root; its vault is <path>/okf. A
                                            directory that already holds bundles/ is taken
                                            as the vault itself. Defaults to the working
                                            directory.

            Options:
              --name <bundle>               The bundle directory's name (default: the
                                            project directory's name)
              --personal                    Initialize the personal vault (OKF_HOME, else
                                            ~/okf) instead of a project vault
              --verbose, -v                 Report the vault the target resolved to
              --help, -h                    Show this help

            Exit codes:
              0  the vault is initialized, whether this run wrote anything or not
              2  usage failure, or a vault that must not be scaffolded there
            """);
    }
}
