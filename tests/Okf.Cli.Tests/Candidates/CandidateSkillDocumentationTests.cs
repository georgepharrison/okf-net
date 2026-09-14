using System.Text.Json;
using System.Text.RegularExpressions;

namespace Okf.Cli.Tests.Candidates;

/// <summary>
/// The documentation contract for <c>okf candidates</c> (work item #80): the verb is named where
/// an agent already looks — the README, the vault-reading skill, the custodian skill — and what
/// those documents say about it stays true against the binary that ships beside them.
/// </summary>
/// <remarks>
/// <para>
/// Issue #80 exists because an agent that never sees the enumeration writes its own scanner, and
/// AD-6's whole objection to that is a second implementation drifting one rule at a time. A skill
/// file is the same hazard in prose: nothing stops <c>okf-candidates</c>'s documented exit codes,
/// summary line, or JSON shape from drifting away from the real ones, and a document that drifts
/// is worse than none, because an agent trusts it.
/// </para>
/// <para>
/// So the assertions here are cross-checked rather than self-referential. The claims about
/// behaviour — the <c>Scanned</c> summary, the exit codes, the bare-array stdout, the
/// <c>--format</c> flag — are established by running the verb in a temp tree, and the prose is
/// then required to carry the same strings. That is why the expected values are not typed into
/// this file: a literal here would agree with a stale document forever, and the point of the test
/// is to notice when they part.
/// </para>
/// <para>
/// The negative assertions are the other half, and they are what makes this a contract rather
/// than a keyword grep: the issue's acceptance criteria are that the skills must NOT claim the
/// command verifies, reviews, stamps, or writes. A test that only asserted the word "candidates"
/// appears would pass on a document that described the verb as a gate or as a writer.
/// </para>
/// </remarks>
public class CandidateSkillDocumentationTests
{
    /// <summary>
    /// The skills that enumerate the vault-reading commands, which is where #80 says the
    /// enumeration belongs. <c>okf-capture</c> is deliberately absent: it searches before it
    /// writes and never enumerates a backlog, so naming a review-inventory there would be noise.
    /// </summary>
    private static readonly string[] VaultReadingSkills = ["okf-vault", "okf-custodian"];

