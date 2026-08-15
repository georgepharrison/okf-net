using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// <c>okf verify &lt;concept-path&gt;…</c> — appends a verification event to one or more
/// concepts (PRD CLI-13, CORE-14; decisions.md Q6).
/// </summary>
/// <remarks>
/// <para>Verification is an acknowledgment, not a rewrite: <c>generated</c> is never
/// touched, the body is never touched, and nothing in the bundle is regenerated. A
/// concept whose content was written by an agent and then read and confirmed by a person
/// is exactly the same bytes plus one line.</para>
/// <para>The identity is resolved once for the whole invocation, before any file is read,
/// so a run either stamps every named concept or writes nothing.</para>
/// </remarks>
internal static class VerifyCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>verify</c>.</param>
    /// <param name="environment">The environment to resolve configuration against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and <c>--verbose</c> notes go.</param>
    /// <param name="clock">The clock the stamp's <c>at</c> comes from.</param>
    /// <param name="gitEmail">
    /// Reads <c>git config --global user.email</c>; defaults to the real read. Injected so
    /// the identity chain is exercisable without a git installation.
    /// </param>
    /// <returns>The process exit code.</returns>
    public static int Run(
        string[] args,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error,
        TimeProvider? clock = null,
        Func<string?>? gitEmail = null)
    {
        VerifyArguments parsed;
        try
        {
            parsed = VerifyArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf verify --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Verify(parsed, environment, output, error, clock ?? TimeProvider.System, gitEmail);
        }
        catch (OkfConfigException exception)
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

    private static int Verify(
        VerifyArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error,
        TimeProvider clock,
        Func<string?>? gitEmail)
    {
        if (arguments.Paths.Count == 0)
        {
            error.WriteLine("okf: error: no concept named. `okf verify <concept-path>...` stamps the files you name.");
            error.WriteLine("Run `okf verify --help` for usage.");
            return CliApplication.ExitUsage;
        }

        if (ResolveActor(arguments, environment, error, gitEmail) is not { } actor)
        {
            return CliApplication.ExitUsage;
        }

        if (arguments.Verbose)
        {
            error.WriteLine($"okf: stamping as {actor.Actor} (from {actor.Source})");
        }

        var files = new List<string>();
        foreach (var path in arguments.Paths)
        {
            var full = Path.GetFullPath(Path.Combine(environment.CurrentDirectory, path));
            if (!File.Exists(full))
            {
                error.WriteLine($"okf: error: no such concept: '{full}'.");
                return CliApplication.ExitUsage;
            }

            files.Add(full);
        }

        // Every check runs against every named file before the first byte is written: a
        // three-concept run that refuses the third leaves the first two unstamped, because
        // a half-applied acknowledgment is worse than none.
        var documents = new List<string>();
        foreach (var file in files)
        {
            OkfDocument document;
            try
            {
                document = OkfDocument.Parse(File.ReadAllText(file));
            }
            catch (OkfDocumentException exception)
            {
                error.WriteLine(
                    $"okf: error: '{DiagnosticWriter.Display(file, environment.CurrentDirectory)}' has frontmatter " +
                    $"that does not parse ({exception.Message}). Run `okf lint` on the bundle.");
                return CliApplication.ExitUsage;
            }

            if (SelfVerification(document, actor.Actor!) is { } generatedBy)
            {
                // decisions.md §7 and OKF0201: an actor may verify, but never its own
                // output. Refused rather than warned about, because the command's whole
                // product is the signal this would falsify.
                error.WriteLine(
                    $"okf: error: '{DiagnosticWriter.Display(file, environment.CurrentDirectory)}' was generated by " +
                    $"`{generatedBy}`, which is the actor this would verify as. A concept must not verify itself " +
                    "(§5.3); verification is a second actor's act.");
                return CliApplication.ExitUsage;
            }

            documents.Add(file);
        }

        var at = clock.GetUtcNow();
        var stamp = OkfStamp.FormatTimestamp(at);

        foreach (var path in documents)
        {
            var display = DiagnosticWriter.Display(path, environment.CurrentDirectory);
            if (arguments.DryRun)
            {
                output.WriteLine($"{display}: would append verified: - {OkfStamp.VerifiedEntry(actor.Actor!, at)}");
                continue;
            }

            OkfStamp.VerifyFile(path, actor.Actor!, at);
            output.WriteLine($"{display}: verified by {actor.Actor} at {stamp}");
        }

        output.WriteLine(arguments.DryRun
            ? $"Would verify {DiagnosticWriter.Plural(documents.Count, "concept")} as {actor.Actor} (--dry-run: nothing written)."
            : $"Verified {DiagnosticWriter.Plural(documents.Count, "concept")} as {actor.Actor}.");

        return CliApplication.ExitSuccess;
    }

    /// <summary>
    /// Resolves the actor to stamp, reporting the refusal itself when there is none.
    /// <c>--by</c> wins outright: it is the escape hatch for a second actor recording a
    /// machine confirmation, which the configured human identity by definition is not.
    /// </summary>
    private static OkfActorResolution? ResolveActor(
        VerifyArguments arguments,
        OkfEnvironment environment,
        TextWriter error,
        Func<string?>? gitEmail)
    {
        if (arguments.By is { } by)
        {
            if (!OkfActor.IsValid(by))
            {
                error.WriteLine(
                    $"okf: error: '{by}' is not an actor. §7 spells one `<producer>/<version>`, " +
                    "`human:<id>`, or `process:<id>`.");
                return null;
            }

            return OkfActorResolution.Resolved(by, "--by");
        }

        var global = OkfConfig.TryLoad(environment.GlobalConfigPath);
        var project = LoadProjectConfig(arguments, environment);
        var resolution = OkfVerifyIdentity.Resolve(
            project,
            global,
            gitEmail ?? (() => OkfVerifyIdentity.GlobalUserEmail(environment)));

        if (!resolution.IsResolved)
        {
            error.WriteLine($"okf: error: {resolution.Problem}");
            return null;
        }

        return resolution;
    }

    /// <summary>
    /// The project configuration <c>verify.actor</c> is read from: the one named by
    /// <c>--config</c>, else the vault discovered from the working directory. A working
    /// directory outside every vault is not an error here — the chain simply falls through
    /// to the global file and then to git.
    /// </summary>
    private static OkfConfig? LoadProjectConfig(VerifyArguments arguments, OkfEnvironment environment)
    {
        if (arguments.ConfigPath is { } explicitConfig)
        {
            var path = Path.GetFullPath(Path.Combine(environment.CurrentDirectory, explicitConfig));
            return File.Exists(path)
                ? OkfConfig.Load(path)
                : throw new OkfConfigException($"No such config file: '{path}'.");
        }

        try
        {
            return OkfDiscovery.Resolve(null, environment).ProjectConfigPath is { } vaultConfig
                ? OkfConfig.TryLoad(vaultConfig)
                : null;
        }
        catch (OkfDiscoveryException)
        {
            return null;
        }
    }

    /// <summary>The generating actor, when it is the one about to verify; otherwise null.</summary>
    private static string? SelfVerification(OkfDocument document, string actor)
    {
        if (document.Frontmatter.TryGetValue(OkfStamp.GeneratedKey, out var generated)
            && generated is OkfMapping mapping
            && mapping.TryGetValue("by", out var by)
            && by is OkfScalar scalar
            && string.Equals(scalar.Value, actor, StringComparison.Ordinal))
        {
            return scalar.Value;
        }

        return null;
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf verify <concept-path>... [options]

            Appends `verified: { by: human:<id>, at: <now> }` to each named concept: the
            human acknowledgment that clears it from `okf inbox`. `generated` is never
            touched and neither is the body — verification says a person read this, not
            that anything was rewritten. Frontmatter key order, quoting, and unknown
            producer keys all survive.

            The identity is `verify.actor` from okf configuration, falling back to
            `git config --global user.email`. The GLOBAL git config only: a
            repository-local email is frequently an agent's, and stamping one behind a
            `human:` prefix would manufacture the signal the prefix exists to carry.

            Arguments:
              concept-path                  One or more concept files. Every one is read
                                            and checked before any is written.

            Options:
              --by <actor>                  Stamp this actor instead — for a SECOND actor
                                            recording a machine confirmation, e.g.
                                            `process:nightly-schema-check`. Refused when it
                                            equals the concept's `generated.by`.
              --dry-run                     Report what would be written; write nothing
              --config <path>               Read `verify.actor` from this config file
              --verbose, -v                 Report where the identity came from
              --help, -h                    Show this help

            Exit codes:
              0  every named concept was stamped
              2  no verifying identity, a self-verification, a missing or unparseable
                 concept, or a usage failure
            """);
    }
}
