using System.Globalization;
using System.Text;

namespace Okf.Core;

/// <summary>
/// Raised when a vault must not be scaffolded where it was asked for. Callers map it to
/// the CLI's usage/environment exit code (PRD CLI-14, exit 2) — a refusal is not a
/// diagnostic about a bundle, it is a request that cannot be carried out.
/// </summary>
public class OkfScaffoldException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OkfScaffoldException" /> class.</summary>
    public OkfScaffoldException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfScaffoldException" /> class.</summary>
    /// <param name="message">The error message.</param>
    public OkfScaffoldException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OkfScaffoldException" /> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public OkfScaffoldException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>What <c>okf init</c> did to one file.</summary>
public enum OkfScaffoldStatus
{
    /// <summary>The file was missing and has been written.</summary>
    Created = 0,

    /// <summary>The file was already there and was left exactly as found.</summary>
    Exists,
}

/// <summary>How a scaffolded recipe names one skill.</summary>
/// <param name="Name">The skill's name.</param>
/// <param name="Resource">
/// What the recipe's <c>resource</c> says: a project-relative path when this project has
/// the skill on disk, else the machine-independent instruction form SPEC §5.1 allows.
/// </param>
/// <param name="IsPath">Whether the resource is a path that resolves in this project.</param>
public sealed record OkfSkillPointer(string Name, string Resource, bool IsPath);

/// <summary>One file in a scaffolded vault, and what became of it.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Status">Whether the run created it or found it.</param>
public sealed record OkfScaffoldFile(string Path, OkfScaffoldStatus Status);

/// <summary>Everything <see cref="OkfScaffold" /> needs beyond the target directory.</summary>
public sealed class OkfScaffoldOptions
{
    /// <summary>
    /// The bundle directory's name. <see langword="null" /> derives one from the vault's
    /// parent directory.
    /// </summary>
    public string? BundleName { get; set; }

    /// <summary>
    /// The instant stamped into the scaffolded concept and its log entry. Injected so a
    /// scaffold is reproducible in tests; written in the canonical form
    /// (<see cref="OkfCanonicalTimestamp" />).
    /// </summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The actor recorded as <c>generated.by</c>. The tool wrote the file, and saying so
    /// is what keeps the §5.3 trust tier honest: a scaffolded concept is machine-written
    /// and unverified, not somebody's reviewed prose.
    /// </summary>
    public string Actor { get; set; } = "okf";
}

/// <summary>What one <c>okf init</c> run produced.</summary>
public sealed class OkfScaffoldResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="vaultRoot">The vault that was scaffolded.</param>
    /// <param name="bundleRoot">The bundle root inside it.</param>
    /// <param name="files">Every file the layout calls for, and what became of each.</param>
    /// <param name="skillPointers">How the custodian recipe names each skill.</param>
    public OkfScaffoldResult(
        string vaultRoot,
        string bundleRoot,
        IReadOnlyList<OkfScaffoldFile> files,
        IReadOnlyList<OkfSkillPointer> skillPointers)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultRoot);
        ArgumentException.ThrowIfNullOrEmpty(bundleRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(skillPointers);
        VaultRoot = vaultRoot;
        BundleRoot = bundleRoot;
        Files = files;
        SkillPointers = skillPointers;
    }

    /// <summary>The vault root, <c>&lt;project&gt;/okf</c> or the personal vault.</summary>
    public string VaultRoot { get; }

    /// <summary>The bundle root, <c>&lt;vault&gt;/bundles/&lt;name&gt;</c>.</summary>
    public string BundleRoot { get; }

    /// <summary>Every file the layout calls for, in write order, and what became of each.</summary>
    public IReadOnlyList<OkfScaffoldFile> Files { get; }

    /// <summary>How the custodian recipe names each skill, in the order the recipe lists them.</summary>
    public IReadOnlyList<OkfSkillPointer> SkillPointers { get; }

    /// <summary>How many files this run wrote.</summary>
    public int CreatedCount => Files.Count(file => file.Status == OkfScaffoldStatus.Created);

    /// <summary>How many files were already there and left untouched.</summary>
    public int ExistingCount => Files.Count(file => file.Status == OkfScaffoldStatus.Exists);

    /// <summary>Whether the run changed nothing — a re-run on an initialized vault.</summary>
    public bool IsNoOp => CreatedCount == 0;
}

