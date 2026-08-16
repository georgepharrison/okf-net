using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// <c>okf capture add</c> and <c>okf capture close</c> — the deterministic write side of
/// the capture manifest (AD-52).
/// </summary>
/// <remarks>
/// <para>These exist because the two producer skills used to have an agent hand-write
/// <c>sha256</c>, <c>capturedAt</c> and the ingestion close into
/// <c>&lt;vault&gt;/raw/manifest.json</c> — structure the Design Paradigm says the machine
/// owns, and the part of the record a small local model is least likely to get right. The
/// agent still decides <em>meaning</em>: which artifact was worth capturing, where it came
/// from, and which concept it became.</para>
/// <para>Both refuse rather than repair. An ingested entry is immutable (AD-18), so a
/// second capture of it is a new id; a manifest that will not read as one is reported and
/// left exactly as found.</para>
/// </remarks>
internal static class CaptureCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>capture</c>.</param>
    /// <param name="environment">The environment to resolve the vault against.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where errors go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        CaptureArguments parsed;
        try
        {
            parsed = CaptureArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            return Usage(error, exception.Message);
        }

        if (parsed.ShowHelp || parsed.Verb is null)
        {
            if (parsed.Verb is null && !parsed.ShowHelp)
            {
                return Usage(error, "no subcommand. `okf capture add` records an item; `okf capture close` ingests one.");
            }

            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return parsed.Verb switch
            {
                "add" => Add(parsed, environment, output, error),
                "close" => Close(parsed, environment, output, error),
                _ => Usage(error, $"unknown capture subcommand '{parsed.Verb}'; expected 'add' or 'close'."),
            };
        }
        catch (OkfDiscoveryException exception)
        {
            return Usage(error, exception.Message);
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

    private static int Add(
        CaptureArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        if (arguments.Operands.Count != 1)
        {
            return Usage(
                error,
                "`okf capture add <file-or-directory-under-raw>` records exactly one item, and "
                + $"{DiagnosticWriter.Plural(arguments.Operands.Count, "item")} were named.");
        }

        if (arguments.Concepts.Count > 0)
        {
            return Usage(error, "`--concept` belongs to `okf capture close`; a fresh capture is uningested by definition.");
        }

        if (Actor(arguments, error) is not { } actor || Vault(environment, error) is not { } vault)
        {
            return CliApplication.ExitUsage;
        }

        var manifestPath = OkfCaptureManifest.PathFor(vault);
        var current = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : OkfCaptureWriter.EmptyManifest;

        var result = OkfCaptureWriter.Add(
            current,
            Path.Combine(vault, OkfCaptureManifest.RawDirectoryName),
            new OkfCaptureAddition
            {
                ItemPath = Path.GetFullPath(Path.Combine(environment.CurrentDirectory, arguments.Operands[0])),
                CapturedBy = actor,
                CapturedAt = arguments.At ?? DateTimeOffset.UtcNow,
                OriginalUrl = arguments.Url,
                Title = arguments.Title,
                SourceLastModified = arguments.SourceLastModified,
                Form = arguments.Form,
            });

        return Report(result, manifestPath, arguments, environment, output, error);
    }

    private static int Close(
        CaptureArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        if (arguments.Operands.Count != 1)
        {
            return Usage(
                error,
                "`okf capture close <id-or-path>` closes exactly one entry, and "
                + $"{DiagnosticWriter.Plural(arguments.Operands.Count, "entry", "entries")} were named.");
        }

        if (arguments.Concepts.Count == 0)
        {
            return Usage(
                error,
                "no `--concept` named. Closing an ingestion records which concepts now carry the artifact, "
                + "vault-root-relative (e.g. bundles/<name>/references/<concept>.md).");
        }

        if (Actor(arguments, error) is not { } actor || Vault(environment, error) is not { } vault)
        {
            return CliApplication.ExitUsage;
        }

        // Every concept is checked before the manifest is touched: an ingestion pointing at
        // a file that is not there is the violation `check-manifest.py` reports, and a
        // half-closed entry cannot be reopened.
        foreach (var concept in arguments.Concepts)
        {
            if (concept.StartsWith('/') || concept.Split('/', '\\').Contains(".."))
            {
                error.WriteLine(
                    $"okf: error: `{concept}` must stay inside the vault. `ingestion.concepts` paths are relative "
                    + "to the vault root, never absolute and never escaping it.");
                return CliApplication.ExitDiagnostics;
            }

            if (!File.Exists(Path.Combine(vault, concept.Replace('/', Path.DirectorySeparatorChar))))
            {
                error.WriteLine(
                    $"okf: error: `{concept}` names no file under '{vault}'. An ingested capture points at the "
                    + "concept that carries it, so the concept is written first.");
                return CliApplication.ExitDiagnostics;
            }
        }

        var manifestPath = OkfCaptureManifest.PathFor(vault);
        if (!File.Exists(manifestPath))
        {
            error.WriteLine($"okf: error: no capture manifest at '{manifestPath}'; nothing has been captured.");
            return CliApplication.ExitUsage;
        }

        var result = OkfCaptureWriter.Close(
            File.ReadAllText(manifestPath),
            new OkfCaptureClosure
            {
                Entry = arguments.Operands[0],
                By = actor,
                At = arguments.At ?? DateTimeOffset.UtcNow,
                Concepts = arguments.Concepts,
            });

        return Report(result, manifestPath, arguments, environment, output, error);
    }

    /// <summary>Writes the result, or reports the refusal at the exit code its kind earns.</summary>
    private static int Report(
        OkfCaptureWriteResult result,
        string manifestPath,
        CaptureArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        var display = DiagnosticWriter.Display(manifestPath, environment.CurrentDirectory);

        if (!result.IsWritten)
        {
            error.WriteLine($"okf: error: {result.Problem}");

            // A refusal that names the record's own state is a diagnostic — the manifest
            // says no. Everything else is an environment or usage failure, including a
            // manifest okf cannot read, which is the one case where writing would destroy
            // what it could not understand (AD-18).
            return result.Outcome is OkfCaptureWriteOutcome.AlreadyCaptured or OkfCaptureWriteOutcome.AlreadyClosed
                ? CliApplication.ExitDiagnostics
                : CliApplication.ExitUsage;
        }

        OkfCaptureWriter.Save(manifestPath, result.Text!);

        if (arguments.Json)
        {
            output.WriteLine(ToJson(result, display));
            return CliApplication.ExitSuccess;
        }

        output.WriteLine(result.Outcome switch
        {
            OkfCaptureWriteOutcome.Added => $"{display}: added `{result.Id}` — uningested, awaiting the custodian.",
            OkfCaptureWriteOutcome.Recaptured =>
                $"{display}: replaced uningested `{result.Id}` — a re-capture of an entry nothing cites yet.",
            _ => $"{display}: closed `{result.Id}` into "
                + $"{DiagnosticWriter.Plural(arguments.Concepts.Count, "concept")}. The item is now immutable.",
        });

        return CliApplication.ExitSuccess;
    }

    /// <summary>
    /// The entry as it now stands, read back out of the manifest that was just written
    /// rather than out of what the writer was asked for.
    /// </summary>
    private static string ToJson(OkfCaptureWriteResult result, string manifestPath)
    {
        var entry = OkfCaptureManifest.Parse(result.Text!, manifestPath)!.Captures
            .First(candidate => string.Equals(candidate.Id, result.Id, StringComparison.Ordinal));

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = true, IndentSize = 2, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("manifest", manifestPath);
            writer.WriteString("outcome", result.Outcome.ToString().ToLowerInvariant());
            writer.WriteString("id", entry.Id);
            writer.WriteBoolean("ingested", entry.IsIngested);
            writer.WriteStartArray("files");
            foreach (var file in entry.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The actor, which is <c>--by</c> and nothing else. There is deliberately no chain
    /// and no configured default: a capture is usually an agent's, every agent already
    /// knows the actor string it stamps into <c>generated.by</c>, and a configured fallback
    /// would quietly attribute one producer's capture to another.
    /// </summary>
    private static string? Actor(CaptureArguments arguments, TextWriter error)
    {
        if (arguments.By is not { } by)
        {
            error.WriteLine(
                "okf: error: no `--by` actor. A capture records who retrieved or ingested the artifact; "
                + "§7 spells an actor `<producer>/<version>`, `human:<id>`, or `process:<id>`.");
            return null;
        }

        if (!OkfActor.IsValid(by))
        {
            error.WriteLine(
                $"okf: error: '{by}' is not an actor. §7 spells one `<producer>/<version>`, `human:<id>`, "
                + "or `process:<id>`.");
            return null;
        }

        return by;
    }

    /// <summary>The vault the manifest belongs to, discovered from the working directory.</summary>
    private static string? Vault(OkfEnvironment environment, TextWriter error)
    {
        var vault = OkfDiscovery.Resolve(null, environment).VaultRoot;
        if (vault is null)
        {
            error.WriteLine(
                "okf: error: no vault resolved from this directory, and `raw/` is a vault's drop zone. "
                + "Run from inside a project holding `okf/bundles/`.");
        }

        return vault;
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine($"okf: error: {message}");
        error.WriteLine("Run `okf capture --help` for usage.");
        return CliApplication.ExitUsage;
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("""
            okf capture add <file-or-directory-under-raw> --by <actor> [options]
            okf capture close <id-or-path> --concept <path> --by <actor> [options]

            Writes the capture manifest, `<vault>/raw/manifest.json` — the immutability
            record and the custodian's work queue. `add` records an item you have already
            dropped into `raw/`: okf computes every `files[].sha256`, stamps `capturedAt` in
            the one form it writes (RFC 3339 UTC, seconds), derives the entry's id and form
            from the item's own name and shape, and leaves `ingestion` null. `close` sets
            that `ingestion` once the artifact has become a concept.

            The manifest is edited in place, byte for byte: no entry but the one being
            written is touched, and a file that does not read as a manifest is reported and
            LEFT AS FOUND. Repairing the immutability record is how the record is lost.

            Arguments:
              file-or-directory             A file (a `flat` capture) or a directory (a
                                            `packet`) sitting directly in `<vault>/raw/`.
                                            Its name is the entry id: `<YYYY-MM-DD>-<slug>`.
              id-or-path                    The entry to close, by id or by any path it
                                            claims under `raw/`.

            Options:
              --by <actor>                  REQUIRED. Who captured or ingested it, in §7
                                            form. There is no default: an agent knows its
                                            own actor, and guessing would misattribute.
              --url <url>                   The original URL, carried forward on the entry
                                            (`add` only). This is the one place it survives.
              --title <text>                The artifact's title (`add` only)
              --source-last-modified <date> Its own last-modified date, YYYY-MM-DD
              --form flat|packet            Assert the form; refused when it disagrees with
                                            what the item actually is
              --concept <path>              A concept the capture became, relative to the
                                            VAULT root, repeatable (`close` only). Each must
                                            already exist.
              --captured-at, --at <instant> Pin the stamp, e.g. 2026-08-16T14:00:00Z, for a
                                            reproducible run. Defaults to now.
              --json                        Report the written entry as JSON
              --help, -h                    Show this help

            Exit codes:
              0  the manifest was written
              1  the record says no: the item is already captured and ingested, the entry is
                 already closed, or a named concept does not exist
              2  the manifest does not read as one, the item is not a capturable item, no
                 actor, no vault, or a usage failure
            """);
    }
}
