using System.Text;
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
/// <para><b>Destinations carry an allowlisted scheme or none at all.</b> Disabling raw HTML
/// closes one door into the page and not the other: <c>[x](javascript:alert(1))</c>,
/// <c>&lt;javascript:alert(1)&gt;</c> and a reference definition pointing at one are all
/// ordinary markdown, and each renders an anchor a reader can click. On a page opened from
/// <c>file://</c> that anchor executes with no origin to contain it, which is the same hole
/// <c>DisableHtml</c> exists to shut. A destination that
/// carries any scheme outside <see cref="AllowedSchemes" /> is neutralized instead — it
/// keeps its text and is marked broken, because §6.1's tolerance rule says mark, never
/// drop.</para>
/// </remarks>
internal static class OkfSiteMarkdown
{
    /// <summary>
    /// The schemes a generated page may point at. A relative destination carries no scheme
    /// and is unaffected; everything else — <c>javascript:</c>, <c>vbscript:</c>,
    /// <c>data:</c>, <c>file:</c>, <c>about:</c> — is neutralized.
    /// </summary>
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

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

            // Checked before the destination is resolved, and for an image's `src` as well
            // as an anchor's `href`: a scheme this generator will not emit must not reach a
            // page by any route, including the one where `resolve` calls it external and
            // hands it back untouched.
            if (Blocked(url))
            {
                link.Url = Uri.EscapeDataString(url);
                link.GetAttributes().AddClass("broken");
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

        // A CommonMark autolink is neither a link inline nor raw HTML, so neither the loop
        // above nor `DisableHtml` has seen `<javascript:alert(1)>`. It carries its own
        // destination as its own text, so a blocked one becomes exactly the text it was
        // written as, with no anchor around it.
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (Blocked(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url))
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
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

    /// <summary>Whether a destination carries a scheme the site refuses to point at.</summary>
    /// <param name="url">The destination, as the parser decoded it.</param>
    /// <returns><see langword="true" /> when the destination must be neutralized.</returns>
    private static bool Blocked(string url) =>
        Scheme(url) is { Length: > 0 } scheme && !AllowedSchemes.Contains(scheme);

    /// <summary>The scheme a destination carries, or <see langword="null" /> when it is relative.</summary>
    /// <param name="url">The destination, as the parser decoded it.</param>
    /// <returns>The scheme, without its colon.</returns>
    /// <remarks>
    /// Read the way a browser reads it rather than the way <see cref="Uri" /> does. A browser
    /// drops ASCII whitespace and C0 controls before it decides what scheme a URL carries, so
    /// a destination written with a leading space, or with a tab spliced into the word by a
    /// numeric character reference, still navigates to a <c>javascript:</c> URL — and Markdig
    /// has already decoded that reference by the time this runs. Dropping them here too is
    /// what keeps the allowlist from being one whitespace character wide. Scheme matching is
    /// case-insensitive, because <c>JaVaScRiPt:</c> is the same URL.
    /// </remarks>
    private static string? Scheme(string url)
    {
        var builder = new StringBuilder(url.Length);
        foreach (var character in url)
        {
            if (character > ' ' && character != '\u007f')
            {
                builder.Append(character);
            }
        }

        var text = builder.ToString();
        var end = 0;
        while (end < text.Length
               && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '+' or '-' or '.'))
        {
            end++;
        }

        // RFC 3986 §3.1: a scheme is a letter followed by letters, digits, `+`, `-` and `.`,
        // and then a colon. Anything else — a leading `/`, a `#`, a bare relative path — has
        // no scheme, which is the ordinary case for a link inside a bundle.
        return end > 0 && end < text.Length && text[end] == ':' && char.IsAsciiLetter(text[0])
            ? text[..end]
            : null;
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
