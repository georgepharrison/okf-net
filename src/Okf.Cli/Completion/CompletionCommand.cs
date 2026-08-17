using System.Linq;
using System.Text;

namespace Okf.Cli.Completion;

/// <summary>
/// <c>okf completion bash|zsh|fish|pwsh</c> — prints the completion script for one shell,
/// rendered from <see cref="CompletionTable" /> (issue #51).
/// </summary>
/// <remarks>
/// The scripts are generated rather than kept as four hand-written files, because four
/// hand-written files drift from the verb surface the first week nobody remembers to edit
/// them. They are hand-rolled templates with no dependency (AD-9), pure ASCII, and byte-
/// stable for a given table, which is what makes a golden test possible.
/// <para>
/// Nothing in a generated script reads a vault. The only command a script runs is
/// <c>okf skills list --names</c>, which answers from the binary itself; paths are the
/// shell's own file completion. A completion that walked bundles would put a directory scan
/// on the tab key.
/// </para>
/// </remarks>
internal static class CompletionCommand
{
    /// <summary>Every option <see cref="Run" /> accepts (see <see cref="InitArguments.Flags" />).</summary>
    public static readonly string[] Flags = ["--help", "-h"];

    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>completion</c>.</param>
    /// <param name="output">Where the script goes.</param>
    /// <param name="error">Where usage failures go.</param>
    /// <returns>The process exit code: 0 with a script on stdout, 2 for usage.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? shell = null;
        foreach (string argument in args)
        {
            switch (argument)
            {
                case "--help" or "-h":
                    WriteUsage(output);
                    return CliApplication.ExitSuccess;

                default:
                    if (TakeShell(argument, ref shell) is { } refusal)
                    {
                        return Refuse(error, refusal);
                    }

                    break;
            }
        }

