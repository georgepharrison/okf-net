using System.Reflection;
using System.Text;

namespace Okf.Core.Site;

/// <summary>
/// The site's stylesheet and client script, which ship inside the binary as embedded
/// resources.
/// </summary>
/// <remarks>
/// <para>Both assets are okf-net's own: the generated site loads no third-party CSS or
/// JavaScript, from a CDN or otherwise. A page opened from <c>file://</c> with no network
/// must look and behave exactly as one served from GitLab Pages, and a knowledge site that
/// phoned a CDN on every page view would report the reader's browsing to a third party.</para>
/// <para><see cref="Assembly.GetManifestResourceStream(string)" /> is
/// NativeAOT-safe — a manifest resource is data in the image, not a type the trimmer has to
/// keep — which is why the assets are embedded rather than emitted from string literals in
/// C#: they stay editable, lintable, diffable files in the repository.</para>
/// </remarks>
public static class OkfSiteAssets
{
    /// <summary>The resource name of the stylesheet.</summary>
    public const string StyleSheetResource = "Okf.Core.Assets.site.css";

    /// <summary>The resource name of the client script.</summary>
    public const string ScriptResource = "Okf.Core.Assets.site.js";

    /// <summary>The site-relative path the stylesheet is written to in multi-page mode.</summary>
    public const string StyleSheetPath = "assets/site.css";

    /// <summary>The site-relative path the client script is written to in multi-page mode.</summary>
    public const string ScriptPath = "assets/site.js";

    /// <summary>The site-relative path the embedded site data is written to in multi-page mode.</summary>
    public const string DataPath = "assets/site-data.js";

    /// <summary>The stylesheet's text.</summary>
    public static string StyleSheet => Read(StyleSheetResource);

    /// <summary>The client script's text.</summary>
    public static string Script => Read(ScriptResource);

    private static string Read(string resource)
    {
        Assembly assembly = typeof(OkfSiteAssets).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"The embedded site asset '{resource}' is missing from {assembly.GetName().Name}. " +
                "It is declared as an EmbeddedResource in Okf.Core.csproj.");

        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
