namespace Okf.Core.Vault;

/// <summary>How a concept read ended (PRD CORE-12, MCP-5).</summary>
public enum OkfConceptStatus
{
    /// <summary>The concept was read.</summary>
    Ok,

    /// <summary>
    /// The identifier is not a bundle-relative path: it is empty, absolute, carries a
    /// <c>..</c> segment, or is otherwise malformed. A caller's own bug, not a miss.
    /// </summary>
    InvalidPath,

    /// <summary>The path resolves outside the bundle root; okf-net never serves it (MCP-5).</summary>
    Outside,

    /// <summary>The path is well formed and inside the bundle, but no such file exists.</summary>
    NotFound,

    /// <summary>The file exists but its frontmatter does not parse (an <c>OKF0001</c> error).</summary>
    Unparseable,
}

/// <summary>
/// One concept, read whole: its frontmatter, its body, and the two derived judgements a
/// consumer needs before trusting it — trust tier (§5.3) and staleness (§5.5). This is the
/// read side of progressive disclosure (PRD CORE-12): search points at a concept, this
/// opens it.
/// </summary>
public sealed class OkfConcept
{
    /// <summary>Initializes a concept.</summary>
    /// <param name="bundle">The bundle the concept belongs to.</param>
    /// <param name="path">The concept's absolute path.</param>
    /// <param name="document">The parsed document.</param>
    /// <param name="today">The date staleness is judged against.</param>
    public OkfConcept(OkfBundle bundle, string path, OkfDocument document, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(document);

        Bundle = bundle;
        AbsolutePath = path;
        Path = bundle.RelativePath(path);
        Id = Path.EndsWith(".md", StringComparison.Ordinal) ? Path[..^3] : Path;
        Frontmatter = document.Frontmatter;
        Body = document.Body;
        Type = FrontmatterValues.Scalar(document.Frontmatter, "type");
        Description = FrontmatterValues.Scalar(document.Frontmatter, "description");
        Tags = FrontmatterValues.Tags(document.Frontmatter);
        Title = FrontmatterValues.Scalar(document.Frontmatter, "title") is { Length: > 0 } title
            ? title
            : System.IO.Path.GetFileNameWithoutExtension(path);
        TrustTier = OkfDocument.TrustTier(document.Frontmatter);
        Stale = OkfDocument.IsStale(document.Frontmatter, today);
    }

    /// <summary>The bundle the concept belongs to.</summary>
    public OkfBundle Bundle { get; }

    /// <summary>The concept id: the bundle-relative path minus <c>.md</c> (spec §2).</summary>
    public string Id { get; }

    /// <summary>The bundle-relative path, with <c>/</c> separators.</summary>
    public string Path { get; }

    /// <summary>The concept's absolute path.</summary>
    public string AbsolutePath { get; }

    /// <summary>The frontmatter, structure-preserving and in source order (PRD CORE-2).</summary>
    public OkfMapping Frontmatter { get; }

    /// <summary>The markdown body.</summary>
    public string Body { get; }

    /// <summary>The <c>title</c>, falling back to the filename stem.</summary>
    public string Title { get; }

    /// <summary>The <c>type</c> (§4.1), or <see langword="null" /> when absent or falsy.</summary>
    public string? Type { get; }

    /// <summary>The <c>description</c> (§4.1), or <see langword="null" /> when absent.</summary>
    public string? Description { get; }

    /// <summary>The <c>tags</c> (§4.1); a scalar <c>tags</c> reads as one tag.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The derived trust tier (§5.3, PRD CORE-6).</summary>
    public OkfTrustTier TrustTier { get; }

    /// <summary>Whether the concept is stale (§5.5, PRD CORE-7).</summary>
    public bool Stale { get; }

    /// <inheritdoc />
    public override string ToString() => Path;
}

/// <summary>The outcome of a concept read: the concept, or why there is none.</summary>
public sealed class OkfConceptResult
{
    private OkfConceptResult(OkfConceptStatus status, OkfConcept? concept, string message)
    {
        Status = status;
        Concept = concept;
        Message = message;
    }

    /// <summary>How the read ended.</summary>
    public OkfConceptStatus Status { get; }

    /// <summary>The concept, present exactly when <see cref="Status" /> is <see cref="OkfConceptStatus.Ok" />.</summary>
    public OkfConcept? Concept { get; }

    /// <summary>An explanation for the caller to surface; empty on success.</summary>
    public string Message { get; }

    /// <summary>Whether the read succeeded.</summary>
    public bool IsOk => Status == OkfConceptStatus.Ok;

    /// <summary>Builds a successful result.</summary>
    /// <param name="concept">The concept read.</param>
    /// <returns>The result.</returns>
    public static OkfConceptResult Ok(OkfConcept concept) =>
        new(OkfConceptStatus.Ok, concept, string.Empty);

    /// <summary>Builds a failed result.</summary>
    /// <param name="status">Why the read failed.</param>
    /// <param name="message">The explanation.</param>
    /// <returns>The result.</returns>
    public static OkfConceptResult Fail(OkfConceptStatus status, string message) =>
        new(status, null, message);
}

/// <summary>Where a concept read gets its inputs, so tests need no filesystem.</summary>
public sealed class OkfConceptOptions
{
    /// <summary>
    /// The date staleness is judged against. Injected rather than read from the clock, so a
    /// read is deterministic (PRD CORE-7).
    /// </summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Supplies a file's text. Returning <see langword="null" /> falls back to reading the
    /// file.
    /// </summary>
    public Func<string, string?>? ReadText { get; set; }
}

