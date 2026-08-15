using Okf.Core;

namespace Okf.Core.Tests;

/// <summary>
/// The separator contract for bundle-relative paths (work item #36).
/// </summary>
/// <remarks>
/// <para>OKF bundle-relative paths are <c>/</c>-separated BY SPEC, and they are not an
/// internal detail: they are the <c>path</c> field of <c>okf search --format json</c>, the
/// <c>path</c> of every file in <c>okf-distribution.json</c>, the entry names inside the
/// distribution tar, the <c>link</c> of every generated index entry, and the href of every
/// page <c>okf site</c> writes. A consumer diffs them, resolves them and stores them.</para>
/// <para>Windows is where that contract is at risk, because
/// <see cref="Path.DirectorySeparatorChar" /> is <c>\</c> there and every
/// <see cref="Path.Combine(string,string)" /> and
/// <see cref="Path.GetRelativePath(string,string)" /> in the codebase produces it. These
/// tests run on Linux, where the bug they guard against cannot reproduce — so they are
/// written as assertions about the CONTRACT rather than about the platform: emitted paths
/// contain <c>/</c> and never <c>\</c>, and a <c>\</c> arriving from outside is refused
/// rather than interpreted. A Windows run cannot differ from these, because there is no
/// path through the code that would let it.</para>
/// </remarks>
public class OkfBundlePathTests
{
    /// <summary>
    /// The producer of every bundle-relative string in okf-net. Whatever the host's
    /// separator is, this emits the spec's.
    /// </summary>
    [Fact]
    public void RelativePath_emits_forward_slashes_for_a_nested_file()
    {
        using var tree = new PathVault();
        var bundle = new OkfBundle(tree.Bundle);

        var relative = bundle.RelativePath(
            Path.Combine(tree.Bundle, "topics", "deep", "widgets.md"));

        Assert.Equal("topics/deep/widgets.md", relative);
        Assert.DoesNotContain('\\', relative);
    }

    /// <summary>
    /// Every path the walk hands to the linter, the bundler, the indexer and the site
    /// builder round-trips through <see cref="OkfBundle.RelativePath" />, so the walk's own
    /// output is the first place a separator could leak.
    /// </summary>
    [Fact]
    public void Walked_files_have_slash_separated_bundle_relative_forms()
    {
        using var tree = new PathVault();
        var bundle = new OkfBundle(tree.Bundle);

        var relatives = bundle.MarkdownFiles().Select(bundle.RelativePath).ToList();

        Assert.Contains("topics/deep/widgets.md", relatives);
        Assert.All(relatives, relative => Assert.DoesNotContain('\\', relative));
        Assert.All(
            bundle.ContentFiles().Select(bundle.RelativePath),
            relative => Assert.DoesNotContain('\\', relative));
    }

    /// <summary>
    /// The walk's ORDER is part of the contract too — it is the order of <c>okf lint</c>'s
    /// diagnostics and of <c>okf index --json</c>'s entries — and it is fixed to the
    /// bundle-relative form rather than to the absolute one.
    /// </summary>
    /// <remarks>
    /// The fixture is chosen so the two orders would disagree on Windows and agree here:
    /// <c>topics/</c> against <c>topicsZ.md</c> straddles the separator, and <c>/</c>
    /// (0x2F) sorts below <c>Z</c> (0x5A) while <c>\</c> (0x5C) sorts above it. So this
    /// assertion is the POSIX projection of a rule that only bites on Windows — it pins
    /// the intended order, and the Windows half of it is unexercised until a Windows
    /// runner exists (see docs/decisions.md, work item #36).
    /// </remarks>
    [Fact]
    public void Walked_files_are_ordered_by_bundle_relative_path()
    {
        using var tree = new PathVault();
        var bundle = new OkfBundle(tree.Bundle);

        var relatives = bundle.MarkdownFiles().Select(bundle.RelativePath).ToList();

        Assert.Equal(
            relatives.Order(StringComparer.Ordinal).ToList(),
            relatives);
        Assert.True(
            relatives.IndexOf("topics/deep/widgets.md") < relatives.IndexOf("topicsZ.md"),
            "a subdirectory sorts before a sibling file whose name continues past the "
                + "separator, because the separator is '/' and not the host's");
    }

