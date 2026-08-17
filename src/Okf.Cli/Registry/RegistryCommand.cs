using System.Globalization;
using Okf.Core;

namespace Okf.Cli.Registry;

/// <summary>
/// <c>okf register</c>, <c>okf unregister</c> and <c>okf registry list|prune</c> — the
/// explicit, idempotent registry (PRD CLI-2). Nothing else writes the registry file, and
/// nothing auto-registers: a vault enters a person's scope because they said so.
/// </summary>
internal static class RegistryCommand
{
    /// <summary>Runs <c>okf register [path]</c>.</summary>
    /// <param name="args">The arguments after <c>register</c>.</param>
    /// <param name="environment">The environment vaults and the registry resolve against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Register(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error) =>
        Run(args, "register", subcommands: false, environment, output, error, Register);

    /// <summary>Runs <c>okf unregister [path|id]</c>.</summary>
    /// <param name="args">The arguments after <c>unregister</c>.</param>
    /// <param name="environment">The environment vaults and the registry resolve against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Unregister(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error) =>
        Run(args, "unregister", subcommands: false, environment, output, error, Unregister);

    /// <summary>Runs <c>okf registry list|prune</c>.</summary>
    /// <param name="args">The arguments after <c>registry</c>.</param>
    /// <param name="environment">The environment the registry resolves against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Registry(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error) =>
        Run(args, "registry", subcommands: true, environment, output, error, Inspect);

