namespace Okf.Core.Vault;

/// <summary>
/// A file the concept walk found but could not turn into a concept, because its
/// frontmatter does not parse. The repo's word for this is <i>unreadable</i>, the one both
/// reports already print ("Skipped N files whose frontmatter does not parse"). A file with
/// no frontmatter block at all is NOT one of these: <c>OkfDocument.Parse</c> reads that as
/// an empty mapping, so it comes out as a concept and <c>okf lint</c> reports its missing
/// <c>type</c> as <c>OKF0001</c>. A file that cannot be read off the disk is not collected
/// here either — the filesystem error propagates and stops the walk.
/// </summary>
/// <param name="Bundle">The bundle the file sits in.</param>
/// <param name="Path">The bundle-relative path, with <c>/</c> separators.</param>
/// <param name="AbsolutePath">The file's absolute path.</param>
/// <param name="Reason">The parse failure's own message.</param>
internal readonly record struct OkfUnreadableConcept(OkfBundle Bundle, string Path, string AbsolutePath, string Reason);

/// <summary>
/// The one authoritative answer to "which files in these bundles are concepts, and in what
/// order are they reported". Four decisions are made together here and nowhere else:
/// markdown-only selection, spec §3.1 reserved-file exclusion, a deterministic ordinal
/// order, and the separation between concepts that parsed and files whose frontmatter did
/// not. <see cref="OkfInboxScanner" /> consumes it; <c>okf candidates</c> and every later
/// whole-bundle surface will too.
/// </summary>
/// <remarks>
/// The four decisions are one primitive because they are one answer: a surface that
/// re-derives any of them is a surface that can disagree with another about what the
/// corpus is (AD-6). The order is fixed where it is decided — concepts first in bundle
/// order, then bundle-relative path ordinal, so the order a report prints is the order the
/// walk produced and never a filesystem's enumeration order. A file whose frontmatter does
/// not parse is never dropped silently: it is a <c>OKF0001</c> error for <c>okf lint</c>,
/// and a surface that quietly skipped it would make a broken vault read as a clean one.
///
/// The unreadable files come back named rather than counted, because naming them is what
/// makes the count defensible: a surface that says "nothing needs attention" has to be
/// able to point at what it could not read. <c>okf inbox</c> prints only the count today,
/// which is one <c>.Count</c>, and <c>okf candidates</c> (issue #71) is the consumer that
/// names them — it refuses to call its inventory complete while any concept's history is
/// unknown, so it needs the path and the reason, not a number.
/// </remarks>
internal static class OkfConceptWalk
{
    /// <summary>
    /// Reads every concept in the bundles, in walk order, and separates the files that
    /// could not be read from the concepts that came out of it. The two lists are disjoint
    /// and together are every non-reserved <c>.md</c> file the walk reached.
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
    /// Only the parse is guarded: reading the lifecycle of what parsed cannot raise this,
    /// and if it ever did, counting it as an unreadable file would hide the bug.
    /// </remarks>
    private static void ReadBundle(
        OkfBundle bundle,
        DateOnly today,
        List<OkfConcept> concepts,
        List<OkfUnreadableConcept> unreadable)
    {
        foreach (string file in bundle.MarkdownFiles().Where(file => !OkfBundle.IsReservedFile(file)))
        {
            OkfDocument document;
            try
            {
                document = OkfDocument.Parse(File.ReadAllText(file));
            }
            catch (OkfDocumentException exception)
            {
                unreadable.Add(new OkfUnreadableConcept(bundle, bundle.RelativePath(file), file, exception.Message));
                continue;
            }

            concepts.Add(new OkfConcept(bundle, file, document, today));
        }
    }
}
