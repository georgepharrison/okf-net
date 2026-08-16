using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// <c>okf generated stamp &lt;concept&gt;…</c> — writes or refreshes a concept's
/// <c>generated: { by, at }</c> through the same text-surgery path <c>okf verify</c> uses
/// (AD-23, AD-52).
/// </summary>
/// <remarks>
/// The stamp is the one frontmatter field a producer owes on every write, and it is a
/// timestamp in a fixed spelling next to an actor in a fixed grammar — structure the
/// machine owns. What it is a claim <em>about</em> stays the agent's: that the prose beside
/// it was written now.
/// </remarks>
internal static class GeneratedCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>generated</c>.</param>
    /// <param name="environment">The environment paths are resolved against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors and warnings go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        GeneratedArguments parsed;
        try
        {
            parsed = GeneratedArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            return Usage(error, exception.Message);
        }

        if (parsed.ShowHelp || parsed.Verb is null)
        {
            if (parsed.Verb is null && !parsed.ShowHelp)
            {
                return Usage(error, "no subcommand. `okf generated stamp <concept>...` is the only one.");
            }

            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        if (parsed.Verb != "stamp")
        {
            return Usage(error, $"unknown generated subcommand '{parsed.Verb}'; expected 'stamp'.");
        }

        try
        {
            return Stamp(parsed, environment, output, error);
        }
        catch (IOException exception)
        {
            return Usage(error, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Usage(error, exception.Message);
        }
    }

    private static int Stamp(
        GeneratedArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        if (arguments.Paths.Count == 0)
        {
            return Usage(
                error,
                "no concept named. `okf generated stamp <concept-path>...` stamps the files you name.");
        }

        if (arguments.By is not { } actor)
        {
            return Usage(
                error,
                "no `--by` actor. A generation stamp names who wrote the content; §7 spells an actor "
                + "`<producer>/<version>`, `human:<id>`, or `process:<id>`.");
        }

        if (!OkfActor.IsValid(actor))
        {
            return Usage(
                error,
                $"'{actor}' is not an actor. §7 spells one `<producer>/<version>`, `human:<id>`, or `process:<id>`.");
        }

        if (OkfActor.IsHuman(actor))
        {
            // Allowed — §7 lets a person be a producer — but odd enough to say out loud:
            // `generated` by a human makes a concept its own author's work with no second
            // actor anywhere, and `okf verify` is the surface a person usually wants.
            error.WriteLine(
                $"okf: warning: '{actor}' is a person. `generated` records who WROTE the content; the human "
                + "surface is `okf verify`, which records who read and cleared it.");
        }

        // Every named concept is read and checked before the first byte is written: a
        // three-concept run that refuses the third would otherwise leave two claiming a
        // freshness the run did not finish establishing.
        var files = new List<string>();
        foreach (var path in arguments.Paths)
        {
            var full = Path.GetFullPath(Path.Combine(environment.CurrentDirectory, path));
            if (!File.Exists(full))
            {
                return Usage(error, $"no such concept: '{full}'.");
            }

            string text;
            OkfDocument document;
            try
            {
                text = File.ReadAllText(full);
                document = OkfDocument.Parse(text);
            }
            catch (OkfDocumentException exception)
            {
                return Usage(
                    error,
                    $"'{DiagnosticWriter.Display(full, environment.CurrentDirectory)}' has frontmatter that does "
                    + $"not parse ({exception.Message}). Run `okf lint` on the bundle.");
            }

            // `OkfDocument.Parse` hands back the whole text as the body exactly when it
            // found no frontmatter block. Stamping one would give a file frontmatter it
            // never had — holding `generated` and no `type`, which `okf lint` then rejects.
            if (string.Equals(document.Body, text, StringComparison.Ordinal))
            {
                return Usage(
                    error,
                    $"'{DiagnosticWriter.Display(full, environment.CurrentDirectory)}' has no frontmatter, so it "
                    + "is not a concept. `okf generated stamp` refreshes a concept's stamp; it does not give a "
                    + "file frontmatter it never had.");
            }

            files.Add(full);
        }

        var at = arguments.At ?? DateTimeOffset.UtcNow;
        var stamp = OkfCanonicalTimestamp.ToCanonical(at);

        foreach (var file in files)
        {
            var display = DiagnosticWriter.Display(file, environment.CurrentDirectory);
            if (arguments.DryRun)
            {
                output.WriteLine($"{display}: would write generated: {OkfStamp.GeneratedEntry(actor, at)}");
                continue;
            }

            OkfStamp.StampGeneratedFile(file, actor, at);
            output.WriteLine($"{display}: generated by {actor} at {stamp}");
        }

        output.WriteLine(arguments.DryRun
            ? $"Would stamp {DiagnosticWriter.Plural(files.Count, "concept")} as {actor} (--dry-run: nothing written)."
            : $"Stamped {DiagnosticWriter.Plural(files.Count, "concept")} as generated by {actor}.");

        return CliApplication.ExitSuccess;
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine($"okf: error: {message}");
        error.WriteLine("Run `okf generated --help` for usage.");
        return CliApplication.ExitUsage;
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf generated stamp <concept-path>... --by <actor> [options]

            Writes `generated: { by: <actor>, at: <now> }` into each named concept — the
            stamp a producer owes on every write, including a one-line edit. `at` is RFC
            3339 UTC at second precision, the one instant form okf writes, so a stamp sorts
            as text in the order it sorts in time.

            The edit is text surgery, not a re-emitted document: an existing one-line
            `generated` mapping is replaced and a missing key is inserted, so key order,
            quoting, unknown producer keys, `verified` and the body all survive byte for
            byte. The result is parsed back and required to hold the stamp just written.

            A fresh `generated.at` makes the concept UNACKNOWLEDGED again — it is newer than
            the latest `verified[].at`, so `okf inbox` lists it and its trust tier stops
            covering the current text. That is correct: the content changed, and the person
            who cleared the old version has not seen this one.

            Arguments:
              concept-path                  One or more concept files. Every one is read and
                                            checked before any is written.

            Options:
              --by <actor>                  REQUIRED. The generating actor, in §7 form:
                                            `<producer>/<version>` for an agent or tool,
                                            `process:<id>` for an automated process. A
                                            `human:<id>` actor is allowed and warned about —
                                            `okf verify` is the surface for a person.
              --at <instant>                Pin the stamp, e.g. 2026-08-16T14:00:00Z, for a
                                            reproducible run. Defaults to now.
              --dry-run                     Report what would be written; write nothing
              --help, -h                    Show this help

            Exit codes:
              0  every named concept was stamped
              2  a missing concept, a file with no frontmatter, frontmatter that does not
                 parse, a malformed actor, or a usage failure
            """);
    }
}