/// <summary>
/// Scaffolds the vault layout decisions.md §2 describes: a repo-facing <c>README.md</c>
/// outside every bundle root, one bundle with a conformant first concept and a generated
/// index, the <c>raw/</c> drop zone with its capture manifest, the custodian directory,
/// and the project config that is the team's committed lint contract (PRD CLI-8).
/// </summary>
/// <remarks>
/// <para><b>It never overwrites content.</b> Every file is written only when it is
/// missing, so a second run on an initialized vault reports what is there and changes
/// nothing. That is what makes `okf init` safe to put in a setup script, and it is why
/// nothing here merges, patches, or "fixes" a file somebody already owns.</para>
/// <para><b>It refuses rather than trapping.</b> A vault inside a bundle root would put a
/// frontmatter-less <c>README.md</c> in that root and break §11 conformance — the
/// best-documented gotcha in decisions.md §2, which until now only surfaced later as a
/// generic <c>OKF0001</c> (dogfood friction #19-14). It is refused up front, by name.</para>
/// </remarks>
public static class OkfScaffold
{
    /// <summary>The bundle name used for a personal vault when none is given.</summary>
    public const string PersonalBundleName = "personal";

    /// <summary>The first concept every scaffolded bundle carries (decisions.md §2).</summary>
    public const string AboutThisBundleFileName = "about-this-bundle.md";

    /// <summary>The vault's markdownlint configuration, written for the MD025 collision.</summary>
    public const string MarkdownLintFileName = ".markdownlint.yaml";

    /// <summary>The custodian directory inside a vault (decisions.md §1, §2).</summary>
    public const string CustodianDirectoryName = "custodian";

    /// <summary>The tags <c>about-this-bundle.md</c> is scaffolded with, and the registry's seed.</summary>
    private static readonly string[] SeedTags = ["bundle", "meta", "okf"];

    /// <summary>
    /// The producer-side skills a scaffolded recipe names, and when each fires. The
    /// consumer-side <c>okf-vault</c> ships beside them and is not custodian machinery, so
    /// the recipe does not list it.
    /// </summary>
    private static readonly (string Name, string When)[] RecipeSkills =
    [
        ("okf-capture",
            "Something was just learned and belongs in the bundle: search first, apply the capture-vs-cite test, "
            + "drop what cannot defend itself into raw/ with a manifest entry, write the concept."),
        ("okf-custodian",
            "A capture is waiting in raw/, or the bundle needs enrichment, index regeneration, lint clearing, "
            + "or a log.md line."),
    ];

    /// <summary>How many symbolic links one path resolution will follow before giving up.</summary>
    private const int MaxLinkDepth = 40;

    /// <summary>
    /// The markdownlint configuration filenames looked for beside the vault, so the
    /// vault's own config can <c>extends</c> the host repo's instead of replacing it.
    /// </summary>
    private static readonly string[] MarkdownLintConfigNames =
    [
        ".markdownlint.yaml",
        ".markdownlint.yml",
        ".markdownlint.json",
        ".markdownlint.jsonc",
    ];

