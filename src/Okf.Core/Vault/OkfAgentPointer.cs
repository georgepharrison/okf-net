using System.Text;

namespace Okf.Core.Vault;

/// <summary>What one agent-pointer file write did.</summary>
public enum OkfAgentPointerStatus
{
    /// <summary>The file did not exist and was written.</summary>
    Created = 0,

    /// <summary>The file existed and the fenced block inside it changed — spliced in, or appended.</summary>
    Updated,

    /// <summary>The file already carried exactly this fenced block; nothing was written.</summary>
    Unchanged,

    /// <summary>The file already existed and was left exactly as found (<c>CLAUDE.md</c> only).</summary>
    Skipped,

    /// <summary>
    /// The file carried more than one fence and was left exactly as found: a write could
    /// only bring one of them current, and the others would go on saying something stale in
    /// the agent's context on every turn.
    /// </summary>
    Refused,
}

/// <summary>One file an agent-pointer write considered, and what became of it.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Status">What the write did.</param>
public sealed record OkfAgentPointerFile(string Path, OkfAgentPointerStatus Status);

/// <summary>
/// Writes the context pointer that tells an agent a project keeps knowledge in an OKF
/// vault: a marker-fenced block in <c>AGENTS.md</c>, plus a one-line <c>CLAUDE.md</c> that
/// points at it. Both are written at the <em>project</em> root — the vault's parent
/// (<see cref="OkfDiscovery" />, <see cref="OkfScaffold" />) — not inside <c>okf/</c>, which
/// is why writing them is opt-out rather than assumed by every caller (work item #56;
/// decisions.md, AD-2 amendment).
/// </summary>
/// <remarks>
/// <para><b>The block is a constant, not an embedded resource.</b> The three agent skills
/// are embedded (AD-50) because they are whole documents a person edits, lints, and reads on
/// their own; this block is a seven-line fragment whose exact bytes are load-bearing (the fence
/// contract), and it is templated exactly the way every other scaffolded file in
/// <see cref="OkfScaffold" /> is — a C# string, reviewed as code. Nothing about it benefits
/// from a second file to keep in sync.</para>
/// <para><b>It never touches a byte outside the fence.</b> <c>AGENTS.md</c> is created with
/// the block when the file is absent; when it is present with no fence, the block is
/// appended after one blank line; when exactly one fence is present, only the text from the
/// opening marker line to the closing marker line, inclusive, is replaced — byte for byte,
/// and only when it differs from what is already there. Everything else in the file — its
/// own text, its newline convention — survives untouched, the same discipline
/// <see cref="OkfStamp" /> uses for a concept's frontmatter. A file carrying more than one
/// fence is left exactly as found and reported <see cref="OkfAgentPointerStatus.Refused" />:
/// bringing one of them current would leave the rest saying something stale, and this writer
/// deletes nothing a run of it did not write.</para>
/// <para><b><c>CLAUDE.md</c> is never rewritten.</b> It is created, in this repository's own
/// one-line form, only when no <c>CLAUDE.md</c> exists at all — a project that already has
/// one owns it, and okf-net has nothing to say about its content.</para>
/// </remarks>
public static class OkfAgentPointer
{
    /// <summary>The file the fenced block is written into.</summary>
    public const string AgentsFileName = "AGENTS.md";

    /// <summary>The file created, only when absent, to point a CLAUDE-reading host at <see cref="AgentsFileName" />.</summary>
    public const string ClaudeFileName = "CLAUDE.md";

    /// <summary>The line that opens the machine-maintained region.</summary>
    public const string BeginMarker = "<!-- okf:begin -->";

    /// <summary>The line that closes the machine-maintained region.</summary>
    public const string EndMarker = "<!-- okf:end -->";

    /// <summary>
    /// The canonical block, markers included, <c>\n</c>-joined. Each writer below renders it
    /// in the target file's own newline convention rather than always this one.
    /// </summary>
    public const string Block = """
        <!-- okf:begin -->
        This project keeps its knowledge in `okf/`, an OKF vault.

        - **Read**: before answering or deciding from memory — reach for `okf-vault`, or `okf search` when it is not installed.
        - **Capture**: when the session produced something durable the code cannot re-derive — reach for `okf-capture`.
        - **Maintain**: when `okf inbox` is non-empty — reach for `okf-custodian`.
        <!-- okf:end -->
        """;

    /// <summary>This repository's own one-liner, and the exact bytes a fresh <c>CLAUDE.md</c> gets.</summary>
    public const string ClaudeMdContent = "# Agent Instructions\n\nRead [AGENTS.md](AGENTS.md) and follow it.\n";