/// <summary>
/// Reads one concept out of a bundle by its bundle-relative path (PRD CORE-12). Every path
/// is confined to the bundle root: <c>..</c> traversal and absolute paths are rejected
/// before the filesystem is touched (PRD MCP-5).
/// </summary>
public static class OkfConceptReader
{
    /// <summary>Reads one concept.</summary>
    /// <param name="bundle">The bundle to read from.</param>
    /// <param name="id">
    /// The concept id or bundle-relative path. The <c>.md</c> suffix is optional, so both a
    /// search result's <c>id</c> and its <c>path</c> work.
    /// </param>
    /// <param name="options">The staleness date and where to read text from.</param>
    /// <returns>The concept, or why there is none.</returns>
    /// <exception cref="IOException">The file exists but could not be read.</exception>
    public static OkfConceptResult Read(OkfBundle bundle, string? id, OkfConceptOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        OkfConceptOptions resolvedOptions = options ?? new OkfConceptOptions();

        if (Normalize(id) is not { } relative)
        {
            return InvalidPath(id);
        }

        if (!bundle.TryResolve(relative, out string? path))
        {
            return Outside(bundle, relative);
        }

        return ReadResolvedConcept(bundle, relative, path, resolvedOptions);
    }

    /// <summary>
    /// Normalizes a concept id into a bundle-relative markdown path, or
    /// <see langword="null" /> when the id is not one.
    /// </summary>
    /// <param name="id">The id or path.</param>
    /// <returns>The relative path ending in <c>.md</c>, or <see langword="null" />.</returns>
    /// <remarks>
    /// <para>A backslash is rejected rather than treated as a separator: bundle-relative
    /// paths use <c>/</c> everywhere (that is what <see cref="OkfBundle.RelativePath" />
    /// emits), and silently reinterpreting a separator is how containment checks get bypassed
    /// on one platform and not another.</para>
    /// <para>A NUL is rejected for a blunter reason: no filesystem accepts one in a name, and
    /// <see cref="Path.GetFullPath(string)" /> throws on one rather than answering. A caller
    /// that can be handed arbitrary text — <c>okf mcp</c> can — must get an answer, not an
    /// exception.</para>
    /// </remarks>
    public static string? Normalize(string? id)
    {
        string trimmed = TrimmedId(id) ?? string.Empty;
        if (!IsRelativeId(trimmed))
        {
            return null;
        }

        List<string> segments = NormalizeSegments(trimmed);
        if (segments.Count == 0)
        {
            return null;
        }

        return EnsureMarkdownSuffix(string.Join('/', segments));
    }

    private static OkfConceptResult InvalidPath(string? id) =>
        OkfConceptResult.Fail(
            OkfConceptStatus.InvalidPath,
            $"'{id}' is not a bundle-relative path. Give a path like 'topics/widgets.md', " +
            "without a leading '/' and without '..' segments.");

    private static OkfConceptResult Outside(OkfBundle bundle, string relative) =>
        OkfConceptResult.Fail(
            OkfConceptStatus.Outside,
            $"'{relative}' resolves outside bundle '{bundle.Name}'. okf serves only paths inside a bundle root.");

    private static OkfConceptResult ReadResolvedConcept(
        OkfBundle bundle,
        string relative,
        string path,
        OkfConceptOptions options)
    {
        ResolvedConceptPath conceptPath = new(bundle, relative, path);
        string? text = LoadText(conceptPath.Path, options.ReadText);
        if (text is null)
        {
            return NotFound(conceptPath);
        }

        return ParseConcept(conceptPath, text, options.Today);
    }

    private static string? LoadText(string path, Func<string, string?>? readText)
    {
        string? text = readText?.Invoke(path);
        if (text is not null)
        {
            return text;
        }

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static OkfConceptResult NotFound(ResolvedConceptPath conceptPath) =>
        OkfConceptResult.Fail(
            OkfConceptStatus.NotFound,
            $"No concept '{conceptPath.Relative}' in bundle '{conceptPath.Bundle.Name}'. Search for it, or list the directory.");

    private static OkfConceptResult ParseConcept(ResolvedConceptPath conceptPath, string text, DateOnly today)
    {
        OkfDocument document;
        try
        {
            document = OkfDocument.Parse(text);
        }
        catch (OkfDocumentException exception)
        {
            return Unparseable(conceptPath, exception);
        }

        return OkfConceptResult.Ok(new OkfConcept(conceptPath.Bundle, conceptPath.Path, document, today));
    }

    private static OkfConceptResult Unparseable(ResolvedConceptPath conceptPath, OkfDocumentException exception) =>
        OkfConceptResult.Fail(
            OkfConceptStatus.Unparseable,
            $"'{conceptPath.Relative}' in bundle '{conceptPath.Bundle.Name}' has frontmatter that does not parse " +
            $"({exception.Message}). Run `okf lint` on the bundle.");

    private static string? TrimmedId(string? id) => id?.Trim();

    private static bool IsRelativeId(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        return !trimmed.StartsWith('/')
            && !trimmed.StartsWith('~')
            && !Path.IsPathRooted(trimmed);
    }

    private static List<string> NormalizeSegments(string trimmed)
    {
        List<string> segments = new List<string>();
        foreach (string segment in trimmed.Split('/'))
        {
            if (IgnoresSegment(segment))
            {
                continue;
            }

            if (IsParentTraversal(segment))
            {
                return [];
            }

            segments.Add(segment);
        }

        return segments;
    }

    private static bool IgnoresSegment(string segment) =>
        segment.Length == 0 || string.Equals(segment, ".", StringComparison.Ordinal);

    private static bool IsParentTraversal(string segment) =>
        string.Equals(segment, "..", StringComparison.Ordinal);

    private static string EnsureMarkdownSuffix(string relative) =>
        relative.EndsWith(".md", StringComparison.Ordinal) ? relative : relative + ".md";

    private sealed record ResolvedConceptPath(OkfBundle Bundle, string Relative, string Path);
}
