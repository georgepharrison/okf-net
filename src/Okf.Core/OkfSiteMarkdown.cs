using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Okf.Core;

/// <summary>Where one markdown link ended up once it was resolved against the site.</summary>
/// <param name="Href">The href to emit, or <see langword="null" /> to leave the original alone.</param>
/// <param name="TargetId">The site id the link resolved to, or <see langword="null" />.</param>
/// <param name="CssClass">A class for the anchor — <c>external</c>, <c>broken</c>, or none.</param>
/// <param name="IsExternal">Whether the link leaves the site and should open in a new tab.</param>
internal sealed record OkfSiteLink(string? Href, string? TargetId, string? CssClass, bool IsExternal);

/// <summary>What rendering one markdown body produced.</summary>
/// <param name="Html">The rendered HTML.</param>
/// <param name="LinksTo">The site ids the body links to, deduplicated, in first-appearance order.</param>
/// <param name="FootnoteOrders">Footnote label to the 1-based number Markdig gave it.</param>
internal sealed record OkfSiteBody(
    string Html,
    IReadOnlyList<string> LinksTo,
    IReadOnlyDictionary<string, int> FootnoteOrders);

/// <summary>
/// Markdown to HTML for the static site, with internal links rewritten in the syntax tree
/// rather than in the output text.
/// </summary>
/// <remarks>
/// <para><b>Raw HTML is disabled.</b> A body's HTML is escaped and shown as text instead of
/// being emitted. Two reasons, both structural: a bundle is data a consumer did not write —
/// okf-net is required to tolerate foreign bundles (PRD ACC-1) — and a generated page is
/// opened from <c>file://</c>, where a <c>&lt;script&gt;</c> smuggled through a concept body
/// would run with no origin to contain it; and the generator promises well-formed output,
/// which passthrough HTML cannot guarantee because nothing validates it upstream.</para>
/// <para><b>Links are rewritten on the AST.</b> Rewriting the rendered text with a regular
/// expression would have to re-implement the parser's idea of what a link is; setting
/// <see cref="LinkInline.Url" /> before rendering cannot disagree with it.</para>
/// </remarks>
internal static class OkfSiteMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseFootnotes()
        .UseAutoLinks()
        .UseTaskLists()
        .DisableHtml()
        .Build();

    /// <summary>Renders one body.</summary>
    /// <param name="markdown">The markdown body.</param>
    /// <param name="resolve">Resolves one raw link destination against the site.</param>
    /// <returns>The rendered HTML and what the render learned about the body.</returns>
    public static OkfSiteBody Render(string markdown, Func<string, OkfSiteLink> resolve)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(resolve);

        var document = Markdown.Parse(markdown, Pipeline);
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.Url is not { Length: > 0 } url)
            {
                continue;
            }

            var resolved = resolve(url);

            if (!link.IsImage && resolved.Href is { Length: > 0 } href)
            {
                link.Url = href;
            }

            if (resolved.TargetId is { Length: > 0 } target && seen.Add(target))
            {
                links.Add(target);
            }

            var attributes = link.GetAttributes();
            if (resolved.CssClass is { Length: > 0 } cssClass)
            {
                attributes.AddClass(cssClass);
            }

            if (resolved.IsExternal && !link.IsImage)
            {
                attributes.AddPropertyIfNotExist("target", "_blank");
                attributes.AddPropertyIfNotExist("rel", "noopener noreferrer");
            }
        }

        // Read before rendering: `Order` is assigned while the document is parsed, and it
        // is exactly the number the rendered `fn:N` anchors carry, so the scan does not
        // depend on what the footnote-group renderer does to the group on its way out.
        var orders = new Dictionary<string, int>(StringComparer.Ordinal);
        var position = 0;
        foreach (var footnote in document.Descendants<Footnote>())
        {
            position++;
            if (Label(footnote.Label) is { Length: > 0 } label)
            {
                orders[label] = footnote.Order > 0 ? footnote.Order : position;
            }
        }

        using var writer = new StringWriter { NewLine = "\n" };
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return new OkfSiteBody(writer.ToString(), links, orders);
    }

    /// <summary>
    /// The bare footnote label. Markdig records it in its source form, which carries the
    /// leading <c>^</c> (and, in some shapes, the surrounding brackets); §5.1's join key
    /// into <c>sources[].id</c> is what is left once those are stripped.
    /// </summary>
    /// <param name="label">The label as Markdig recorded it.</param>
    /// <returns>The label without its footnote delimiters.</returns>
    private static string Label(string? label)
    {
        var text = (label ?? string.Empty).Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            text = text[1..^1];
        }

        return text.StartsWith('^') ? text[1..] : text;
    }
}