        return shell is null
            ? Refuse(error, $"`okf completion` needs a shell: {Shells}.")
            : WriteScript(shell, output, error);
    }

    private static string? TakeShell(string argument, ref string? shell)
    {
        if (argument.StartsWith('-') && argument.Length > 1)
        {
            return $"Unknown option '{argument}'.";
        }

        if (shell is not null)
        {
            return $"`okf completion` takes one shell; got '{shell}' and '{argument}'.";
        }

        shell = argument;
        return null;
    }

    private static int WriteScript(string shell, TextWriter output, TextWriter error)
    {
        if (Render(shell) is not { } script)
        {
            return Refuse(error, $"Unknown shell '{shell}'; expected {Shells}.");
        }

        output.Write(script);
        return CliApplication.ExitSuccess;
    }

    /// <summary>Renders one shell's script.</summary>
    /// <param name="shell">The shell's name.</param>
    /// <returns>The script, or <see langword="null" /> when the shell is not one of the four.</returns>
    public static string? Render(string shell) => shell switch
    {
        "bash" => Bash(),
        "zsh" => Zsh(),
        "fish" => Fish(),
        "pwsh" or "powershell" => PowerShell(),
        _ => null,
    };

    private static string Shells => string.Join(", ", CompletionTable.Shells);

    private static int Refuse(TextWriter error, string message)
    {
        error.WriteLine($"okf: error: {message}");
        error.WriteLine("Run `okf completion --help` for usage.");
        return CliApplication.ExitUsage;
    }

    private static void WriteUsage(TextWriter writer) =>
        writer.WriteLine("""
            okf completion <bash|zsh|fish|pwsh>

            Prints a completion script for one shell on stdout. It is generated from okf's
            own verb table, so it lists exactly the verbs and options this binary has.

            Load it into the current shell:
              bash    eval "$(okf completion bash)"
              zsh     eval "$(okf completion zsh)"
              fish    okf completion fish | source
              pwsh    okf completion pwsh | Out-String | Invoke-Expression

            Install it for every new shell:
              bash    okf completion bash > ${XDG_DATA_HOME:-~/.local/share}/bash-completion/completions/okf
              zsh     okf completion zsh > ${XDG_DATA_HOME:-~/.local/share}/zsh/site-functions/_okf
              fish    okf completion fish > ${XDG_CONFIG_HOME:-~/.config}/fish/completions/okf.fish
              pwsh    add the `Invoke-Expression` line above to your $PROFILE

            install.sh and install.ps1 do that for the shell they detect, unless
            OKF_SKIP_COMPLETIONS=1 is set.

            Exit codes:
              0  the script is on stdout
              2  no shell, an unknown shell, or usage
            """);

    /// <summary>The flags that consume the next word, across every verb.</summary>
    private static IReadOnlyList<string> ValueFlags { get; } =
        [.. CompletionTable.Verbs
            .SelectMany(verb => verb.Flags)
            .Where(flag => flag.Value.TakesValue)
            .Select(flag => flag.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)];

    private static string Words(IEnumerable<string> words) => string.Join(" ", words);

    private static string FlagWords(CompletionVerb verb) => Words(verb.Flags.Select(flag => flag.Name));

    /// <summary>
    /// One verb's value-taking flags, grouped by what they accept and kept in table order,
    /// so the generated `case` arms are the same bytes on every run.
    /// </summary>
    private static IEnumerable<(CompletionValue Value, IReadOnlyList<string> Flags)> ValueGroups(CompletionVerb verb) =>
        verb.Flags
            .Where(flag => flag.Value.TakesValue)
            .GroupBy(flag => flag.Value.Kind, StringComparer.Ordinal)
            .Select(group => (group.First().Value, (IReadOnlyList<string>)[.. group.Select(flag => flag.Name)]));

    /// <summary>Whether the operand is offered after this subcommand.</summary>
    private static string OperandCondition(CompletionVerb verb, string variable) =>
        verb.OperandSubcommands.Count == 0
            ? string.Empty
            : string.Join(" -o ", verb.OperandSubcommands.Select(name => $"\"{variable}\" = \"{name}\""));

    private static void Line(StringBuilder script, string text) => script.Append(text).Append('\n');

    private static string Bash()
    {
        StringBuilder script = new StringBuilder();
        WriteBashPreamble(script);
        WriteBashHelpers(script);
        WriteBashScan(script);
        WriteBashVerbs(script);
        Line(script, "    esac");
        Line(script, "}");
        Line(script, "");
        Line(script, "complete -F _okf okf");
        return script.ToString();
    }

    private static void WriteBashPreamble(StringBuilder script)
    {
        Line(script, "# okf completion for bash. Generated by `okf completion bash`; do not edit.");
        Line(script, "#");
        Line(script, "# Load it into this shell:  eval \"$(okf completion bash)\"");
        Line(script, "# Install it permanently:   okf completion bash > \\");
        Line(script, "#     \"${XDG_DATA_HOME:-$HOME/.local/share}/bash-completion/completions/okf\"");
        Line(script, "");
    }

    private static void WriteBashHelpers(StringBuilder script)
    {
        Line(script, "__okf_words() {");
        Line(script, "    COMPREPLY=( $(compgen -W \"$1\" -- \"$2\") )");
        Line(script, "}");
        Line(script, "");
        Line(script, "__okf_files() {");
        Line(script, "    # A path may contain spaces, and the default IFS would split one such name");
        Line(script, "    # into several candidates. `local IFS` keeps this to the one function and");
        Line(script, "    # stays inside bash 3.2, which is what macOS still ships.");
        Line(script, "    local IFS=$'\\n'");
        Line(script, "    COMPREPLY=( $(compgen -f -- \"$1\") )");
        Line(script, "    compopt -o filenames 2>/dev/null");
        Line(script, "}");
        Line(script, "");
        Line(script, "# The only thing a completion asks okf for. It answers from the binary itself, so");
        Line(script, "# there is no vault to find and no directory to walk.");
        Line(script, "__okf_skills() {");
        Line(script, "    COMPREPLY=( $(compgen -W \"$(\"$2\" skills list --names 2>/dev/null)\" -- \"$1\") )");
        Line(script, "}");
        Line(script, "");
    }

    private static void WriteBashScan(StringBuilder script)
    {
        WriteBashLocals(script);
        WriteBashVerbWalk(script);
        WriteBashEmptyVerb(script);
        Line(script, "    case \"$verb\" in");
    }

    private static void WriteBashLocals(StringBuilder script)
    {
        Line(script, "_okf() {");
        Line(script, "    local cur prev verb sub word skip index");
        Line(script, "");
        Line(script, "    # bash does not clear this between completions; a function that leaves it");
        Line(script, "    # alone offers the previous word's answers.");
        Line(script, "    COMPREPLY=()");
        Line(script, "    cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        Line(script, "    prev=\"\"");
        Line(script, "    if [ \"$COMP_CWORD\" -gt 0 ]; then");
        Line(script, "        prev=\"${COMP_WORDS[COMP_CWORD-1]}\"");
        Line(script, "    fi");
        Line(script, "");
    }

    private static void WriteBashVerbWalk(StringBuilder script)
    {
        Line(script, "    # The verb and its subcommand are the first two bare words that are not some");
        Line(script, "    # flag's value, which is why the value-taking flags are listed here.");
        Line(script, "    verb=\"\"");
        Line(script, "    sub=\"\"");
        Line(script, "    skip=0");
        Line(script, "    index=1");
        Line(script, "    while [ \"$index\" -lt \"$COMP_CWORD\" ]; do");
        Line(script, "        word=\"${COMP_WORDS[index]}\"");
        Line(script, "        index=$((index + 1))");
        Line(script, "        if [ \"$skip\" = 1 ]; then");
        Line(script, "            skip=0");
        Line(script, "            continue");
        Line(script, "        fi");
        Line(script, "        case \"$word\" in");
        Line(script, "            --*=*) ;;");
        Line(script, $"            {string.Join("|", ValueFlags)}) skip=1 ;;");
        Line(script, "            -*) ;;");
        Line(script, "            *)");
        Line(script, "                if [ -z \"$verb\" ]; then");
        Line(script, "                    verb=\"$word\"");
        Line(script, "                elif [ -z \"$sub\" ]; then");
        Line(script, "                    sub=\"$word\"");
        Line(script, "                fi");
        Line(script, "                ;;");
        Line(script, "        esac");
        Line(script, "    done");
        Line(script, "");
    }

    private static void WriteBashEmptyVerb(StringBuilder script)
    {
        Line(script, "    if [ -z \"$verb\" ]; then");
        Line(script, $"        __okf_words \"{Words(CompletionTable.Names)} --help -h --version\" \"$cur\"");
        Line(script, "        return");
        Line(script, "    fi");
        Line(script, "");
    }

    private static void WriteBashVerbs(StringBuilder script)
    {
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            WriteBashVerb(script, verb);
        }
    }

    private static void WriteBashVerb(StringBuilder script, CompletionVerb verb)
    {
        Line(script, $"        {verb.Name})");
        WriteBashValueGroups(script, verb);
        WriteBashFlags(script, verb);
        WriteBashSubcommands(script, verb);
        WriteBashOperand(script, verb);
        Line(script, "            ;;");
    }

    private static void WriteBashValueGroups(StringBuilder script, CompletionVerb verb)
    {
        List<(CompletionValue Value, IReadOnlyList<string> Flags)> groups = ValueGroups(verb).ToList();
        if (groups.Count == 0)
        {
            return;
        }

        Line(script, "            case \"$prev\" in");
        foreach ((CompletionValue value, IReadOnlyList<string> flags) in groups)
        {
            string pattern = string.Join("|", flags);
            Line(script, value.Kind switch
            {
                "path" => $"                {pattern}) __okf_files \"$cur\"; return ;;",
                "skill" => $"                {pattern}) __okf_skills \"$cur\" \"${{COMP_WORDS[0]}}\"; return ;;",
                "text" => $"                {pattern}) return ;;",
                _ => $"                {pattern}) __okf_words \"{Words(value.Candidates)}\" \"$cur\"; return ;;",
            });
        }

        Line(script, "            esac");
    }

    private static void WriteBashFlags(StringBuilder script, CompletionVerb verb)
    {
        if (verb.Flags.Count == 0)
        {
            return;
        }

        Line(script, "            case \"$cur\" in");
        Line(script, $"                -*) __okf_words \"{FlagWords(verb)}\" \"$cur\"; return ;;");
        Line(script, "            esac");
    }

    private static void WriteBashSubcommands(StringBuilder script, CompletionVerb verb)
    {
        if (verb.Subcommands.Count == 0)
        {
            return;
        }

        Line(script, "            if [ -z \"$sub\" ]; then");
        Line(script, $"                __okf_words \"{Words(verb.Subcommands)}\" \"$cur\"");
        Line(script, "                return");
        Line(script, "            fi");
    }

    private static void WriteBashOperand(StringBuilder script, CompletionVerb verb)
    {
        string guard = OperandCondition(verb, "$sub");
        string indent = guard.Length > 0 ? "                " : "            ";
        if (guard.Length > 0)
        {
            Line(script, $"            if [ {guard} ]; then");
        }

        switch (verb.Operand.Kind)
        {
            case "path":
                Line(script, $"{indent}__okf_files \"$cur\"");
                break;

            case "skill":
                Line(script, $"{indent}__okf_skills \"$cur\" \"${{COMP_WORDS[0]}}\"");
                break;

            case "none" or "text":
                break;

            default:
                Line(script, $"{indent}__okf_words \"{Words(verb.Operand.Candidates)}\" \"$cur\"");
                break;
        }

        if (guard.Length > 0)
        {
            Line(script, "            fi");
        }
    }

    private static string Zsh()
    {
        StringBuilder script = new StringBuilder();
        WriteZshPreamble(script);
        WriteZshScan(script);
        WriteZshVerbs(script);
        Line(script, "    esac");
        Line(script, "}");
        Line(script, "");
        Line(script, "# Both entry points: sourced (`eval \"$(okf completion zsh)\"`) the function has to");
        Line(script, "# register itself, autoloaded from $fpath it is already the completer being called.");
        Line(script, "if [ \"$funcstack[1]\" = \"_okf\" ]; then");
        Line(script, "    _okf \"$@\"");
        Line(script, "else");
        Line(script, "    compdef _okf okf");
        Line(script, "fi");
        return script.ToString();
    }

    private static void WriteZshPreamble(StringBuilder script)
    {
        Line(script, "#compdef okf");
        Line(script, "# okf completion for zsh. Generated by `okf completion zsh`; do not edit.");
        Line(script, "#");
        Line(script, "# Load it into this shell:  eval \"$(okf completion zsh)\"");
        Line(script, "# Install it permanently:   okf completion zsh > \\");
        Line(script, "#     \"${XDG_DATA_HOME:-$HOME/.local/share}/zsh/site-functions/_okf\"");
        Line(script, "# (that directory has to be on $fpath before compinit runs)");
        Line(script, "");
        Line(script, "__okf_skill_names() {");
        Line(script, "    local -a names");
        Line(script, "    names=(${(f)\"$(okf skills list --names 2>/dev/null)\"})");
        Line(script, "    compadd -a names");
        Line(script, "}");
        Line(script, "");
    }

    private static void WriteZshScan(StringBuilder script)
    {
        WriteZshLocals(script);
        WriteZshVerbWalk(script);
        WriteZshEmptyVerb(script);
        Line(script, "    case \"$verb\" in");
    }

    private static void WriteZshLocals(StringBuilder script)
    {
        Line(script, "_okf() {");
        Line(script, "    local cur prev verb sub word index skip");
        Line(script, "    local -a verbs options");
        Line(script, "    cur=\"${words[CURRENT]}\"");
        Line(script, "    prev=\"\"");
        Line(script, "    (( CURRENT > 1 )) && prev=\"${words[CURRENT-1]}\"");
        Line(script, "");
    }

    private static void WriteZshVerbWalk(StringBuilder script)
    {
        Line(script, "    # The verb and its subcommand are the first two bare words that are not some");
        Line(script, "    # flag's value, which is why the value-taking flags are listed here.");
        Line(script, "    verb=\"\"");
        Line(script, "    sub=\"\"");
        Line(script, "    skip=0");
        Line(script, "    index=2");
        Line(script, "    while (( index < CURRENT )); do");
        Line(script, "        word=\"${words[index]}\"");
        Line(script, "        (( index++ ))");
        Line(script, "        if (( skip )); then");
        Line(script, "            skip=0");
        Line(script, "            continue");
        Line(script, "        fi");
        Line(script, "        case \"$word\" in");
        Line(script, "            (--*=*) ;;");
        Line(script, $"            ({string.Join("|", ValueFlags)}) skip=1 ;;");
        Line(script, "            (-*) ;;");
        Line(script, "            (*)");
        Line(script, "                if [[ -z \"$verb\" ]]; then");
        Line(script, "                    verb=\"$word\"");
        Line(script, "                elif [[ -z \"$sub\" ]]; then");
        Line(script, "                    sub=\"$word\"");
        Line(script, "                fi");
        Line(script, "                ;;");
        Line(script, "        esac");
        Line(script, "    done");
        Line(script, "");
    }

    private static void WriteZshEmptyVerb(StringBuilder script)
    {
        Line(script, "    if [[ -z \"$verb\" ]]; then");
        Line(script, "        verbs=(");
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line(script, $"            '{verb.Name}:{verb.Summary}'");
        }

        Line(script, "        )");
        Line(script, "        _describe -t verbs 'okf verb' verbs");
        Line(script, "        return");
        Line(script, "    fi");
        Line(script, "");
    }

    private static void WriteZshVerbs(StringBuilder script)
    {
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            WriteZshVerb(script, verb);
        }
    }

    private static void WriteZshVerb(StringBuilder script, CompletionVerb verb)
    {
        Line(script, $"        ({verb.Name})");
        WriteZshValueGroups(script, verb);
        WriteZshFlags(script, verb);
        WriteZshSubcommands(script, verb);
        WriteZshOperand(script, verb);
        Line(script, "            ;;");
    }

    private static void WriteZshValueGroups(StringBuilder script, CompletionVerb verb)
    {
        List<(CompletionValue Value, IReadOnlyList<string> Flags)> groups = ValueGroups(verb).ToList();
        if (groups.Count == 0)
        {
            return;
        }

        Line(script, "            case \"$prev\" in");
        foreach ((CompletionValue value, IReadOnlyList<string> flags) in groups)
        {
            string pattern = string.Join("|", flags);
            Line(script, value.Kind switch
            {
                "path" => $"                ({pattern}) _files; return ;;",
                "skill" => $"                ({pattern}) __okf_skill_names; return ;;",
                "text" => $"                ({pattern}) return ;;",
                _ => $"                ({pattern}) compadd -- {Words(value.Candidates)}; return ;;",
            });
        }

        Line(script, "            esac");
    }

    private static void WriteZshFlags(StringBuilder script, CompletionVerb verb)
    {
        if (verb.Flags.Count == 0)
        {
            return;
        }

        Line(script, "            if [[ \"$cur\" == -* ]]; then");
        Line(script, "                options=(");
        foreach (CompletionFlag flag in verb.Flags)
        {
            Line(script, $"                    '{flag.Name}:{flag.Description}'");
        }

        Line(script, "                )");
        Line(script, "                _describe -t options 'option' options");
        Line(script, "                return");
        Line(script, "            fi");
    }

    private static void WriteZshSubcommands(StringBuilder script, CompletionVerb verb)
    {
        if (verb.Subcommands.Count == 0)
        {
            return;
        }

        Line(script, "            if [[ -z \"$sub\" ]]; then");
        Line(script, $"                compadd -- {Words(verb.Subcommands)}");
        Line(script, "                return");
        Line(script, "            fi");
    }

    private static void WriteZshOperand(StringBuilder script, CompletionVerb verb)
    {
        string guard = OperandCondition(verb, "$sub");
        string indent = guard.Length > 0 ? "                " : "            ";
        if (guard.Length > 0)
        {
            Line(script, $"            if [[ {guard.Replace(" -o ", " || ", StringComparison.Ordinal)} ]]; then");
        }

        switch (verb.Operand.Kind)
        {
            case "path":
                Line(script, $"{indent}_files");
                break;

            case "skill":
                Line(script, $"{indent}__okf_skill_names");
                break;

            case "none" or "text":
                break;

            default:
                Line(script, $"{indent}compadd -- {Words(verb.Operand.Candidates)}");
                break;
        }

        if (guard.Length > 0)
        {
            Line(script, "            fi");
        }
    }

    private static string Fish()
    {
        StringBuilder script = new StringBuilder();
        WriteFishPreamble(script);
        WriteFishVerbs(script);
        return script.ToString();
    }

    private static void WriteFishPreamble(StringBuilder script)
    {
        Line(script, "# okf completion for fish. Generated by `okf completion fish`; do not edit.");
        Line(script, "#");
        Line(script, "# Load it into this shell:  okf completion fish | source");
        Line(script, "# Install it permanently:   okf completion fish > \\");
        Line(script, "#     \"$XDG_CONFIG_HOME/fish/completions/okf.fish\"  (else ~/.config/fish/completions)");
        Line(script, "");
        Line(script, "# The verb position: no files here, only verbs.");
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line(script, $"complete -c okf -n '__fish_use_subcommand' -f -a '{verb.Name}' -d '{verb.Summary}'");
        }
    }

    private static void WriteFishVerbs(StringBuilder script)
    {
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            WriteFishVerb(script, verb);
        }
    }

    private static void WriteFishVerb(StringBuilder script, CompletionVerb verb)
    {
        Line(script, "");
        Line(script, $"# okf {verb.Name}");
        string condition = $"__fish_seen_subcommand_from {verb.Name}";
        WriteFishFlags(script, verb, condition);
        WriteFishSubcommands(script, verb, condition);
        WriteFishOperand(script, verb, condition);
    }

    private static void WriteFishFlags(StringBuilder script, CompletionVerb verb, string condition)
    {
        foreach (CompletionFlag flag in verb.Flags)
        {
            string spelling = flag.Name.StartsWith("--", StringComparison.Ordinal)
                ? $"-l {flag.Name[2..]}"
                : $"-s {flag.Name[1..]}";
            string value = flag.Value.Kind switch
            {
                "none" => string.Empty,
                "path" => " -r -F",
                "skill" => " -x -a '(okf skills list --names 2>/dev/null)'",
                "text" => " -x",
                _ => $" -x -a '{Words(flag.Value.Candidates)}'",
            };

            Line(script, $"complete -c okf -n '{condition}' {spelling}{value} -d '{flag.Description}'");
        }
    }

    private static void WriteFishSubcommands(StringBuilder script, CompletionVerb verb, string condition)
    {
        if (verb.Subcommands.Count == 0)
        {
            return;
        }

        Line(
            script,
            $"complete -c okf -n '{condition}; and not __fish_seen_subcommand_from " +
            $"{Words(verb.Subcommands)}' -f -a '{Words(verb.Subcommands)}'");
    }

    private static void WriteFishOperand(StringBuilder script, CompletionVerb verb, string condition)
    {
        string seen = verb.OperandSubcommands.Count > 0
            ? $"{condition}; and __fish_seen_subcommand_from {Words(verb.OperandSubcommands)}"
            : condition;
        switch (verb.Operand.Kind)
        {
            case "skill":
                Line(script, $"complete -c okf -n '{seen}' -f -a '(okf skills list --names 2>/dev/null)'");
                break;

            case "none":
                if (verb.Subcommands.Count == 0)
                {
                    Line(script, $"complete -c okf -n '{seen}' -f");
                }

                break;

            case "path" or "text":
                break;

            default:
                Line(script, $"complete -c okf -n '{seen}' -f -a '{Words(verb.Operand.Candidates)}'");
                break;
        }
    }

    private static string PowerShell()
    {
        StringBuilder script = new StringBuilder();
        WritePowerShellPreamble(script);
        WritePowerShellScan(script);
        WritePowerShellVerbs(script);
        WritePowerShellResult(script);
        return script.ToString();
    }

    private static void WritePowerShellPreamble(StringBuilder script)
    {
        Line(script, "# okf completion for PowerShell. Generated by `okf completion pwsh`; do not edit.");
        Line(script, "#");
        Line(script, "# Load it into this session:  okf completion pwsh | Out-String | Invoke-Expression");
        Line(script, "# Install it permanently:     add that line to your $PROFILE");
        Line(script, "");
        Line(script, "Register-ArgumentCompleter -Native -CommandName okf -ScriptBlock {");
        Line(script, "    param($wordToComplete, $commandAst, $cursorPosition)");
        Line(script, "");
        Line(script, "    $words = @($commandAst.CommandElements | ForEach-Object { $_.ToString() })");
        Line(script, $"    $valueFlags = @({string.Join(", ", ValueFlags.Select(flag => $"'{flag}'"))})");
        Line(script, "");
    }

    private static void WritePowerShellScan(StringBuilder script)
    {
        Line(script, "    # The verb and its subcommand are the first two bare words that are not some");
        Line(script, "    # flag's value.");
        Line(script, "    $verb = ''");
        Line(script, "    $sub = ''");
        Line(script, "    $skip = $false");
        Line(script, "    if ($words.Count -gt 1) {");
        Line(script, "        foreach ($word in $words[1..($words.Count - 1)]) {");
        Line(script, "            if ($skip) { $skip = $false; continue }");
        Line(script, "            if ($word -like '--*=*') { continue }");
        Line(script, "            if ($valueFlags -contains $word) { $skip = $true; continue }");
        Line(script, "            if ($word -like '-*') { continue }");
        Line(script, "            if (-not $verb) { $verb = $word } elseif (-not $sub) { $sub = $word }");
        Line(script, "        }");
        Line(script, "    }");
        Line(script, "    if ($words.Count -gt 1 -and $wordToComplete) {");
        Line(script, "        # The word under the cursor is not a decided verb yet.");
        Line(script, "        if ($sub -eq $wordToComplete) { $sub = '' } elseif ($verb -eq $wordToComplete) { $verb = '' }");
        Line(script, "    }");
        Line(script, "");
        Line(script, "    $previous = ''");
        Line(script, "    if ($words.Count -gt 1) {");
        Line(script, "        $previous = if ($wordToComplete) { $words[$words.Count - 2] } else { $words[$words.Count - 1] }");
        Line(script, "    }");
        Line(script, "");
        Line(script, "    $candidates = @()");
        Line(script, "    switch ($verb) {");
    }

    private static void WritePowerShellVerbs(StringBuilder script)
    {
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            WritePowerShellVerb(script, verb);
        }
    }

    private static void WritePowerShellVerb(StringBuilder script, CompletionVerb verb)
    {
        Line(script, $"        '{verb.Name}' {{");
        Line(script, "            switch ($previous) {");
        WritePowerShellValueGroups(script, verb);
        WritePowerShellFallback(script, verb);
        Line(script, "            }");
        WritePowerShellOperand(script, verb);
        Line(script, "        }");
    }

    private static void WritePowerShellValueGroups(StringBuilder script, CompletionVerb verb)
    {
        foreach ((CompletionValue value, IReadOnlyList<string> flags) in ValueGroups(verb))
        {
            string words = value.Kind switch
            {
                "path" => "@()",
                "skill" => "@(& okf skills list --names 2>$null)",
                "text" => "@()",
                _ => $"@({string.Join(", ", value.Candidates.Select(candidate => $"'{candidate}'"))})",
            };

            foreach (string flag in flags)
            {
                Line(script, $"                '{flag}' {{ $candidates = {words} }}");
            }
        }
    }

    private static void WritePowerShellFallback(StringBuilder script, CompletionVerb verb)
    {
        List<string> fallback = new List<string>();
        fallback.AddRange(verb.Flags.Select(flag => $"'{flag.Name}'"));
        if (verb.Subcommands.Count > 0)
        {
            fallback.AddRange(verb.Subcommands.Select(subcommand => $"'{subcommand}'"));
        }

        Line(script, $"                default {{ $candidates = @({string.Join(", ", fallback)}) }}");
    }

    private static void WritePowerShellOperand(StringBuilder script, CompletionVerb verb)
    {
        if (verb.Operand.Kind == "skill")
        {
            Line(script, "            if (-not $previous.StartsWith('-') -and $sub -eq 'path') {");
            Line(script, "                $candidates += @(& okf skills list --names 2>$null)");
            Line(script, "            }");
            return;
        }

        if (verb.Operand.Candidates.Count == 0)
        {
            return;
        }

        string operands = string.Join(", ", verb.Operand.Candidates.Select(candidate => $"'{candidate}'"));
        Line(script, "            if (-not $previous.StartsWith('-')) {");
        Line(script, $"                $candidates += @({operands})");
        Line(script, "            }");
    }

    private static void WritePowerShellResult(StringBuilder script)
    {
        string verbNames = string.Join(", ", CompletionTable.Names.Select(name => $"'{name}'"));
        Line(script, $"        default {{ $candidates = @({verbNames}) }}");
        Line(script, "    }");
        Line(script, "");
        Line(script, "    $candidates |");
        Line(script, "        Where-Object { $_ -like \"$wordToComplete*\" } |");
        Line(script, "        Sort-Object -Unique |");
        Line(script, "        ForEach-Object {");
        Line(script, "            [System.Management.Automation.CompletionResult]::new(");
        Line(script, "                $_, $_, 'ParameterValue', $_)");
        Line(script, "        }");
        Line(script, "}");
    }
}