    private static int Run(
        string[] args,
        string verb,
        bool subcommands,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error,
        Func<RegistryArguments, OkfEnvironment, TextWriter, TextWriter, int> action)
    {
        if (Parse(args, verb, subcommands, error) is not { } parsed)
        {
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(verb, output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            if (parsed.Verbose)
            {
                WriteVerbose(environment, error);
            }

            return action(parsed, environment, output, error);
        }
        catch (Exception exception)
            when (exception is OkfConfigException or OkfDiscoveryException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
    }

    private static RegistryArguments? Parse(string[] args, string verb, bool subcommands, TextWriter error)
    {
        try
        {
            return RegistryArguments.Parse(args, verb, subcommands);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine($"Run `okf {verb} --help` for usage.");
            return null;
        }
    }

    private static int Register(
        RegistryArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        OkfRegistry registry = OkfRegistry.Load(environment);
        string target = Target(arguments, environment);
        (OkfRegistryEntry entry, bool added) = registry.Register(target, DateTimeOffset.Now);

        if (!added)
        {
            // Idempotent by contract (PRD CLI-2): re-registering a known path succeeds and
            // says so, so a setup script can run twice.
            output.WriteLine($"Already registered as '{entry.Id}' ({entry.Kind.ToRegistryString()}): {entry.Path}");
            return CliApplication.ExitSuccess;
        }

        registry.Save(OkfRegistry.PathFor(environment));
        output.WriteLine($"Registered '{entry.Id}' ({entry.Kind.ToRegistryString()}): {entry.Path}");
        WriteScopeHint(environment, output);
        return CliApplication.ExitSuccess;
    }

    private static int Unregister(
        RegistryArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        OkfRegistry registry = OkfRegistry.Load(environment);
        string target = arguments.Target ?? Target(arguments, environment);
        OkfRegistryEntry? removed = registry.Unregister(target, environment.CurrentDirectory);

        if (removed is null)
        {
            // Also idempotent: removing what is not there is the state the caller asked for.
            output.WriteLine($"Nothing to unregister: '{target}' is not in the registry.");
            return CliApplication.ExitSuccess;
        }

        registry.Save(OkfRegistry.PathFor(environment));
        output.WriteLine($"Unregistered '{removed.Id}' ({removed.Kind.ToRegistryString()}): {removed.Path}");
        return CliApplication.ExitSuccess;
    }

    private static int Inspect(
        RegistryArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error) =>
        arguments.Action == RegistryAction.Prune
            ? Prune(environment, output)
            : List(arguments, environment, output);

    private static int List(RegistryArguments arguments, OkfEnvironment environment, TextWriter output)
    {
        OkfRegistry registry = OkfRegistry.Load(environment);
        string path = OkfRegistry.PathFor(environment);

        if (arguments.Json)
        {
            output.Write(ToJson(registry));
            output.Write(Environment.NewLine);
            return CliApplication.ExitSuccess;
        }

        if (registry.Entries.Count == 0)
        {
            output.WriteLine($"No entries in '{path}'. Add one with `okf register [path]`.");
            return CliApplication.ExitSuccess;
        }

        WriteEntries(registry, path, output);
        return CliApplication.ExitSuccess;
    }

    private static void WriteEntries(OkfRegistry registry, string path, TextWriter output)
    {
        int width = registry.Entries.Max(entry => entry.Id.Length);
        int missing = 0;
        foreach (OkfRegistryEntry entry in registry.Entries)
        {
            // Whether the path still exists is reported, never repaired: a lint rule that
            // rewrote machine-wide state would be a rule with a side effect (AD-7).
            bool present = entry.Exists;
            missing += present ? 0 : 1;
            output.WriteLine(
                $"{entry.Id.PadRight(width)}  {entry.Kind.ToRegistryString().PadRight(6)}  {entry.Path}" +
                (present ? string.Empty : "  (missing)"));
        }

        output.WriteLine(ListSummary(registry, path, missing));
    }

    private static string ListSummary(OkfRegistry registry, string path, int missing) =>
        $"{DiagnosticWriter.Plural(registry.Entries.Count, "entry", "entries")} in '{path}'" +
        (missing == 0
            ? "."
            : $"; {DiagnosticWriter.Plural(missing, "path")} missing — `okf registry prune` removes " +
              (missing == 1 ? "it." : "them."));

    private static int Prune(OkfEnvironment environment, TextWriter output)
    {
        OkfRegistry registry = OkfRegistry.Load(environment);
        IReadOnlyList<OkfRegistryEntry> removed = registry.Prune();

        if (removed.Count == 0)
        {
            output.WriteLine(
                $"Nothing to prune: {DiagnosticWriter.Plural(registry.Entries.Count, "entry", "entries")} " +
                "in the registry, all present.");
            return CliApplication.ExitSuccess;
        }

        registry.Save(OkfRegistry.PathFor(environment));
        foreach (OkfRegistryEntry entry in removed)
        {
            output.WriteLine($"Pruned '{entry.Id}' ({entry.Kind.ToRegistryString()}): {entry.Path}");
        }

        output.WriteLine(
            $"Pruned {DiagnosticWriter.Plural(removed.Count, "entry", "entries")}; " +
            $"{DiagnosticWriter.Plural(registry.Entries.Count, "entry", "entries")} left.");
        return CliApplication.ExitSuccess;
    }

    /// <summary>
    /// What <c>okf register</c> and <c>okf unregister</c> act on with no argument: the vault
    /// CLI-1 already resolves — the project vault by walk-up, and the personal vault when
    /// there is none. The personal vault is registered by exactly this path and gets an
    /// ordinary entry; nothing about it is privileged (decisions.md §6).
    /// </summary>
    private static string Target(RegistryArguments arguments, OkfEnvironment environment) =>
        arguments.Target is { Length: > 0 } given
            ? Path.GetFullPath(Path.Combine(environment.CurrentDirectory, given))
            : OkfDiscovery.Resolve(null, environment).VaultRoot
              ?? throw new OkfDiscoveryException(
                  "Resolved a bundle rather than a vault; pass the path to register explicitly.");

    private static void WriteScopeHint(OkfEnvironment environment, TextWriter output) =>
        output.WriteLine(
            "Search still defaults to the project vault. Use `okf search <query> --scope registered` (or " +
            $"`--scope all`), or set `search.scope` in '{environment.GlobalConfigPath}'.");

    private static void WriteVerbose(OkfEnvironment environment, TextWriter error)
    {
        error.WriteLine($"okf: registry {OkfRegistry.PathFor(environment)}");

        OkfConfig? global = OkfConfig.TryLoad(environment.GlobalConfigPath, globalLayer: true);
        if (global?.AutoRegister is { } autoRegister)
        {
            // Recorded and validated, with no behaviour attached: registration is explicit,
            // and a setting that silently widened a person's scope is the thing CLI-2 turned
            // off by default in the first place.
            error.WriteLine(
                $"okf: autoRegister {autoRegister.ToString().ToLowerInvariant()} (from {global.Source}) — recorded " +
                "only; nothing auto-registers, use `okf register`");
        }
    }

    private static string ToJson(OkfRegistry registry)
    {
        return JsonOutput.Write(writer =>
        {
            writer.WriteStartArray();
            foreach (OkfRegistryEntry entry in registry.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteString("kind", entry.Kind.ToRegistryString());
                writer.WriteString("path", entry.Path);
                writer.WriteBoolean("exists", entry.Exists);
                writer.WriteString("registeredAt", entry.RegisteredAt);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private static void WriteUsage(string verb, TextWriter writer)
    {
        switch (verb)
        {
            case "register":
                WriteRegisterUsage(writer);
                return;
            case "unregister":
                WriteUnregisterUsage(writer);
                return;
            default:
                WriteRegistryUsage(writer);
                return;
        }
    }

    private static void WriteRegisterUsage(TextWriter writer) =>
        writer.WriteLine("""
            okf register [path] [options]

            Adds a vault or a bundle root to okf's registry, which is the one way anything
            beyond the current project enters a command's scope. Idempotent: registering a
            path already in the registry succeeds and changes nothing.

            A directory holding bundles/ registers as a vault, a project root holding
            okf/bundles/ registers as the vault inside it, and any other directory registers
            as a bare bundle root. Each entry gets a short id — derived from the directory
            name, uniquified once, and never recomputed — that `okf unregister` accepts and
            that survives the directory being moved.

            The personal vault (OKF_HOME, else ~/okf) is an ordinary entry with no
            privileges. `okf register` with no path registers the vault this directory
            resolves to: the project vault found by walking up, and the personal vault when
            there is none.

            Nothing is ever registered automatically.

            Arguments:
              path                          The vault, project root, or bundle to register.

            Options:
              --verbose, -v                 Report the registry's location
              --help, -h                    Show this help

            Exit codes:
              0  registered, or already registered
              2  usage or environment failure (no such directory, unwritable registry)
            """);

    private static void WriteUnregisterUsage(TextWriter writer) =>
        writer.WriteLine("""
            okf unregister [path|id] [options]

            Removes an entry from okf's registry, by path or by the id `okf registry list`
            reports. Idempotent: removing something that is not registered succeeds and says
            so. With no argument it removes the vault this directory resolves to.

            Removing an entry never touches the directory it pointed at.

            Arguments:
              path|id                       The registered path, or its id.

            Options:
              --verbose, -v                 Report the registry's location
              --help, -h                    Show this help

            Exit codes:
              0  removed, or not registered in the first place
              2  usage or environment failure (unwritable registry)
            """);

    private static void WriteRegistryUsage(TextWriter writer) =>
        writer.WriteLine("""
            okf registry [list|prune] [options]

            Inspects okf's registry — the vaults and bundles `okf search --scope registered`
            and `okf mcp --scope registered` look at.

              list    Report every entry: its id, its kind, its path, and whether that path
                    still exists. Entries whose path is gone are marked (missing); nothing
                    is repaired or rewritten by reading.
              prune   Remove the entries whose paths no longer exist. This is the only
                    reading-shaped command that writes, and it writes only the registry.

            Options:
              --format <text|json>          Output format for `list` (default: text)
              --json                        Alias for --format json
              --verbose, -v                 Report the registry's location
              --help, -h                    Show this help

            Exit codes:
              0  reported, or pruned
              2  usage or environment failure (unreadable or unwritable registry)
            """);
}
