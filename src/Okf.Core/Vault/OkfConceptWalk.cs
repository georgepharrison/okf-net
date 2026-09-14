namespace Okf.Core.Vault;

/// <summary>
/// A file the concept walk found but could not turn into a concept, because its frontmatter
/// could not be read. The repo's word for this is <i>unreadable</i>, the one both reports
/// already print ("Skipped N files whose frontmatter does not parse").
/// </summary>
/// <remarks>
/// <para>
/// A file with <b>no frontmatter block at all</b> is one of these, and that is work item #76.
/// It is not a parse failure — <c>OkfDocument.Parse</c> reads it as an empty mapping, which is
/// correct for <c>okf lint</c> and <c>okf search</c> and does not change — but it is a file
/// whose frontmatter was never found, so nothing about it can be established. Treating it as a
/// concept with provably-absent verification history let an author move a file between "review
/// candidate" and "invisible" by adding or deleting three dashes.
/// </para>
/// <para>
/// A file that cannot be read off the disk is not collected here either — the filesystem error
/// propagates and stops the walk, because that is an environment failure rather than a fact
/// about a file.
/// </para>
/// </remarks>
/// <param name="Bundle">The bundle the file sits in.</param>
/// <param name="Path">The bundle-relative path, with <c>/</c> separators.</param>
/// <param name="AbsolutePath">The file's absolute path.</param>
/// <param name="Read">Which of the three causes this is, so a report can name the fix.</param>
/// <param name="Reason">
/// The failure's own words where there are failure words to have — the parse exception's
/// message — and otherwise the cause's words. A consumer that only prints a message still
/// prints something true for a fenceless file, which is the case that has no exception
/// behind it.
/// </param>
internal readonly record struct OkfUnreadableConcept(
    OkfBundle Bundle,
    string Path,
    string AbsolutePath,
    OkfDocument.OkfFrontmatterRead Read,
    string Reason);

/// <summary>
/// The one authoritative answer to "which files in these bundles are concepts, and in what
/// order are they reported". Four decisions are made together here and nowhere else:
/// markdown-only selection, spec §3.1 reserved-file exclusion, a deterministic ordinal
/// order, and the separation between concepts whose frontmatter was read and files whose
/// frontmatter could not be. <see cref="OkfInboxScanner" /> consumes it; <c>okf candidates</c>
/// and every later whole-bundle surface will too.
/// </summary>
/// <remarks>
/// The four decisions are one primitive because they are one answer: a surface that
/// re-derives any of them is a surface that can disagree with another about what the
/// corpus is (AD-6). The order is fixed where it is decided — concepts first in bundle
/// order, then bundle-relative path ordinal, so the order a report prints is the order the
/// walk produced and never a filesystem's enumeration order. A file whose frontmatter cannot
/// be read is never dropped silently: it is a <c>OKF0001</c> error for <c>okf lint</c>,
/// and a surface that quietly skipped it would make a broken vault read as a clean one.
///
/// The unreadable files come back named rather than counted, because naming them is what
/// makes the count defensible: a surface that says "nothing needs attention" has to be
/// able to point at what it could not read. <c>okf inbox</c> prints only the count today,
/// which is one <c>.Count</c>, and <c>okf candidates</c> (issue #71) is the consumer that
/// names them — it refuses to call its inventory complete while any concept's history is
/// unknown, so it needs the path and the reason, not a number.
///
/// WHAT "UNREADABLE" MEANS HERE, AND WHAT IT DOES NOT MEAN FOR THE PARSER
/// ----------------------------------------------------------------------
/// Three shapes land in the unreadable list: no opening fence, a fence that never closes, and
/// a delimited block that does not parse to a mapping. The first is NOT a parse failure —
/// <see cref="OkfDocument.Parse" /> succeeds on it and returns empty frontmatter — so the walk
/// asks <see cref="OkfDocument.FrontmatterReadOf" /> alongside the parse rather than widening
/// the parser. The parser's leniency is <c>okf lint</c>'s and <c>okf search</c>'s requirement
/// (#76's note for the implementer) and does not change here: this is the walk's eligibility
/// answer, not a new parse.
/// </remarks>
internal static class OkfConceptWalk
{
    /// <summary>
    /// Reads every concept in the bundles, in walk order, and separates the files whose
    /// frontmatter could not be read from the concepts that came out of it. The two lists are
    /// disjoint and together are every non-reserved <c>.md</c> file the walk reached.
    /// </summary>
    /// <param name="bundles">The bundles to walk, in the order their concepts are reported.</param>
    /// <param name="today">The date staleness is judged against.</param>
    /// <returns>The concepts, and the files that did not parse.</returns>
    /// <remarks>
    /// A filesystem error is not caught here. <c>okf inbox</c> treats one as an environment
    /// failure and exits 2 (<c>InboxCommand.IsEnvironmentFailure</c>), so the walk lets it
    /// through rather than counting its way past a tree it could not see.
    /// </remarks>
    public static (List<OkfConcept> Concepts, List<OkfUnreadableConcept> Unreadable) Read(
        IEnumerable<OkfBundle> bundles,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(bundles);

        List<OkfConcept> concepts = new List<OkfConcept>();
        List<OkfUnreadableConcept> unreadable = new List<OkfUnreadableConcept>();

        foreach (OkfBundle bundle in bundles)
        {
            ReadBundle(bundle, today, concepts, unreadable);
        }

        return (concepts, unreadable);
    }

    /// <summary>
    /// Reads one bundle's concepts into the caller's lists, in walk order.
    /// </summary>
    /// <remarks>
    /// The fence test comes first and the parse is guarded second, in that order, because they
    /// answer different questions: whether a frontmatter block was FOUND, and whether what was
    /// found parsed. Only the second can throw, and a file that passes both becomes a concept —
    /// reading the lifecycle of what parsed cannot raise this, and if it ever did, counting it
    /// as an unreadable file would hide the bug.
    /// </remarks>
    private static void ReadBundle(
        OkfBundle bundle,
        DateOnly today,
        List<OkfConcept> concepts,
        List<OkfUnreadableConcept> unreadable)
    {
        foreach (string file in bundle.MarkdownFiles().Where(file => !OkfBundle.IsReservedFile(file)))
        {
            string text = File.ReadAllText(file);
            OkfDocument.OkfFrontmatterRead read = OkfDocument.FrontmatterReadOf(text);
            if (read != OkfDocument.OkfFrontmatterRead.Readable)
            {
                string reason = read == OkfDocument.OkfFrontmatterRead.Unparseable
                    ? ParseException(text)
                    : OkfDocument.MessageFor(read);
                unreadable.Add(new OkfUnreadableConcept(bundle, bundle.RelativePath(file), file, read, reason));
                continue;
            }

            concepts.Add(new OkfConcept(bundle, file, OkfDocument.Parse(text), today));
        }
    }

    /// <summary>
    /// The parse failure's own message for a block the classifier already decided cannot be
    /// read. <see cref="OkfDocument.FrontmatterReadOf" /> reports the CAUSE and this recovers
    /// the WORDS, because the cause is a stable enum for consumers while the message is what
    /// tells a person which line of the YAML is wrong. The catch cannot be reached by a shape
    /// the classifier did not already reject, so it degrades to the cause's words rather than
    /// pretending a message always exists.
    /// </summary>
    private static string ParseException(string text)
    {
        try
        {
            OkfDocument.Parse(text);
        }
        catch (OkfDocumentException exception)
        {
            return exception.Message;
        }

        return OkfDocument.MessageFor(OkfDocument.OkfFrontmatterRead.Unparseable);
    }
}