    /// <summary>
    /// The containment primitive refuses a backslash rather than resolving it, so one
    /// bundle-relative string cannot mean two different files on two platforms.
    /// </summary>
    [Fact]
    public void TryResolve_refuses_a_backslash_rather_than_treating_it_as_a_separator()
    {
        using var tree = new PathVault();
        var bundle = new OkfBundle(tree.Bundle);

        Assert.True(bundle.TryResolve("topics/deep/widgets.md", out var slashed));
        Assert.Equal(Path.Combine(tree.Bundle, "topics", "deep", "widgets.md"), slashed);

        Assert.False(bundle.TryResolve("topics\\deep\\widgets.md", out var backslashed));
        Assert.Null(backslashed);
    }

    /// <summary>
    /// The escape a backslash would otherwise buy on Windows: <c>..\</c> is not a
    /// separator here, so it can never climb.
    /// </summary>
    [Fact]
    public void TryResolve_refuses_a_backslash_traversal()
    {
        using var tree = new PathVault();
        var bundle = new OkfBundle(tree.Bundle);

        Assert.False(bundle.TryResolve("..\\outside.md", out _));
        Assert.False(bundle.TryResolve("topics\\..\\..\\outside.md", out _));
    }

    /// <summary>
    /// The three emitters a consumer actually reads: the generated index's links, the
    /// distribution manifest's file paths, and the search result's path. None of them may
    /// carry a host separator.
    /// </summary>
    [Fact]
    public void Emitted_bundle_relative_paths_never_carry_a_host_separator()
    {
        using var tree = new PathVault();
        var bundle = new OkfBundle(tree.Bundle);

        var plan = OkfIndexGenerator.Plan(bundle, new OkfIndexOptions());

        Assert.All(
            plan.Indexes.SelectMany(index => index.Entries).Select(entry => entry.Link),
            link => Assert.DoesNotContain('\\', link));
        Assert.Contains(
            plan.Indexes.SelectMany(index => index.Entries),
            entry => entry.Link == "deep/index.md");

        var outcome = OkfSearchEngine.Search(
            [bundle],
            OkfSearchQuery.Parse("widgets"),
            new OkfSearchOptions { Limit = 0 });

        Assert.NotEmpty(outcome.Results);
        Assert.All(outcome.Results, result => Assert.DoesNotContain('\\', result.Path));
        Assert.All(outcome.Results, result => Assert.DoesNotContain('\\', result.Id));
        Assert.Contains(outcome.Results, result => result.Path == "topics/deep/widgets.md");
    }

    /// <summary>A bundle whose shape makes the separator question answerable.</summary>
    private sealed class PathVault : IDisposable
    {
        private readonly string parent;

        public PathVault()
        {
            this.parent = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());
            Bundle = Path.Combine(this.parent, "bundles", "alpha");

            Write("index.md", "<!-- generated by okf -->\n\n# Guide\n");
            Write("topics/deep/widgets.md", Concept("Widgets", "Widgets, deeply nested."));
            Write("topics/deep/index.md", "<!-- generated by okf -->\n\n# Concept\n");
            Write("topics/index.md", "<!-- generated by okf -->\n\n# Concept\n");

            // The sibling that straddles the separator: `topics/` vs `topicsZ.md` order
            // differently under '/' and under '\'.
            Write("topicsZ.md", Concept("Topics Z", "A sibling that sorts against a directory."));

            // A file outside the bundle, for the traversal cases to aim at.
            File.WriteAllText(Path.Combine(this.parent, "outside.md"), "not bundle content\n");
        }

        /// <summary>The bundle root.</summary>
        public string Bundle { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.parent, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone; nothing to clean up.
            }
        }

        private static string Concept(string title, string description) =>
            $"---\ntype: Concept\ntitle: {title}\ndescription: {description}\n---\n\n# {title}\n\n{description}\n";

        private void Write(string relativePath, string content)
        {
            var path = Path.Combine(Bundle, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}
