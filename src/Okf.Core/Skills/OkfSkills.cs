using System.Reflection;
using System.Text;

namespace Okf.Core.Skills;

/// <summary>One agent skill, as it ships inside the binary.</summary>
/// <param name="Name">
/// The skill's directory name, which is also the <c>name</c> its frontmatter carries —
/// a skill host addresses a skill by that name, so the two are the same string.
/// </param>
/// <param name="Description">
/// The <c>description</c> from the frontmatter, folded to one line. It is the half of a
/// skill a host loads at startup: it says when the skill fires.
/// </param>
/// <param name="Content">The whole <c>SKILL.md</c>, exactly as it is in the repository.</param>
public sealed record OkfSkill(string Name, string Description, string Content);

/// <summary>
/// The agent skills, which ship inside the binary as embedded resources: an installed
/// <c>okf</c> carries the skills it was built with, and <c>okf skills install</c> needs no
/// network and no second download (AD-7).
/// </summary>
/// <remarks>
/// <para>The resources are globbed from <c>skills/*/SKILL.md</c> by
/// <c>Okf.Core.csproj</c>, so a skill added to the repository is embedded by the next
/// build; <c>OkfSkillsTests</c> asserts the embedded set equals the on-disk set, which is
/// what keeps the glob honest.</para>
/// <para><see cref="Assembly.GetManifestResourceStream(string)" /> is NativeAOT-safe — a
/// manifest resource is data in the image, not a type the trimmer has to keep — which is
/// the same reason the site assets are embedded rather than emitted as string literals:
/// the skills stay editable, lintable, reviewable markdown in the repository.</para>
/// </remarks>
public static class OkfSkills
{
    /// <summary>The file every skill directory holds, and the only file that is embedded.</summary>
    public const string SkillFileName = "SKILL.md";

    /// <summary>The directory a checkout keeps its skills in, and the one installs write.</summary>
    public const string DirectoryName = "skills";

    /// <summary>The logical-name prefix every embedded skill resource carries.</summary>
    public const string ResourcePrefix = "Okf.Core.Skills.";

    private static readonly Lazy<IReadOnlyList<OkfSkill>> Embedded = new(Load);

    /// <summary>Every embedded skill, ordered by name.</summary>
    public static IReadOnlyList<OkfSkill> All => Embedded.Value;

    /// <summary>Every embedded skill's name, ordered.</summary>
    public static IReadOnlyList<string> Names => [.. All.Select(skill => skill.Name)];

    /// <summary>The resource name a skill directory's <c>SKILL.md</c> is embedded under.</summary>
    /// <param name="name">The skill's name.</param>
    /// <returns>The manifest resource name.</returns>
    public static string ResourceNameFor(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return $"{ResourcePrefix}{name}.{SkillFileName}";
    }

    /// <summary>Finds one embedded skill by name.</summary>
    /// <param name="name">The skill's name.</param>
    /// <returns>The skill, or <see langword="null" /> when no skill has that name.</returns>
    public static OkfSkill? Find(string? name) =>
        All.FirstOrDefault(skill => string.Equals(skill.Name, name, StringComparison.Ordinal));

#pragma warning disable CA1859 // backs the frozen public All property (2026-08-15) via Lazy<IReadOnlyList<OkfSkill>>
    private static IReadOnlyList<OkfSkill> Load()
#pragma warning restore CA1859
    {
        var assembly = typeof(OkfSkills).Assembly;
        var suffix = $".{SkillFileName}";

        var skills = new List<OkfSkill>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = resource[ResourcePrefix.Length..^suffix.Length];
            var content = Read(assembly, resource);
            skills.Add(new OkfSkill(name, DescriptionOf(name, content), content));
        }

        skills.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return skills;
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"The embedded skill '{resource}' is missing from {assembly.GetName().Name}. " +
                "Skills are globbed into Okf.Core.csproj from skills/*/SKILL.md.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The one-line <c>description</c> a skill host preloads. A skill writes it as a
    /// folded YAML scalar over several lines, so the fold is undone here rather than at
    /// every call site.
    /// </summary>
    private static string DescriptionOf(string name, string content)
    {
        var frontmatter = OkfDocument.Parse(content).Frontmatter;
        if (frontmatter["description"] is not OkfScalar description)
        {
            throw new InvalidOperationException(
                $"The embedded skill '{name}' has no `description` in its frontmatter. " +
                "A skill host preloads name and description and reads nothing else until the " +
                "skill fires, so a skill without one can never be chosen.");
        }

        return string.Join(' ', description.Value.Split(
            (char[])[' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