    /// <summary>
    /// Works out which vault a target path means, exactly as
    /// <see cref="OkfDiscovery" /> would read it: a directory that already holds
    /// <c>bundles/</c> is the vault, anything else is a project root whose vault is
    /// <c>&lt;path&gt;/okf</c>.
    /// </summary>
    /// <param name="path">The target path, absolute or relative to <paramref name="environment" />.</param>
    /// <param name="environment">The environment the path resolves against.</param>
    /// <returns>The vault root that would be scaffolded.</returns>
    public static string ResolveVault(string? path, OkfEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var target = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(environment.CurrentDirectory, path ?? ".")));

        return Directory.Exists(Path.Combine(target, OkfDiscovery.BundlesDirectoryName))
            ? target
            : Path.Combine(target, OkfDiscovery.VaultDirectoryName);
    }

    /// <summary>
    /// Scaffolds a vault, writing only the files that are missing.
    /// </summary>
    /// <param name="vaultRoot">The vault root to scaffold, absolute.</param>
    /// <param name="options">The bundle name, clock, and actor; defaults are used when null.</param>
    /// <returns>What the run found and what it wrote.</returns>
    /// <exception cref="OkfScaffoldException">The vault must not be scaffolded there.</exception>
    /// <exception cref="IOException">A file could not be written.</exception>
    public static OkfScaffoldResult Initialize(string vaultRoot, OkfScaffoldOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultRoot);
        options ??= new OkfScaffoldOptions();

        var vault = Path.TrimEndingDirectorySeparator(Path.GetFullPath(vaultRoot));
        RefuseBundleRoot(vault);

        if (File.Exists(vault))
        {
            throw new OkfScaffoldException($"'{vault}' is a file; okf init needs a directory.");
        }

        var name = BundleName(vault, options.BundleName);
        var bundleRoot = Path.Combine(vault, OkfDiscovery.BundlesDirectoryName, name);
        var stamp = OkfCanonicalTimestamp.ToCanonical(options.Now);
        var day = options.Now.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Directory.CreateDirectory(bundleRoot);
        Directory.CreateDirectory(Path.Combine(vault, CustodianDirectoryName));
        Directory.CreateDirectory(Path.Combine(vault, OkfCaptureManifest.RawDirectoryName));

        var pointers = SkillPointers(vault);

        var files = new List<OkfScaffoldFile>
        {
            Write(Path.Combine(vault, "README.md"), VaultReadme(name)),
            Write(Path.Combine(vault, OkfDiscovery.ConfigFileName), ProjectConfig(name)),
            Write(Path.Combine(vault, MarkdownLintFileName), MarkdownLintConfig(vault)),
            Write(Path.Combine(vault, CustodianDirectoryName, "README.md"), CustodianReadme(name, pointers)),
            Write(Path.Combine(vault, CustodianDirectoryName, "recipe.json"), CustodianRecipe(name, pointers)),
            Write(Path.Combine(vault, OkfCaptureManifest.RawDirectoryName, ".gitignore"), RawGitignore()),
            Write(
                Path.Combine(vault, OkfCaptureManifest.RawDirectoryName, OkfCaptureManifest.FileName),
                OkfCaptureWriter.EmptyManifest),
            Write(Path.Combine(bundleRoot, AboutThisBundleFileName), AboutThisBundle(name, options.Actor, stamp)),
            Write(Path.Combine(bundleRoot, OkfBundle.LogFileName), BundleLog(day)),
        };

        files.Add(WriteRootIndex(bundleRoot));

        return new OkfScaffoldResult(vault, bundleRoot, files, pointers);
    }

    /// <summary>
    /// Writes the bundle-root <c>index.md</c> through the real generator rather than from
    /// a template, so a scaffolded index is byte-identical to what <c>okf index</c> would
    /// write a second later and cannot be born drifted (<c>OKF0306</c>).
    /// </summary>
    private static OkfScaffoldFile WriteRootIndex(string bundleRoot)
    {
        var path = Path.Combine(bundleRoot, OkfBundle.IndexFileName);
        if (File.Exists(path))
        {
            return new OkfScaffoldFile(path, OkfScaffoldStatus.Exists);
        }

        var bundle = new OkfBundle(bundleRoot);
        var index = OkfIndexGenerator.Plan(bundle).For(bundleRoot)
            ?? throw new OkfScaffoldException(
                $"'{bundleRoot}' has nothing to index; okf init writes {AboutThisBundleFileName} first.");

        File.WriteAllText(path, index.Content);
        return new OkfScaffoldFile(path, OkfScaffoldStatus.Created);
    }

    private static OkfScaffoldFile Write(string path, string content)
    {
        if (File.Exists(path))
        {
            return new OkfScaffoldFile(path, OkfScaffoldStatus.Exists);
        }

        File.WriteAllText(path, content);
        return new OkfScaffoldFile(path, OkfScaffoldStatus.Created);
    }

    /// <summary>
    /// Refuses a vault that would sit inside a bundle root — the README trap, surfaced by
    /// name at the moment it can still be avoided rather than later as a generic
    /// <c>OKF0001</c> on a file somebody committed (dogfood friction #19-14).
    /// </summary>
    /// <remarks>
    /// <para>The check reads the <em>physical</em> path, not the one that was typed:
    /// <see cref="Path.GetFullPath(string)" /> normalizes <c>.</c> and <c>..</c> lexically
    /// but does not follow symbolic links, and a lexical path is not where the write
    /// lands. A link into a bundle root — or a link anywhere along the way — walked
    /// straight past a lexical check and put the README in a real bundle root.</para>
    /// <para>It also does not care whether the <c>bundles/</c> directory exists yet.
    /// Requiring it made the refusal describe the tree before the run rather than after:
    /// <c>okf init &lt;vault&gt;/bundles/new</c> on a vault whose <c>bundles/</c> had not
    /// been created was allowed, and creating it was exactly what turned <c>new</c> into a
    /// bundle root with a README in it.</para>
    /// </remarks>
    private static void RefuseBundleRoot(string vault)
    {
        var real = RealPath(vault);
        var shown = string.Equals(real, vault, StringComparison.Ordinal)
            ? $"'{vault}'"
            : $"'{vault}' (which resolves to '{real}')";

        for (var directory = new DirectoryInfo(real); directory is not null; directory = directory.Parent)
        {
            var parent = directory.Parent;
            if (parent is null
                || !string.Equals(parent.Name, OkfDiscovery.BundlesDirectoryName, StringComparison.Ordinal)
                || parent.Parent is null)
            {
                continue;
            }

            var relation = string.Equals(directory.FullName, real, StringComparison.Ordinal)
                ? "is a bundle root"
                : $"sits inside the bundle root '{directory.FullName}'";

            throw new OkfScaffoldException(
                $"Refusing to initialize: {shown} {relation}. " +
                "okf init writes a README.md at the vault root, and OKF v0.2 does not reserve that name — " +
                "inside a bundle root it would be read as a frontmatter-less concept and fail §11 conformance. " +
                "A bundle root is never a vault root (decisions.md §2); initialize beside the bundles, not in one.");
        }
    }

    /// <summary>
    /// A path with every symbolic link in it followed. The components that do not exist
    /// yet are the ones this run is about to create, so they cannot be links and are kept
    /// exactly as given.
    /// </summary>
    private static string RealPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        var pending = new Stack<string>();
        var existing = full;
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (string.IsNullOrEmpty(parent))
            {
                return full;
            }

            pending.Push(Path.GetFileName(existing));
            existing = parent;
        }

        var budget = MaxLinkDepth;
        var resolved = Resolve(existing, ref budget);
        while (pending.Count > 0)
        {
            resolved = Path.Combine(resolved, pending.Pop());
        }

        return resolved;
    }

    /// <summary>
    /// Resolves one existing path, parents first, so a link found anywhere along it is
    /// followed and its own parents are resolved in turn. The budget bounds the link hops
    /// rather than the descent, and a loop of links runs it out and stops.
    /// </summary>
    private static string Resolve(string path, ref int budget)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent) || budget <= 0)
        {
            return path;
        }

        var candidate = Path.Combine(Resolve(parent, ref budget), Path.GetFileName(path));
        if (LinkTarget(candidate) is not { } target)
        {
            return candidate;
        }

        budget--;
        return Resolve(target, ref budget);
    }

    /// <summary>
    /// Where a path points when it is a symbolic link, as an absolute path; <see
    /// langword="null" /> when it is not one, or cannot be read. An unreadable component
    /// is left as written — the refusal is about layout, and a path this cannot inspect is
    /// a path the write will fail on anyway.
    /// </summary>
    private static string? LinkTarget(string path)
    {
        try
        {
            if (new DirectoryInfo(path).LinkTarget is not { Length: > 0 } target)
            {
                return null;
            }

            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(target, Path.GetDirectoryName(path) ?? path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The bundle directory's name: the one given, else the vault's parent directory —
    /// the project — which is the name a reader already associates with the knowledge.
    /// </summary>
    private static string BundleName(string vault, string? requested)
    {
        if (requested is not null)
        {
            return Validate(requested, "The --name given");
        }

        var parent = Path.GetFileName(Path.GetDirectoryName(vault) ?? string.Empty);
        var derived = Sanitize(parent);
        return derived.Length > 0
            ? derived
            : throw new OkfScaffoldException(
                $"Cannot derive a bundle name from '{vault}'; pass --name <bundle>.");
    }

    private static string Validate(string name, string subject)
    {
        if (name.Length == 0
            || name.StartsWith('.')
            || name.Contains('/', StringComparison.Ordinal)
            || name.Contains('\\', StringComparison.Ordinal)
            || name.Any(character => Path.GetInvalidFileNameChars().Contains(character)))
        {
            throw new OkfScaffoldException(
                $"{subject}, '{name}', is not a usable directory name: a bundle name is a single " +
                "directory name, not a path, and must not start with a dot.");
        }

        return name;
    }

    /// <summary>
    /// Turns a directory name into a bundle name: lowercase, with runs of anything that
    /// is not a letter, digit, dot or underscore collapsed to a single hyphen.
    /// </summary>
    private static string Sanitize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '_')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-', '.');
    }

    private static string VaultReadme(string name) => $$"""
        # The {{name}} OKF vault

        This directory is an **OKF v0.2 knowledge vault**: curated knowledge kept as
        plain markdown, readable with nothing but a text editor, and validated by
        `okf` in a hook or a CI job.

        ## Layout

        ```text
        okf/
        ├─ README.md          # this file — repo-facing, deliberately OUTSIDE every bundle root
        ├─ okf.json           # project config: the team contract for lint severities and tags
        ├─ .markdownlint.yaml # the one markdownlint rule OKF markdown cannot satisfy
        ├─ custodian/         # the machinery that maintains the bundle (the bundler strips it)
        ├─ raw/               # captured artifacts + manifest.json, OUTSIDE every bundle root
        └─ bundles/
           └─ {{name}}/{{Padding(name)}}# the bundle root
        ```

        `README.md` sits here rather than inside `bundles/{{name}}/` on purpose. OKF
        does not reserve the name `README.md`, so a README inside a bundle root would
        be read as a frontmatter-less concept and would fail §11 conformance. The rule
        that falls out of it — **a bundle root is never a vault root** — is why `okf
        init` refuses to scaffold a vault inside one.

        `raw/` is the drop zone for captured artifacts, and it is **committed**. It
        sits outside every bundle root for the same reason: a dropped `.html` or `.pdf`
        inside one would be a frontmatter-less concept the moment it landed. Nothing
        indexes it, searches it, or ships it. `raw/manifest.json` is the immutability
        record and the one place a captured item's original URL is guaranteed to
        survive; `raw/.gitignore` un-ignores everything, so a repo-wide pattern like
        `*.log` can never quietly eat the evidence.

        `custodian/` holds the machinery that maintains the bundle: the recipe naming
        the skills that do the work, and any checks CI runs beside `okf lint`. It is
        producer-side, and the bundler strips it — a consumer receives readable
        markdown that runs nothing.

        ## Working with it

        | Task | Command |
        | --- | --- |
        | Lint the vault | `okf lint okf/` |
        | Regenerate indexes | `okf index okf/bundles/{{name}}` |
        | Fail on index drift | `okf index --check okf/bundles/{{name}}` |
        | Search the bundle | `okf search "<query>" okf/` |

        Run all four from the repository root, and run the first two in CI: a bundle
        nothing checks drifts the same way documentation does.

        ## What belongs here

        Curated knowledge, written to be found again months later by someone who has
        forgotten where they filed it. Each concept carries its `type`, a
        `description` that has to work as a one-line index entry, tags from the
        registry in `okf.json`, and provenance in `sources` — a claim with no source
        is an opinion the bundle cannot defend.

        """;

    /// <summary>Pads the tree diagram's bundle line so the comment column lines up.</summary>
    private static string Padding(string name)
    {
        // Every other comment in the diagram opens at the same column; the bundle line is
        // indented three characters further and carries a trailing slash, which leaves
        // its name exactly this much room before the comment.
        const int room = 15;
        return new string(' ', Math.Max(1, room - name.Length));
    }

    private static string ProjectConfig(string name) => $$"""
        {
          // {{name}}'s project config: the committed team contract for this vault.
          // It is the middle configuration layer — CLI arguments win over it, and a
          // global ~/.config/okf/okf.json sits underneath it.
          //
          // THIS FILE IS JSONC. It is named `.json` because that is the name `okf`
          // looks for, and it is parsed with comments and trailing commas allowed,
          // deliberately: a severity promotion without a stated reason is a promotion
          // nobody can review, and the reasons are the half of a config file a reader
          // actually needs. A strict JSON editor or schema will flag the comments; the
          // tool will not.
          "lint": {
            "severities": {
              // OKF0301 missing-description: a concept without a description renders as
              // a bare link in its index and a snippet-less search hit. Every entry
              // point into this vault degrades at once, so it is an error here.
              "OKF0301": "error",

              // OKF0306 generated-index-drift: an index.md we generated no longer
              // matches its tree. `okf index` regenerates it; nothing here is
              // hand-edited, so drift means someone edited an output.
              "OKF0306": "error",

              // OKF0307 missing-source-resource: provenance is the load-bearing half of
              // the trust model, so a `sources` entry recording something nobody can go
              // and check is a defect, whatever it is elsewhere.
              "OKF0307": "error",

              // OKF0310 raw-item-mutated: an ingested raw/ item no longer matches the
              // sha256 its capture-manifest entry records. That artifact is the evidence
              // a concept rests on; if it changed, the concept is citing something that
              // is no longer there. Resolve it by hand — never by rewriting the manifest.
              "OKF0310": "error",

              // --- Tag governance -------------------------------------------------
              //
              // Both at WARNING, and both from the vault's first commit. The registry
              // below is what closes the vocabulary: with it present, a new tag can no
              // longer arrive by accident, because CI names it the moment it appears.
              // An error would be too much on day one — the first author reaching for
              // an obviously right new word would get a red pipeline instead of a
              // review comment. Promote to error once the registry has survived a
              // consolidation pass and a few real additions.
              //
              // OKF0305 unregistered-tag: a tag absent from `tagRegistry`.
              "OKF0305": "warning",

              // OKF0304 missing-tags: a concept with no `tags` at all. It comes with
              // OKF0305 or not at all — a concept with no tags is trivially compliant
              // with any registry, so promoting only the unregistered-tag rule would
              // make deleting the key the cheapest way to satisfy tag governance.
              "OKF0304": "warning"
            },

            // The bundle's tag vocabulary. `okf lint` accepts a tag only if it appears
            // here, spelled exactly this way (the comparison is ordinal).
            //
            // It starts at the three tags `okf init` gave about-this-bundle.md, and it
            // is present rather than absent on purpose: a registry introduced later has
            // to be introduced by a change that is simultaneously unenforceable and
            // enormous, because every tag the bundle already grew arrives at once. From
            // here every addition is one line, reviewed beside the concept that needed
            // the word.
            //
            // How a tag gets added: in the same change as the concept that needs it,
            // with that concept as the argument for it. Group them by facet as they
            // accumulate — scope, domain, kind — so a reviewer can see where a proposed
            // tag would sit, and see when it does not sit anywhere.
            "tagRegistry": [
        {{TagRegistryLines()}}
            ]
          }
        }

        """;

    private static string TagRegistryLines() =>
        string.Join(",\n", SeedTags.Select(tag => $"      \"{tag}\""));

    private static string MarkdownLintConfig(string vault)
    {
        var host = HostMarkdownLintConfig(vault);

        // A nested config REPLACES the parent rather than merging with it, so a vault
        // config written without `extends` silently switches every rule the host repo
        // turned off back on (dogfood friction #19-6). The line is emitted only when
        // there is something to extend: `extends` naming a file that does not exist is
        // an error, which would break markdownlint for a repo that had no config at all.
        var extends = host is null
            ? string.Empty
            : $"""
                # A nested config REPLACES the repository-root one rather than merging with it,
                # so the root file is pulled in explicitly and exactly one extra rule is turned
                # off for the vault.
                extends: ../{host}


                """;

        return $"""
            # markdownlint configuration for the OKF vault only.

            {extends}# MD025 (single H1) is structurally incompatible with OKF v0.2 markdown, in
            # two independent ways:
            #
            #   1. §8 specifies an index.md as "one or more `#` sections". Every generated
            #      index with more than one concept type therefore carries several H1s,
            #      and those files are `okf index` output — not editable by hand.
            #   2. markdownlint counts a frontmatter `title:` as the document title, so
            #      ANY body heading at level 1 is already the second H1. OKF concepts
            #      carry their title in frontmatter and use `#` for body sections (§4.2's
            #      `# Schema` / `# Examples` convention), which makes the collision
            #      unavoidable rather than stylistic.
            #
            # Scoped to the vault so the rest of the repository keeps the rule.
            MD025: false

            """;
    }

    private static string? HostMarkdownLintConfig(string vault)
    {
        var host = Path.GetDirectoryName(vault);
        if (host is null)
        {
            return null;
        }

        return MarkdownLintConfigNames.FirstOrDefault(name => File.Exists(Path.Combine(host, name)));
    }

    private static string CustodianReadme(string name, IReadOnlyList<OkfSkillPointer> pointers) => $$"""
        # The {{name}} custodian

        This directory is the **custodian machinery** for the `{{name}}` bundle: the
        skills that maintain it, the recipe that configures them, and any check `okf
        lint` structurally cannot perform. It sits beside the bundle rather than inside
        it, and the bundler strips it on the way out, so a consumer receives readable
        markdown that runs nothing.

        Per decisions.md §1: *custodian machinery lives in the project repo beside the
        bundle, git-hook/CI triggered — but the shared toolset is referenced by version,
        never vendored per project.*

        ## The skills

        The custodian is not a program. It is prose skills, shipped inside the `okf`
        binary and **referenced, never copied here** — a vendored copy is a copy that
        drifts:

        - **okf-capture** — capture. Something was just learned and belongs in the
          bundle: search the bundle first, apply the capture-versus-cite test, drop what
          cannot defend itself into `raw/` with an entry in the capture manifest, write
          the concept and cite it by key.
        - **okf-custodian** — maintenance. A capture is waiting in `raw/`, or the bundle
          needs enrichment, index regeneration, lint clearing, or a `log.md` line.

        `recipe.json` names them as:

        {{SkillReadmePointers(pointers)}}

        Run `okf skills install` to put them on this machine — they ship inside the
        binary, so nothing is downloaded — and `okf skills path okf-capture` to print
        where one landed. A project that would rather keep its own copy can commit them
        under `skills/` (`okf skills install --dir skills`), which is the path a
        re-scaffolded recipe then points at.

        ## What runs where

        Fill this table in as the gates go up, and keep it honest: a directory that
        claims a robot it does not have is worse than one that claims nothing.

        | Trigger | What runs | What it does not do |
        | --- | --- | --- |
        | `pre-commit` hook | *(nothing yet)* | Keep `okf lint` out of it unless the wait is acceptable — it is a CI gate first. |
        | CI | `okf lint okf/`, `okf index --check okf/bundles/{{name}}` | Neither writes. Both report and exit. |
        | Enrichment | The skills above, invoked by a person opening a session | Nothing scheduled, and no agent watching `stale_after`. |

        ## Related

        - `../README.md` — what the vault is and how to work with it.
        - `../okf.json` — the lint severities and the tag registry, with the reasons.
        - `recipe.json` — the skills, the search seeds, and the exact commands.

        """;

    /// <summary>
    /// How the scaffolded recipe names each skill, resolved against the project the vault
    /// sits in: this repository's own <c>skills/</c> layout first, then a project-scoped
    /// host install, and otherwise the instruction form.
    /// </summary>
    /// <remarks>
    /// Only project-relative candidates are considered. A committed <c>recipe.json</c> is
    /// read on every machine that clones the project, so an absolute path — or a <c>~</c>
    /// one — would be a pointer that resolves for exactly the person who ran
    /// <c>okf init</c>.
    /// </remarks>
    private static IReadOnlyList<OkfSkillPointer> SkillPointers(string vault)
    {
        var projectRoot = Path.GetDirectoryName(vault);

        return
        [
            .. RecipeSkills.Select(skill =>
            {
                var relative = projectRoot is null
                    ? null
                    : OkfSkillInstaller.ProjectRelativeCandidates(skill.Name).FirstOrDefault(candidate =>
                        File.Exists(Path.Combine(projectRoot, candidate.Replace('/', Path.DirectorySeparatorChar))));

                return relative is null
                    ? new OkfSkillPointer(skill.Name, $"okf skills path {skill.Name}", IsPath: false)
                    : new OkfSkillPointer(skill.Name, relative, IsPath: true);
            }),
        ];
    }

    private static string SkillEntries(IReadOnlyList<OkfSkillPointer> pointers) =>
        string.Join(
            ",\n",
            pointers.Select(pointer => $$"""
                    {
                      "name": "{{pointer.Name}}",
                      "resource": "{{pointer.Resource}}",
                      "when": "{{RecipeSkills.Single(skill => skill.Name == pointer.Name).When}}"
                    }
                """.TrimEnd()));

    private static string CustodianRecipe(string name, IReadOnlyList<OkfSkillPointer> pointers) => $$"""
        {
          // {{name}}'s custodian recipe: what maintains this vault, and the exact
          // commands that maintenance runs. Read this file to know what the custodian
          // IS; read ../okf.json to know what the linter ENFORCES. They are separate
          // because the second ships as the team contract for anyone linting this
          // vault, while this one is producer-side machinery the bundler strips.
          //
          // Parsed as JSONC, like okf.json: comments are the reviewable half of a
          // configuration file. Nothing reads this file today — it is the recipe a
          // person or an agent reads before starting a pass.
          "recipeVersion": 1,
          "vault": "okf/",
          "bundle": "okf/bundles/{{name}}",

          // The skills, REFERENCED and never copied into this directory: a vendored
          // copy is a copy that drifts. A `resource` is a project-relative path when
          // this project has the skill on disk, and otherwise the instruction SPEC §5.1
          // allows in place of one — an absolute or ~ path would name one machine's
          // home directory in a file the whole team commits.
          //
          // `okf skills install` puts the skills on this machine (they ship inside the
          // okf binary), and `okf skills path <skill>` prints where one landed.
          "skills": [
        {{SkillEntries(pointers)}}
          ],

          // Search terms an enrichment pass starts from — this bundle's own subject
          // matter, one per domain. `okf search` is the retrieval step, so seeds are
          // queries rather than crawl URLs. Replace these with real ones.
          "seeds": [],

          // The exact commands, run from the repository root.
          "commands": {
            "search": "okf search \"<terms>\" okf/ --limit 10",
            "index": "okf index okf/bundles/{{name}}",
            "indexCheck": "okf index --check okf/bundles/{{name}}",
            "lint": "okf lint okf/"
          },

          // Where each command runs today. Honest scoping: nothing on this list is an
          // agent. The gates are mechanical; the enrichment they gate is invoked by a
          // person opening a session with the skills above.
          "triggers": {
            "pre-commit": [],
            "ci": [
              "lint",
              "indexCheck"
            ],
            "scheduled": []
          }
        }

        """;

    private static string SkillReadmePointers(IReadOnlyList<OkfSkillPointer> pointers) =>
        string.Join(
            "\n",
            pointers.Select(pointer => pointer.IsPath
                ? $"- `{pointer.Name}` \u2192 `{pointer.Resource}`, a path in this project."
                : $"- `{pointer.Name}` \u2192 `{pointer.Resource}` \u2014 an instruction, not a\n"
                    + "  path: this project keeps no copy of its own."));

    private static string RawGitignore() => """
        # Nothing in raw/ is ignored, ever.
        #
        # raw/ is the drop zone for captured artifacts and it is COMMITTED: the bundler
        # ships only bundles/, which is exactly what makes these originals a
        # producer-side archive. This repository keeps what was retrieved; a consumer
        # receives the concepts extracted from it plus the original URL. Ignoring any of
        # it would delete the only copy of the evidence the trust model rests on.
        #
        # The danger is not a deliberate rule but an inherited one. An ordinary
        # repository .gitignore carries patterns like `*.log`, `*.tmp`, `*.bak`, `tmp/`
        # or `*.pdf`, all of which are perfectly reasonable for build output and all of
        # which match real captured artifacts. A capture that git silently declines to
        # stage looks exactly like a capture that worked, right up until someone clones
        # the repository and the manifest points at nothing.
        #
        # These two lines re-include everything, directories first, because a file
        # cannot be re-included while a parent directory of it is still excluded.
        !*/
        !*

        """;


    private static string AboutThisBundle(string name, string actor, string stamp) => $$"""
        ---
        type: Guide
        title: About This Bundle
        description: What the {{name}} bundle covers, who maintains it, and how to consume it.
        tags: [{{string.Join(", ", SeedTags)}}]
        generated: { by: {{actor}}, at: {{stamp}} }
        ---

        This is the knowledge bundle for **{{name}}**. It was scaffolded by `okf init`
        and holds nothing yet — this concept is the only one in it. Replace this
        paragraph with what the bundle is *for*: the subject it covers, and the reader
        it is written for.

        # What is in here

        Nothing but this file, so far. As concepts arrive, group them into
        subdirectories by subject and give each subdirectory an `about.md` whose
        `description` is the blurb its parent index will show. `okf index` regenerates
        every `index.md` from the tree; those files are outputs and are never edited by
        hand.

        # Who maintains it

        Say so here, plainly, including what does *not* run. `../../custodian/` holds
        the recipe naming the skills that maintain this bundle and the table of what is
        triggered where. A custodian that overstates itself is worse than none: if
        nothing is scheduled and no agent watches `stale_after`, this is the paragraph
        that has to admit it.

        Every concept starts **unverified** — an actor cannot verify its own work, so a
        bundle sits at the lowest trust tier until a second actor confirms something in
        it. That is accuracy, not modesty.

        # How to consume it

        The bundle is plain markdown, so the floor is `git clone` and a text editor:
        start at `index.md` and follow the links. Nothing here requires executing
        anything.

        Beyond that:

        - **Progressive disclosure.** Every directory carries a generated `index.md`,
          so a reader — human or agent — can walk the tree top-down and open only the
          branch they need.
        - **The CLI.** `okf search <query>` returns ranked, links-first results with a
          trust tier and a stale flag on each hit; `okf lint` reports on the bundle
          without modifying it.
        - **MCP.** `okf mcp` runs a read-only server over stdio from the same binary,
          resolving vaults by exactly the CLI's rules.

        """;

    private static string BundleLog(string day) => $"""
        # Directory Update Log

        ## {day}

        * **Initialization**: Created the bundle with `okf init`. One concept,
          `about-this-bundle.md`, written by the tool and left unverified.

        """;
}