    /// <summary>Writes both files at a project root.</summary>
    /// <param name="projectRoot">
    /// The project root — the vault's parent, exactly as <see cref="OkfScaffold" /> and
    /// <see cref="OkfDiscovery" /> mean it, or a project's working directory for a
    /// project-scoped skills install.
    /// </param>
    /// <returns><c>AGENTS.md</c>'s result, then <c>CLAUDE.md</c>'s — the order they are reported in.</returns>
    /// <exception cref="IOException">A file could not be written.</exception>
    public static IReadOnlyList<OkfAgentPointerFile> Write(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);
        return [WriteAgentsMd(projectRoot), WriteClaudeMd(projectRoot)];
    }

    /// <summary>
    /// Writes or splices the fenced block into <c>AGENTS.md</c> at a project root.
    /// </summary>
    /// <param name="projectRoot">The project root the file sits in.</param>
    /// <returns>The file's path and what happened to it.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static OkfAgentPointerFile WriteAgentsMd(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);
        var path = Path.Combine(projectRoot, AgentsFileName);

        if (!File.Exists(path))
        {
            FileText.WriteAtomic(path, Block + "\n");
            return new OkfAgentPointerFile(path, OkfAgentPointerStatus.Created);
        }

        var original = File.ReadAllText(path);
        var lines = SplitLines(original);
        var newline = DetectNewline(lines);
        var fences = FindFences(lines);

        if (fences.Count > 1)
        {
            return new OkfAgentPointerFile(path, OkfAgentPointerStatus.Refused);
        }

        if (fences.Count == 1)
        {
            var found = fences[0];
            var canonical = Block.Split('\n');
            var existing = lines.Skip(found.Begin).Take(found.End - found.Begin + 1).Select(line => line.Content);
            if (existing.SequenceEqual(canonical, StringComparer.Ordinal))
            {
                return new OkfAgentPointerFile(path, OkfAgentPointerStatus.Unchanged);
            }

            var spliced = Splice(lines, found.Begin, found.End, canonical, newline);
            FileText.WriteAtomic(path, spliced);
            return new OkfAgentPointerFile(path, OkfAgentPointerStatus.Updated);
        }

        // No complete fence: append after exactly one blank line, however the existing file
        // ends — trailing blank lines are trimmed first so "one blank line" is not "one plus
        // however many the file already had".
        var trimmed = original.TrimEnd('\r', '\n');
        var appended = trimmed + newline + newline + Block.Replace("\n", newline, StringComparison.Ordinal) + newline;
        FileText.WriteAtomic(path, appended);
        return new OkfAgentPointerFile(path, OkfAgentPointerStatus.Updated);
    }

    /// <summary>
    /// Creates <c>CLAUDE.md</c> at a project root in this repository's own one-line form,
    /// only when none exists. A project that already has one owns it.
    /// </summary>
    /// <param name="projectRoot">The project root the file sits in.</param>
    /// <returns>The file's path and what happened to it.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static OkfAgentPointerFile WriteClaudeMd(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectRoot);
        var path = Path.Combine(projectRoot, ClaudeFileName);

        if (File.Exists(path))
        {
            return new OkfAgentPointerFile(path, OkfAgentPointerStatus.Skipped);
        }

        FileText.WriteAtomic(path, ClaudeMdContent);
        return new OkfAgentPointerFile(path, OkfAgentPointerStatus.Created);
    }

    /// <summary>One line of a split file: its content, and the exact terminator that followed it.</summary>
    /// <param name="Content">The line's text, without its terminator.</param>
    /// <param name="Terminator"><c>"\r\n"</c>, <c>"\n"</c>, or <c>""</c> for a final line with none.</param>
    private readonly record struct Line(string Content, string Terminator);

    /// <summary>
    /// Splits text into lines carrying their own terminators, so joining
    /// <c>Content + Terminator</c> back together reproduces the input byte for byte. That
    /// round-trip property is what lets <see cref="Splice" /> touch only the lines it means
    /// to and leave everything else — including a file's CRLF endings — exactly as found.
    /// </summary>
    private static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            lines.Add(i > start && text[i - 1] == '\r'
                ? new Line(text[start..(i - 1)], "\r\n")
                : new Line(text[start..i], "\n"));
            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(new Line(text[start..], string.Empty));
        }

        return lines;
    }

    /// <summary>
    /// The file's newline convention: CRLF when any line was terminated that way, else LF —
    /// the style a uniformly-formatted file (the only kind okf-net ever writes) always
    /// carries throughout.
    /// </summary>
    private static string DetectNewline(IReadOnlyList<Line> lines) =>
        lines.Any(line => line.Terminator == "\r\n") ? "\r\n" : "\n";

    /// <summary>
    /// Finds every complete fence, in file order: a line equal to <see cref="BeginMarker" />,
    /// trimmed, closed by the next line equal to <see cref="EndMarker" />, trimmed, at or
    /// after it. Scanning resumes past each close, so nested begins are swallowed by the
    /// fence that closes first; a begin with no matching end (or an end before any begin) is
    /// not a fence, and a file with none is treated as unfenced. The caller needs the count,
    /// not just the first: a second fence is the one case this writer refuses.
    /// </summary>
    private static List<(int Begin, int End)> FindFences(IReadOnlyList<Line> lines)
    {
        var fences = new List<(int Begin, int End)>();
        var begin = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var content = lines[i].Content.Trim();
            if (begin < 0)
            {
                if (content == BeginMarker)
                {
                    begin = i;
                }
            }
            else if (content == EndMarker)
            {
                fences.Add((begin, i));
                begin = -1;
            }
        }

        return fences;
    }

    /// <summary>
    /// Replaces lines <paramref name="begin" />..<paramref name="end" /> (inclusive) with
    /// <paramref name="replacement" />, rendered in <paramref name="newline" />, and rejoins
    /// every other line with the exact terminator it already carried — the splice touches
    /// nothing outside the range it was given.
    /// </summary>
    private static string Splice(
        IReadOnlyList<Line> lines,
        int begin,
        int end,
        string[] replacement,
        string newline)
    {
        // The replaced region's last line keeps the terminator the line it replaces had —
        // typically the file's own convention, but preserved exactly even for the edge case
        // of a fence closing at end of file with no trailing newline at all.
        var lastTerminator = lines[end].Terminator;

        var builder = new StringBuilder();
        for (var i = 0; i < begin; i++)
        {
            builder.Append(lines[i].Content).Append(lines[i].Terminator);
        }

        for (var i = 0; i < replacement.Length; i++)
        {
            builder.Append(replacement[i]).Append(i == replacement.Length - 1 ? lastTerminator : newline);
        }

        for (var i = end + 1; i < lines.Count; i++)
        {
            builder.Append(lines[i].Content).Append(lines[i].Terminator);
        }

        return builder.ToString();
    }
}
