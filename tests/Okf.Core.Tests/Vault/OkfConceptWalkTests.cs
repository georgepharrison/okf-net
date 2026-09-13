namespace Okf.Core.Tests.Vault;

/// <summary>
/// The one authoritative concept walk: which files are concepts, in what order, and what
/// happened to the files that are not (work item #72). Every expectation here comes from
/// spec §3.1 (reserved names) and §11 (every non-reserved <c>.md</c> in the tree is
/// conformant material), not from what the walk happens to do.
/// </summary>
/// <remarks>
/// These assertions are the reason the walk is a primitive rather than a loop inside the
/// inbox scanner: the answer is pinned here, once, so the surfaces that consume it — the
/// inbox today, <c>okf candidates</c> next — cannot each grow a private copy that
/// disagrees (AD-6).
/// </remarks>
public class OkfConceptWalkTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    /// <summary>
    /// Spec §3.1 reserves <c>index.md</c> and <c>log.md</c>: they carry a listing and an
    /// update history, not a concept. Everything else in the tree is a concept, which is
    /// what makes a foreign bundle readable without cooperation.
    /// </summary>
    [Fact]
    public void ReservedFilesAreNotConceptsButEveryOtherMarkdownFileIs()
    {
        using var bundle = new TempBundle();
        bundle.Add("index.md", Document("type: Index"))
            .Add("log.md", "# Directory Update Log\n")
            .Add("notes.md", Document("type: Concept"))
            .Add("nested/index.md", Document("type: Index"))
            .Add("nested/notes.md", Document("type: Concept"));

        (List<OkfConcept> concepts, List<OkfUnreadableConcept> unreadable) = Walk([bundle.Bundle]);

        Assert.Equal(["nested/notes.md", "notes.md"], concepts.Select(concept => concept.Path));
        Assert.Empty(unreadable);
    }

    /// <summary>
    /// Spec §11 conforms every non-reserved <c>.md</c> file in the tree, so a dot-prefixed
    /// name is a concept like any other. A walk that skipped it would report a bundle
    /// whole without having read it.
    /// </summary>
    [Fact]
    public void DotPrefixedMarkdownFilesAreConcepts()
    {
        using var bundle = new TempBundle();
        bundle.Add(".hidden.md", Document("type: Concept"))
            .Add(".notes/also-hidden.md", Document("type: Concept"))
            .Add("visible.md", Document("type: Concept"));

        (List<OkfConcept> concepts, _) = Walk([bundle.Bundle]);

        Assert.Equal([".hidden.md", ".notes/also-hidden.md", "visible.md"], concepts.Select(concept => concept.Path));
    }

    /// <summary>
    /// A non-markdown file is content (spec §6.3's <c>references/</c> covers code) but not
    /// a concept: the walk is markdown-only.
    /// </summary>
    [Fact]
    public void NonMarkdownFilesAreNotConcepts()
    {
        using var bundle = new TempBundle();
        bundle.Add("attester.py", "print('ok')\n")
            .Add("references/evidence.txt", "not markdown\n")
            .Add("notes.md", Document("type: Concept"));

        (List<OkfConcept> concepts, _) = Walk([bundle.Bundle]);

        Assert.Equal(["notes.md"], concepts.Select(concept => concept.Path));
    }

    /// <summary>
    /// The order is ordinal by bundle-relative path, not the filesystem's enumeration
    /// order: two runs over the same bytes report the same list, which is what lets a
    /// report be diffed and a near-duplicate rule name the same file every time.
    /// </summary>
    [Fact]
    public void ConceptsAreOrderedOrdinalByBundleRelativePath()
    {
        using var bundle = new TempBundle();
        bundle.Add("zebra.md", Document("type: Concept"))
            .Add("Alpha.md", Document("type: Concept"))
            .Add("alpha/a.md", Document("type: Concept"))
            .Add("beta.md", Document("type: Concept"));

        (List<OkfConcept> concepts, _) = Walk([bundle.Bundle]);

        // Uppercase sorts before lowercase because the comparison is ordinal, not
        // culture-aware: 'A' (0x41) is below 'a' (0x61).
        Assert.Equal(["Alpha.md", "alpha/a.md", "beta.md", "zebra.md"], concepts.Select(concept => concept.Path));
    }

    /// <summary>
    /// Bundles are walked in the order the caller named them, and that order is the whole
    /// ordering story across bundles — the working set's bundle order is the report order.
    /// </summary>
    [Fact]
    public void TwoBundlesAreReportedInBundleOrderNotPathOrderAcrossBoth()
    {
        using var zebra = new TempBundle("zebra");
        zebra.Add("alpha.md", Document("type: Concept")).Add("zulu.md", Document("type: Concept"));
        using var aardvark = new TempBundle("aardvark");
        aardvark.Add("alpha.md", Document("type: Concept")).Add("zulu.md", Document("type: Concept"));

        (List<OkfConcept> forward, _) = Walk([zebra.Bundle, aardvark.Bundle]);
        (List<OkfConcept> reversed, _) = Walk([aardvark.Bundle, zebra.Bundle]);

        Assert.Equal(
            ["zebra/alpha.md", "zebra/zulu.md", "aardvark/alpha.md", "aardvark/zulu.md"],
            forward.Select(concept => $"{concept.Bundle.Name}/{concept.Path}"));
        Assert.Equal(
            ["aardvark/alpha.md", "aardvark/zulu.md", "zebra/alpha.md", "zebra/zulu.md"],
            reversed.Select(concept => $"{concept.Bundle.Name}/{concept.Path}"));
    }

    /// <summary>
    /// The walk's two lists together are every non-reserved <c>.md</c> file in the tree,
    /// and nothing else: a file is either a concept or it is named as unreadable, never
    /// simply missing. That is what lets a consumer say "the inventory is complete" or
    /// point at the gap.
    /// </summary>
    [Fact]
    public void EveryNonReservedMarkdownFileIsEitherAConceptOrNamedAsUnreadable()
    {
        using var bundle = new TempBundle();
        bundle.Add("index.md", Document("type: Index"))
            .Add("log.md", "# Directory Update Log\n")
            .Add("notes.md", Document("type: Concept"))
            .Add("broken.md", "---\nkey: [unterminated\n---\n\nbody\n")
            .Add(".hidden.md", Document("type: Concept"))
            .Add("notes.md.txt", "not markdown\n");

        (List<OkfConcept> concepts, List<OkfUnreadableConcept> unreadable) = Walk([bundle.Bundle]);

        string[] accounted = concepts.Select(concept => concept.Path)
            .Concat(unreadable.Select(entry => entry.Path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([".hidden.md", "broken.md", "notes.md"], accounted);
    }

    /// <summary>
    /// A file whose frontmatter does not parse is reported, not dropped: it is an
    /// <c>OKF0001</c> error for <c>okf lint</c>, and a walk that silently omitted it would
    /// make a broken vault read as a clean one. The concept and unreadable lists are
    /// disjoint and together account for every non-reserved <c>.md</c> the walk found.
    /// </summary>
    /// <remarks>
    /// A file with no frontmatter block is NOT unreadable: <c>OkfDocument.Parse</c> reads
    /// one as an empty mapping, so it is a concept and <c>okf lint</c> reports its missing
    /// <c>type</c> (<c>OKF0001</c>) — see <see cref="AFrontmatterlessFileIsAConceptNotAnUnreadableFile" />.
    /// What lands here is a frontmatter block that opens and then fails to parse, or opens
    /// and never closes.
    /// </remarks>
    [Fact]
    public void AFileWhoseFrontmatterDoesNotParseIsReportedAsUnreadable()
    {
        using var bundle = new TempBundle();
        bundle.Add("good.md", Document("type: Concept"))
            .Add("broken.md", "---\nkey: [unterminated\n---\n\nbody\n")
            .Add("unterminated.md", "---\ntype: Concept\n\nbody with no closing fence\n");

        (List<OkfConcept> concepts, List<OkfUnreadableConcept> unreadable) = Walk([bundle.Bundle]);

        Assert.Equal(["good.md"], concepts.Select(concept => concept.Path));
        Assert.Equal(["broken.md", "unterminated.md"], unreadable.Select(entry => entry.Path));

        // Each reason is its own failure's words, not one shared string: a report names the
        // file with it, and an empty one would leave a consumer with a count and nothing to
        // say. The two shapes fail differently.
        Assert.Equal(
            ["Invalid YAML in frontmatter: While parsing a flow sequence, did not find expected ',' or ']'.",
                "Unterminated YAML frontmatter block"],
            unreadable.Select(entry => entry.Reason));
        Assert.All(
            unreadable,
            entry => Assert.Equal(bundle.Root + Path.DirectorySeparatorChar + entry.Path, entry.AbsolutePath));

        // `TempBundle.Bundle` mints a fresh OkfBundle per access, so identity is the root,
        // not the instance.
        Assert.All(unreadable, entry => Assert.Equal(bundle.Root, entry.Bundle.Root));
    }

    /// <summary>
    /// A file with no frontmatter block at all is a concept, not an unreadable file:
    /// <c>OkfDocument.Parse</c> reads it as an empty mapping. §11 still rejects it, but for
    /// a missing <c>type</c>, which is <c>okf lint</c>'s finding to make — the walk's
    /// question is only whether what it read parsed.
    /// </summary>
    [Fact]
    public void AFrontmatterlessFileIsAConceptNotAnUnreadableFile()
    {
        using var bundle = new TempBundle();
        bundle.Add("prose.md", "Just prose. Nobody wrote a frontmatter block.\n");

        (List<OkfConcept> concepts, List<OkfUnreadableConcept> unreadable) = Walk([bundle.Bundle]);

        OkfConcept concept = Assert.Single(concepts);
        Assert.Equal("prose.md", concept.Path);
        Assert.Null(concept.Type);
        Assert.Empty(unreadable);
    }

    /// <summary>
    /// The unreadable list is per-file, not a count: a consumer that reports names (the
    /// inbox reports a count today) must be able to, and a count is all a consumer that
    /// wants one needs.
    /// </summary>
    [Fact]
    public void UnreadableFilesKeepTheirBundleSoCountsAndNamesAreBothDerivable()
    {
        using var first = new TempBundle("first");
        first.Add("broken.md", "---\nkey: [unterminated\n---\n");
        using var second = new TempBundle("second");
        second.Add("also-broken.md", "---\nkey: [unterminated\n---\n")
            .Add("notes.md", Document("type: Concept"));

        (List<OkfConcept> concepts, List<OkfUnreadableConcept> unreadable) = Walk([first.Bundle, second.Bundle]);

        Assert.Single(concepts);
        Assert.Equal(
            [("first", "broken.md"), ("second", "also-broken.md")],
            unreadable.Select(entry => (entry.Bundle.Name, entry.Path)));
    }

    /// <summary>
    /// The three malformed shapes, and the one near-miss that is not one. What the walk
    /// calls unreadable is decided by what <c>OkfDocument.Parse</c> throws, not by how
    /// broken the file looks: bad YAML inside a block, and a block that never closes, are
    /// collected; a file with no block at all parses (as nothing) and is a concept.
    /// </summary>
    [Fact]
    public void OnlyAFrontmatterBlockThatFailsToParseIsUnreadable()
    {
        using var bundle = new TempBundle();
        bundle.Add("bad-yaml.md", "---\nkey: [unterminated\n---\n\nbody\n")
            .Add("unclosed.md", "---\ntype: Concept\n\nno closing fence\n")
            .Add("scalar-block.md", "---\n- just\n- a\n- list\n---\n\nbody\n")
            .Add("no-block.md", "Just prose.\n");

        (List<OkfConcept> concepts, List<OkfUnreadableConcept> unreadable) = Walk([bundle.Bundle]);

        Assert.Equal(["bad-yaml.md", "scalar-block.md", "unclosed.md"], unreadable.Select(entry => entry.Path));
        Assert.Equal(["no-block.md"], concepts.Select(concept => concept.Path));
    }

    /// <summary>
    /// The walk reads the same lifecycle fields every other surface reads, so a consumer
    /// gets a concept rather than a document: trust tier and staleness are already derived
    /// against the date it was handed, and the frontmatter it derived them from is the
    /// concept's own.
    /// </summary>
    [Fact]
    public void ConceptsComeOutWithTheirLifecycleJudgementsDerived()
    {
        using var bundle = new TempBundle();
        bundle.Add("expired.md", Document("type: Concept\nstale_after: 2026-08-14"))
            .Add("current.md", Document("type: Concept\nstale_after: 2026-08-16"));

        (List<OkfConcept> concepts, _) = Walk([bundle.Bundle]);

        // §5.5: stale when today is ON or after `stale_after`, so the 14th is stale on the
        // 15th and the 16th is not. The names are alphabetical, so the stale concept is the
        // second one the walk reports.
        Assert.Equal([false, true], concepts.Select(concept => concept.Stale));
        Assert.Equal(OkfTrustTier.Unverified, concepts[0].TrustTier);

        // The document behind each concept is the one read from that file, so a consumer
        // can reach the frontmatter the walk parsed rather than reading the file again.
        // (Ordinal path order puts the 16th first: "2026-08-16" is the `current.md` file.)
        Assert.Equal(
            ["2026-08-16", "2026-08-14"],
            concepts.Select(concept => OkfValues.Text(concept.Frontmatter, "stale_after")));
    }

    /// <summary>The concept id is the bundle-relative path minus <c>.md</c> (spec §2).</summary>
    [Fact]
    public void EachConceptCarriesItsIdAndAbsolutePath()
    {
        using var bundle = new TempBundle();
        bundle.Add("topics/notes.md", Document("type: Concept"));

        (List<OkfConcept> concepts, _) = Walk([bundle.Bundle]);

        OkfConcept concept = Assert.Single(concepts);
        Assert.Equal("topics/notes", concept.Id);
        Assert.Equal("topics/notes.md", concept.Path);
        Assert.Equal(bundle.Root + Path.DirectorySeparatorChar + "topics/notes.md".Replace('/', Path.DirectorySeparatorChar),
            concept.AbsolutePath);
    }

    /// <summary>
    /// A file that cannot be read at all stops the walk rather than being counted as
    /// unreadable. The distinction is the whole point of separating the two: a frontmatter
    /// failure is a fact about one file, while an unreadable file means the walk did not see
    /// the tree, and a report that counted its way past that would be describing a vault
    /// nobody checked. <c>okf inbox</c> relies on this — <c>InboxCommand</c> catches the
    /// filesystem error and exits 2.
    /// </summary>
    [SkippableFact]
    public void AFileThatCannotBeReadAtAllThrowsRatherThanBeingCountedUnreadable()
    {
        // POSIX permissions decide this; Windows ACLs do not. Restated for the
        // platform-compatibility analyzer, which cannot read `Skip.If`.
        Skip.If(OperatingSystem.IsWindows(), "POSIX permissions decide this; Windows ACLs do not.");
        Skip.If(Environment.UserName == "root", "root reads a mode-0000 file regardless.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var bundle = new TempBundle();
        bundle.Add("notes.md", Document("type: Concept"));
        string blocked = Path.Combine(bundle.Root, "blocked.md");
        File.WriteAllText(blocked, "---\ntype: Concept\n---\n\nbody\n");
        File.SetUnixFileMode(blocked, UnixFileMode.None);

        try
        {
            // A permission denial surfaces as UnauthorizedAccessException, which is NOT an
            // IOException — it is an IOException's sibling under SystemException. Both are
            // environment failures to `okf inbox`, so the assertion is that pair rather
            // than one of them by guesswork: what the walk must never do is turn either
            // into an unreadable-concept entry.
            Exception thrown = Assert.ThrowsAny<Exception>(() => OkfConceptWalk.Read([bundle.Bundle], Today));
            Assert.IsType<UnauthorizedAccessException>(thrown);
        }
        finally
        {
            File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static (List<OkfConcept> Concepts, List<OkfUnreadableConcept> Unreadable) Walk(
        IReadOnlyList<OkfBundle> bundles) =>
        OkfConceptWalk.Read(bundles, Today);

    private static string Document(string frontmatter) => $"---\n{frontmatter}\n---\n\nBody.\n";
}
