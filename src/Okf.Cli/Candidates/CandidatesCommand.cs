using System.Globalization;
using System.Text;
using Okf.Core;

namespace Okf.Cli.Candidates;

/// <summary>
/// <c>okf candidates [path]</c> — the concepts in the resolved bundles whose verification
/// history is provably absent (work item #75, spec #71).
/// </summary>
/// <remarks>
/// <para>
/// It is an inventory, not a gate on volume. Forty candidates is a success and exits 0, because
/// a scan step in a verification pipeline must not fail merely because there is work to do — the
/// same reasoning that leaves <c>okf inbox</c> exiting 0 whatever it finds, and the reason there
/// is no <c>--fail-if-any</c> here: the length of this list is the answer, not a verdict.
/// </para>
/// <para>
/// It is complete or it says so. A concept whose <c>verified</c> block could not be read is named
/// on standard error and turns the run into exit 1, because an enumeration that quietly omits a
/// concept is worse than one that admits a gap — the asymmetry the whole feature exists to
/// enforce. The two channels are the contract: stdout is the inventory, stderr is the notice, and
/// neither format mixes them.
/// </para>
/// <para>
/// It writes nothing, launches nothing, and touches no network (CLI-16).
/// </para>
/// </remarks>
internal static class CandidatesCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>candidates</c>.</param>
    /// <param name="environment">The environment to resolve vaults against.</param>
    /// <param name="output">Where the inventory goes.</param>
    /// <param name="error">Where quarantine notices, errors, and <c>--verbose</c> notes go.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, OkfEnvironment environment, TextWriter output, TextWriter error)
    {
        if (Parse(args, error) is not { } parsed)
        {
            return CliApplication.ExitUsage;
        }

        if (parsed.ShowHelp)
        {
            WriteUsage(output);
            return CliApplication.ExitSuccess;
        }

        try
        {
            return Report(parsed, environment, output, error);
        }
        catch (Exception exception) when (IsEnvironmentFailure(exception))
        {
            error.WriteLine($"okf: error: {exception.Message}");
            return CliApplication.ExitUsage;
        }
    }

    private static CandidatesArguments? Parse(string[] args, TextWriter error)
    {
        try
        {
            return CandidatesArguments.Parse(args);
        }
        catch (OkfConfigException exception)
        {
            error.WriteLine($"okf: error: {exception.Message}");
            error.WriteLine("Run `okf candidates --help` for usage.");
            return null;
        }
    }

    private static bool IsEnvironmentFailure(Exception exception) =>
        exception is OkfDiscoveryException or IOException or UnauthorizedAccessException;

    private static int Report(
        CandidatesArguments arguments,
        OkfEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        // PRD CLI-1: the same working set `okf lint`, `okf search` and `okf inbox` resolve, so
        // the four never disagree about which bundles they are looking at. There is no scope
        // flag here for the same reason there is none on the inbox.
        OkfWorkingSet workingSet = OkfDiscovery.Resolve(arguments.Path, environment);
        OkfCandidateResult result = OkfCandidateScanner.Scan(workingSet.Bundles);

        WriteVerbose(arguments, workingSet, result, error);
        WriteQuarantined(result, environment.CurrentDirectory, error);

        if (arguments.Json)
        {
            output.Write(CandidatesJson.Write(result, environment.CurrentDirectory));
            output.Write(Environment.NewLine);
        }
        else
        {
            WriteText(result, environment.CurrentDirectory, output);
        }

        // The inventory's length is not a failure; an unclassified concept is. Nothing else moves
        // the exit code, so a caller can branch on whether the list is trustworthy without
        // scraping output for it.
        return result.IsComplete ? CliApplication.ExitSuccess : CliApplication.ExitDiagnostics;
    }

    private static void WriteVerbose(
        CandidatesArguments arguments,
        OkfWorkingSet workingSet,
        OkfCandidateResult result,
        TextWriter error)
    {
        if (!arguments.Verbose)
        {
            return;
        }

        VerboseReport.WorkingSet(error, workingSet);
        error.WriteLine(
            $"okf: scanned {DiagnosticWriter.Plural(result.ConceptCount, "concept")}, " +
            $"{DiagnosticWriter.Plural(result.Candidates.Count, "candidate", "candidates")}, " +
            $"{result.Quarantined.Count.ToString(CultureInfo.InvariantCulture)} quarantined");
    }

    private static void WriteText(OkfCandidateResult result, string baseDirectory, TextWriter output)
    {
        foreach (OkfCandidate candidate in result.Candidates)
        {
            output.WriteLine(Row(candidate, baseDirectory));
            output.WriteLine($"    {Reason(candidate)}");
        }

        WriteSummary(result, output);
    }

    /// <summary>
    /// One candidate on one line: where it is, what it is called, who vouches for it, and the two
    /// flags a caller might act on. The tier is printed as derived even though it is never
    /// consulted for eligibility — a candidate is a candidate because nobody signed it, whatever
    /// §5.3 makes of the rest of its frontmatter.
    /// </summary>
    private static string Row(OkfCandidate candidate, string baseDirectory)
    {
        OkfConcept concept = candidate.Concept;
        string bundle = Displayed(concept, baseDirectory);
        string type = concept.Type is { Length: > 0 } value ? $" ({value})" : string.Empty;
        return $"  {bundle}  {concept.Title}  [{concept.TrustTier.ToSpecString()}]{Markers(concept)}{type}";
    }

    /// <summary>
    /// The row's flags. Draft and stale are reported, never filtered on: a draft nobody has
    /// verified is exactly as unverified as anything else, and deciding to skip it is the
    /// reviewer's policy rather than this command's (#71 stories 6 and 7).
    /// </summary>
    private static string Markers(OkfConcept concept)
    {
        List<string> markers = [];
        if (string.Equals(OkfVerificationStamp.Status(concept), OkfInboxScanner.DraftStatus, StringComparison.OrdinalIgnoreCase))
        {
            markers.Add("draft");
        }

        if (concept.Stale)
        {
            markers.Add("stale");
        }

        return markers.Count == 0 ? string.Empty : $" ({string.Join(", ", markers)})";
    }

    /// <summary>
    /// The one line under a row that says why this concept is on the list. The library owns the
    /// sentence — it is a reading of the block, and AD-6 puts readings there — and the row prints
    /// it, so the same bytes get the same explanation from every surface that shows them.
    /// </summary>
    private static string Reason(OkfCandidate candidate) =>
        OkfVerificationStamp.Read(candidate.Concept).WhyCandidate;

    private static void WriteSummary(OkfCandidateResult result, TextWriter output)
    {
        StringBuilder summary = new StringBuilder()
            .Append("Scanned ")
            .Append(DiagnosticWriter.Plural(result.ConceptCount, "concept"))
            .Append(" in ")
            .Append(DiagnosticWriter.Plural(result.Bundles.Count, "bundle"))
            .Append(": ")
            .Append(DiagnosticWriter.Plural(result.Candidates.Count, "candidate", "candidates"))
            .Append(", ")
            .Append(result.Quarantined.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" quarantined");

        // A file whose frontmatter could not be read is a quarantine like any other, so it is
        // counted in the number above and named on stderr. #75 carried it as a parenthetical
        // count instead, as the disclosure that a broken vault was not a clean one; #76 replaced
        // the disclosure with the quarantine it was standing in for.
        summary.Append('.');
        if (result.Candidates.Count == 0 && result.Quarantined.Count == 0)
        {
            summary.Append(" Nothing awaits review.");
        }

        output.WriteLine(summary);
    }

    /// <summary>
    /// The files whose history could not be established, named on standard error with the
    /// reason's wire spelling and the detail a person can act on. They are never in the inventory
    /// and never in the JSON array: a machine consumer reads the array as the candidates it is,
    /// and reads exit 1 as "that array is not the whole truth".
    /// </summary>
    /// <remarks>
    /// A frontmatter quarantine is worded as "not readable as a concept" and points at
    /// <c>okf lint</c>, because that file is not a verification problem at all — it is a file a
    /// linter can already explain — whereas a verification-structure quarantine is. Printing the
    /// same sentence for both would send a reader to look for a <c>verified</c> key in a file
    /// that has no frontmatter to put one in. The tier is printed only when the file earned one.
    /// </remarks>
    private static void WriteQuarantined(OkfCandidateResult result, string baseDirectory, TextWriter error)
    {
        foreach (OkfQuarantinedConcept quarantined in result.Quarantined)
        {
            string where = Displayed(quarantined.Bundle, quarantined.Path, baseDirectory);
            string tier = quarantined.TrustTier is { } trustTier
                ? $" [{trustTier.ToSpecString()}]"
                : string.Empty;
            error.WriteLine(
                $"okf: could not classify {where} [{quarantined.Reason.ToWireString()}]{tier}: " +
                $"{Notice(quarantined)}");
        }
    }

    /// <summary>
    /// The one sentence a person reads for one quarantine. The verification-structure half is the
    /// library's own words about the block (#74); the frontmatter half says plainly that the file
    /// is not readable as a concept and names the tool that fixes it.
    /// </summary>
    private static string Notice(OkfQuarantinedConcept quarantined) =>
        quarantined.Reason == OkfQuarantineReason.FrontmatterUnreadable
            ? $"this file is not readable as a concept: {quarantined.Detail}. " +
                "Run `okf lint` on the bundle to fix it."
            : quarantined.Detail;

    /// <summary>
    /// A concept named by bundle and bundle-relative path, relative to the working directory when
    /// the bundle sits under it. The bundle is spelled out because a vault scan reports several of
    /// them, and a row that does not say which bundle it came from cannot be opened.
    /// </summary>
    private static string Displayed(OkfConcept concept, string baseDirectory) =>
        Displayed(concept.Bundle, concept.Path, baseDirectory);

    private static string Displayed(OkfBundle bundle, string path, string baseDirectory) =>
        $"{DiagnosticWriter.Display(bundle.Root, baseDirectory)}/{path}";

    private static void WriteUsage(TextWriter writer) => writer.WriteLine("""
            okf candidates [path] [options]

            Lists the concepts in the resolved bundles whose verification history is
            provably absent — the concepts nobody has ever stood behind. It is an
            inventory, not a gate: finding forty of them is a success, and the command
            writes nothing.

            What makes a concept a candidate is one question, and it is not the trust
            tier's question. The tier asks WHO vouches for a concept; this asks whether
            anybody wrote themselves down at all.

              Candidate       No `verified` key; `verified: null`; `verified: []`.
                              Provable absence, and nothing else qualifies.
              Not listed      Any `verified` event with a non-empty author — a person,
                              a process, a producer. A garbage timestamp still names
                              somebody, and somebody is the answer.
              Quarantined     A file the scan could not classify, under one of
                              two reasons kept distinct because their fixes
                              differ. Named on standard error, never listed,
                              and the run exits 1.
                frontmatter-  No opening fence, a fence that never closes, or
                unreadable    a block that is not a YAML mapping: the file is
                              not readable as a concept, and `okf lint`
                              explains the fix.
                verification- A `verified` block that claims verification but
                structure     cannot be read as events: a scalar, an empty
                              mapping, a mapping naming no author, a sequence
                              with junk in it.

            Draft concepts, stale concepts, and per-directory `about.md` files are all
            included: they are concepts, and a concept nobody verified is a candidate
            whatever else is true of it. Generated `index.md` and `log.md` are not
            concepts and never appear.

            Arguments:
              path                          A bundle root, a vault (a directory holding
                                            bundles/), or a project root (holding
                                            okf/bundles/). Defaults to the vault found by
                                            walking up from the working directory, then to
                                            the personal vault (OKF_HOME, else ~/okf).

            Options:
              --format <text|json>          Output format (default: text)
              --json                        Alias for --format json
              --verbose, -v                 Report vault resolution and the scan counts
              --help, -h                    Show this help

            Standard output carries the inventory and nothing else, in both formats;
            quarantine notices go to standard error in both. In text, each row is followed
            by a line saying what makes it a candidate. In JSON, stdout is a bare array of
            records and that explanation is not one of them: the array's shape is the same
            whether or not a quarantine is being reported, and the exit code is what says
            whether the array is the whole truth.

            The summary line begins `Scanned` and counts the markdown files reached
            (unreadable ones included), the bundles, the candidates and the quarantines, so
            an empty inventory is distinguishable from a failed run.

            JSON records carry the concept, not the mechanism: identity (id, path,
            displayPath, absolutePath, bundle, bundleName), content (title, type,
            description, tags), and state (trustTier, stale, status, generatedBy,
            generatedAt, staleAfter). Absent optionals are null rather than missing keys,
            and there are no verification fields — a candidate has no verification events by
            definition, so an always-null one would only be a field to disbelieve. Ordering
            is byte-stable across runs on unchanged bytes, so one run's output diffs against
            the next to show what was newly verified. (fix(core): quarantine files whose frontmatter cannot be read)

            Exit codes:
              0  the enumeration completed, whatever its length
              1  at least one concept in scope could not be classified — the
                 inventory is incomplete
              2  usage or environment failure
            """);
}