    [Fact]
    public void TheSkillThatAlreadyNamesTheInboxNowNamesTheCandidatesVerb()
    {
        foreach (var skill in VaultReadingSkills)
        {
            var text = Skill(skill);

            // Both skills enumerated the vault-reading commands before #80, in one sentence. The
            // verb has to join THAT sentence: an agent skims the interface section to learn what
            // tools exist, and a verb mentioned only further down is not in that inventory. The
            // assertion is scoped to the enumeration on purpose — a looser one would pass on a
            // file that mentioned the verb once in passing and still listed only the inbox.
            var candidates = EnumerationsOfVaultReadingCommands(text).ToList();
            Assert.Single(candidates);
            string enumeration = candidates[0];

            Assert.Contains("okf inbox", enumeration, StringComparison.Ordinal);
            Assert.Contains("okf candidates", enumeration, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The sentences that read as an inventory of commands — the ones naming at least three
    /// <c>okf</c> verbs in a row. That is the shape the interface sections of both skills use.
    /// </summary>
    private static IEnumerable<string> EnumerationsOfVaultReadingCommands(string text)
    {
        foreach (var sentence in Regex.Split(Unwrap(text), @"(?<=[.!?])\s+"))
        {
            if (Regex.Count(sentence, @"`okf [a-z]+`") >= 3)
            {
                yield return sentence.Trim();
            }
        }
    }

    [Fact]
    public void BothInvocationsAreDocumentedInBothSkills()
    {
        foreach (var skill in VaultReadingSkills)
        {
            var text = Skill(skill);

            // The plain-text and JSON spellings, both, because the two consumers are different: a
            // person reading rows and a pipeline parsing an array.
            Assert.Matches(@"okf candidates( <vault-path>)?\s", text);
            Assert.Matches(@"okf candidates( <vault-path>)?[^\n]*--format json", text);
        }
    }

    [Fact]
    public void TheEscapeHatchForAnUninstalledBinaryIsStatedInBothSkills()
    {
        foreach (var skill in VaultReadingSkills)
        {
            var text = Skill(skill);

            // The form the skills already use: name the project's documented way to run the CLI,
            // then say to stop rather than fall back to a text scan.
            Assert.Contains("absent from the PATH", text, StringComparison.Ordinal);
            Assert.Contains("stop", text, StringComparison.Ordinal);
            Assert.Contains("mise run cli", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheDistinctionFromTheInboxIsStatedAndNotMerged()
    {
        foreach (var skill in VaultReadingSkills)
        {
            var text = Skill(skill);

            // The sentence the issue names as the one that has to come across, or agents keep
            // using the inbox for this. Checked on unwrapped text: this repository's markdown is
            // hard-wrapped by hand, so a fixed pattern would break wherever a line happens to
            // end, and the assertion would depend on reflow rather than on the claim.
            Assert.Matches(
                @"The inbox is a triage list of what is waiting on a person; (this|`okf candidates`) is an inventory of what has never been verified\.",
                Unwrap(text));

            // And that neither contains the other, which is what stops the two being treated as
            // interchangeable counts of the same thing.
            Assert.Contains("neither contains the other", Unwrap(text), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The counterexample the skills offer for "neither contains the other", checked against the
    /// two commands rather than read as prose. An earlier revision of this assertion stopped at
    /// the phrase itself, which a document could keep saying while its worked example described a
    /// vault state that does not behave that way — the reviewer caught exactly that, since the
    /// example originally offered was a regenerated concept, and a regenerated concept is
    /// quarantined rather than listed.
    ///
    /// The example now offered is a hand-written concept that is drafted and past its
    /// <c>stale_after</c> while carrying a real verification event. That state is built here and
    /// run through both commands: the inbox must report it, and the inventory must not.
    /// </summary>
    [Fact]
    public void TheWorkedCounterexampleBehavesTheWayTheSkillsSayItBehaves()
    {
        using var tree = new TempTree();
        tree.CreateDirectory("vault/bundles/b");
        tree.Write(
            "vault/bundles/b/drafted.md",
            "---\ntype: Concept\ntitle: Drafted\nstatus: draft\nstale_after: 2020-01-01\n"
            + "verified:\n  - by: human:ringo\n    at: 2024-01-01T00:00:00Z\n---\n\nBody.\n");

        // Oracle: the two commands, run over the same vault.
        var inbox = CliHarness.RunIn(tree.Root, tree.Root, "inbox", "vault");
        var candidates = CliHarness.RunIn(tree.Root, tree.Root, "candidates", "vault");

        // The concept the skills hold up as "on the inbox, and not on this inventory" must be on
        // the inbox and must not be on the inventory. If either command changes what it counts, the
        // example in the prose becomes false and this goes red. Both report a display path, so
        // that is what is compared.
        var conceptPath = "vault/bundles/b/drafted.md";
        Assert.Contains(conceptPath, inbox.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(conceptPath, candidates.Output, StringComparison.Ordinal);

        // And the reason it is on the inbox is the reason the prose gives — draft and staleness,
        // not an absent signature — so the two lists really do answer different questions here.
        Assert.Contains("status: draft", inbox.Output, StringComparison.Ordinal);
        Assert.Contains("stale_after", inbox.Output, StringComparison.Ordinal);
        Assert.Contains("0 candidates", candidates.Output, StringComparison.Ordinal);

        // The prose must name that state IN THE SENTENCE that makes the claim, not merely
        // somewhere in the file: `status: draft` and `stale_after` each occur several times in
        // both skills for unrelated reasons, so a file-wide Contains is satisfied by prose that
        // has nothing to do with this example and the assertion would never notice the example
        // being replaced. The window is the clause rather than the sentence, because the example
        // is written as "…that is `status: draft`, or past its `stale_after`, is on the inbox …"
        // and the abbreviation's own period sends `stale_after` past a sentence boundary.
        foreach (var skill in VaultReadingSkills)
        {
            string unwrapped = Unwrap(Skill(skill)).Replace("```", " ").Replace("`", string.Empty);
            int at = unwrapped.IndexOf("neither contains the other", StringComparison.Ordinal);

            Assert.True(at >= 0, $"{skill} does not state that the two lists are incomparable");

            // The clause running from that phrase to the period that ends the example.
            int end = unwrapped.IndexOf("to it.", at, StringComparison.Ordinal);

            Assert.True(end > at, $"{skill}: the counterexample clause is unbounded");
            string clause = unwrapped.Substring(at, end - at);

            Assert.Contains("status: draft", clause, StringComparison.Ordinal);
            Assert.Contains("stale_after", clause, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NeitherSkillClaimsTheCommandVerifiesReviewsStampsOrWrites()
    {
        foreach (var skill in VaultReadingSkills)
        {
            var text = Skill(skill);

            // The AC is a prohibition, and a prohibition cannot be checked by scanning for bad
            // words: the sentence "it verifies nothing" contains "verifies" and is the correct
            // thing to say. So the check is that each skill DENIES the capability in the same
            // sentence it raises it, which is the form that survives an agent skimming for a
            // permission. A document that said "candidates reviews the backlog" would fail here
            // while a keyword scan would sail through it.
            var denial = Unwrap(Skill(skill));

            Assert.Matches(
                @"[^.]*candidates?[^.]{0,120}\b(verifies?|reviews?|stamps?|writes?)\s+nothing\b",
                denial);

            // Every capability the AC names is denied by name, not summarised as "it is
            // read-only" — a reader who wants to stamp something looks for the word "stamp".
            foreach (var capability in new[] { "verifies", "reviews", "stamps", "writes" })
            {
                Assert.Matches(
                    $@"\b{capability}\s+nothing\b",
                    denial);
            }

            // And the denial is attached to this verb rather than floating in the file. Checked as
            // the sentence that carries it, not as "the section mentions the verb somewhere": the
            // backlog section names `okf candidates` four times on its way to other things, so a
            // section-level Contains would pass even with the denial's subject removed. The window
            // is one sentence wide, which is wide enough for a subject that names itself in prose
            // ("What it does not do … : `okf candidates` verifies nothing") and too narrow for a
            // verb mentioned two paragraphs away.
            Assert.Contains(
                SentencesAbout(text, "nothing"),
                sentence => Regex.IsMatch(sentence, @"\b(verifies?|reviews?|stamps?|writes?)\s+nothing\b")
                    && Regex.IsMatch(sentence, @"(`?okf candidates`?|it)\s+\b(verifies?|reviews?|stamps?|writes?)\s+nothing\b"));
        }
    }

    /// <summary>
    /// The other half of the prohibition, and the half an earlier revision of this class was
    /// missing: it required the denial to exist without rejecting an affirmative claim made
    /// elsewhere, so a skill that said both "`okf candidates` verifies nothing" and "it writes a
    /// report file" would have passed. A denial and a contradiction in one document is exactly the
    /// failure AC 5 is about, because an agent skims one of the two.
    ///
    /// The scan is over EVERY sentence, not the sentences that name the command: a first draft of
    /// this test filtered to "candidates" and was shown to be vacuous, because a contradiction
    /// written as "It writes a report file to disk." — the natural way to say it one sentence
    /// after naming the verb — does not contain the word and so was never examined. The pronoun is
    /// the point.
    /// </summary>
    [Fact]
    public void NeitherSkillAttributesAWritingOrVerbingCapabilityToTheCommand()
    {
        // Denials are allowed and are required by the other test, so the pattern must tell a
        // denial from an attribution. What distinguishes them is what follows the verb, which the
        // two documents spell differently: "verifies nothing" against "does not write anything".
        // Both are captured as negation; anything else that puts an object after the verb is an
        // attribution.
        var negation = @"\s+(?:nothing|anything)|[^.\s]{0,30}\b(?:nothing|anything)\b|\bnot\b|\bnever\b|\bno\b";
        var attribution = new Regex(
            @"(?:`okf candidates`|\bIt\b|\bThis command\b)[^.]{0,80}?"
            + @"\b(writes?|verifies?|reviews?|stamps?|signs?|records?|measures?)\b(?![^.]{0,40}?(?:" + negation + @"))");

        foreach (var skill in VaultReadingSkills)
        {
            // Scoped to the section that documents this verb, not the whole file. A file-wide
            // pronoun scan is wrong, not merely loose: the vault skill's handoff section says "`okf
            // verify` is the human's word, spoken on request. It stamps …" and that "It" is
            // `okf verify`, which genuinely does stamp. Inside the candidates section, though,
            // "It writes a report file" can only read as being about candidates — which is the
            // contradiction AC 5 forbids, and the reason this scan exists at all.
            var offenders = new List<string>();
            foreach (var section in CandidatesSections(Skill(skill)))
            {
                foreach (var sentence in SentencesAbout(section, string.Empty))
                {
                    foreach (Match match in attribution.Matches(sentence))
                    {
                        offenders.Add(match.Value.Trim());
                    }
                }
            }

            Assert.Empty(offenders);
        }
    }

    /// <summary>
    /// The sections of a skill that document `okf candidates`, delimited by their `##`/`###`
    /// headings. A pronoun only has a referent inside the passage that names it, so this is the
    /// honest scope for a claim about what "it" is said to do.
    /// </summary>
    private static IEnumerable<string> CandidatesSections(string text)
    {
        var sections = new List<string>();
        foreach (var block in Regex.Split(text, @"(?m)^(?=#{2,3} )"))
        {
            if (block.Contains("okf candidates", StringComparison.Ordinal))
            {
                sections.Add(block);
            }
        }

        Assert.NotEmpty(sections);
        return sections;
    }

    [Fact]
    public void TheDocumentedSummaryLineAndExitCodesMatchTheBinary()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", "---\ntype: Concept\ntitle: Quiet\n---\n\nBody.\n");
        tree.Write("vault/bundles/b/quietest.md", "---\ntype: Concept\ntitle: Quietest\n---\n\nBody.\n");
        tree.Write("vault/bundles/b/lying.md", "---\ntype: Concept\ntitle: Lying\nverified: 42\n---\n\nBody.\n");

        var run = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle);

        // Oracle: what the command actually prints and actually exits, in the same run.
        Assert.Equal(CliApplication.ExitDiagnostics, run.ExitCode);
        string summary = run.OutputLines[^1];
        Assert.StartsWith("Scanned ", summary, StringComparison.Ordinal);

        // The comparison that matters is between the printed line and what the skills tell an
        // agent to expect from it. Two earlier revisions of this test were vacuous or wrong: the
        // first compared a hand-typed template to a hand-typed template, which agrees with a
        // stale document forever; the second placeholder-ised the live line and required that
        // string in the prose, which failed on `in 1 bundle` because the nouns pluralize with
        // their count. What a document can honestly promise is the ORDERED SET of counts and what
        // each counts, and that projection is taken from the live line rather than typed here.
        string[] counted = CountedNouns(summary);
        foreach (var skill in VaultReadingSkills)
        {
            string text = Unwrap(Skill(skill));

            foreach (var noun in counted)
            {
                Assert.Contains(noun, text, StringComparison.OrdinalIgnoreCase);
            }

            // The exit code the skills state must be the one this run returned.
            Assert.Contains(
                SentencesAbout(Skill(skill), "quarantined"),
                sentence => sentence.Contains($"exits {run.ExitCode}", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The nouns a summary line counts, singularised, in the order it counts them. This is the
    /// projection a document can promise without quoting one run's pluralisation: the same line
    /// reads `1 bundle` here and `40 bundles` in a wider vault, and a document that copied either
    /// spelling would be wrong about the other.
    /// </summary>
    private static string[] CountedNouns(string summary)
    {
        var nouns = Regex.Matches(summary, @"(?<=\d )[A-Za-z]+(?=[,:.]|$)")
            .Select(match => match.Value[^1] is 's' or 'S'
                ? match.Value[..^1]
                : match.Value)
            .ToArray();

        Assert.NotEmpty(nouns);
        return nouns;
    }

    [Fact]
    public void TheDocumentedJsonShapeMatchesTheBinary()
    {
        using var tree = new TempTree();
        var bundle = tree.CreateDirectory("vault/bundles/b");
        tree.Write("vault/bundles/b/quiet.md", "---\ntype: Concept\ntitle: Quiet\n---\n\nBody.\n");

        // The flag the documents actually print is `--format json`; `--json` is an alias. Running
        // the alias and then vouching for the spelling would let the two drift apart, which is
        // the drift this test exists to catch.
        var documented = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--format", "json");
        var alias = CliHarness.RunIn(tree.Root, tree.Root, "candidates", bundle, "--json");

        Assert.Equal(CliApplication.ExitSuccess, documented.ExitCode);
        Assert.Equal(documented.Output, alias.Output);

        // Oracle: stdout really is a bare array, which is what the skills promise a pipeline.
        using var document = JsonDocument.Parse(documented.Output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        foreach (var skill in VaultReadingSkills)
        {
            // And the spelling under test is the spelling in the document.
            Assert.Matches(@"--format json", Skill(skill));
            Assert.Matches(@"bare array", Skill(skill));
        }
    }

    [Fact]
    public void TheReadmeNamesTheVerbBesideTheReviewAndAcknowledgmentFlow()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

        // The bullet exists (it arrived with #75); #80's contribution is that it is now read as
        // part of the same flow as `okf verify`, and carries the inbox distinction.
        Assert.Matches(@"`okf candidates` / `okf verify`", readme);
        Assert.Matches(
            @"The inbox is a triage list of what is waiting on a person; this is an inventory of what has never been verified\.",
            Unwrap(readme));

        // The skills bullet says they carry the verb, since that is how an agent finds it.
        Assert.Matches(@"They name `okf candidates`", readme);
    }

    [Fact]
    public void TheHelpTextCarriesNoStrayProseFromACommit()
    {
        using var home = new TempTree();
        var run = CliHarness.RunIn(home.Root, home.Root, "candidates", "--help");

        // `okf candidates --help` is the option lookup the skills point an agent at, so its bytes
        // are user-facing text. A conventional-commit subject leaking into a raw string literal is
        // invisible to every compiler and analyzer, and survives review precisely because help
        // output is not read closely.
        Assert.DoesNotMatch(@"\((?:feat|fix|docs|refactor|perf|test|chore|ci)\([^)]*\)", run.Output);
        Assert.DoesNotContain("#7", run.Output, StringComparison.Ordinal);
    }

    private static string Skill(string name)
    {
        var path = Path.Combine(RepositoryRoot(), "skills", name, "SKILL.md");
        Assert.True(File.Exists(path), $"no skill at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The file's hand-wrapped lines joined into running text. Every prose assertion in this
    /// class runs on the result, so a claim is checked as the claim rather than as whatever
    /// fragments a hard wrap happens to cut it into.
    /// </summary>
    private static string Unwrap(string text) =>
        Regex.Replace(text, @"\s*\n\s*", " ");

    /// <summary>
    /// The sentences in which a word appears. A sentence is the unit a claim is made in, so it is
    /// the unit a denial has to be checked in — scanning the whole file for a verb would blame a
    /// sentence that never mentioned the command at all.
    /// </summary>
    private static IEnumerable<string> SentencesAbout(string text, string word)
    {
        string unwrapped = Unwrap(text).Replace("```", " ").Replace("`", string.Empty);

        foreach (var chunk in Regex.Split(unwrapped, @"(?<=[.!?])\s+"))
        {
            if (chunk.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                yield return chunk.Trim();
            }
        }
    }

    private static string RepositoryRoot() =>
        Repository.Root ?? throw new InvalidOperationException(
            "The tests are not running inside a checkout; the skill files have no oracle here.");
}
