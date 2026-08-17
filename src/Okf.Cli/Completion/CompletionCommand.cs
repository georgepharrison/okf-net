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
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        return Refuse(error, $"Unknown option '{argument}'.");
                    }

                    if (shell is not null)
                    {
                        return Refuse(
                            error,
                            $"`okf completion` takes one shell; got '{shell}' and '{argument}'.");
                    }

                    shell = argument;
                    break;
            }
        }

        if (shell is null)
        {
            return Refuse(error, $"`okf completion` needs a shell: {Shells}.");
        }

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

    private static string Bash()
    {
        StringBuilder script = new StringBuilder();
        void Line(string text) => script.Append(text).Append('\n');

        Line("# okf completion for bash. Generated by `okf completion bash`; do not edit.");
        Line("#");
        Line("# Load it into this shell:  eval \"$(okf completion bash)\"");
        Line("# Install it permanently:   okf completion bash > \\");
        Line("#     \"${XDG_DATA_HOME:-$HOME/.local/share}/bash-completion/completions/okf\"");
        Line("");
        Line("__okf_words() {");
        Line("    COMPREPLY=( $(compgen -W \"$1\" -- \"$2\") )");
        Line("}");
        Line("");
        Line("__okf_files() {");
        Line("    # A path may contain spaces, and the default IFS would split one such name");
        Line("    # into several candidates. `local IFS` keeps this to the one function and");
        Line("    # stays inside bash 3.2, which is what macOS still ships.");
        Line("    local IFS=$'\\n'");
        Line("    COMPREPLY=( $(compgen -f -- \"$1\") )");
        Line("    compopt -o filenames 2>/dev/null");
        Line("}");
        Line("");
        Line("# The only thing a completion asks okf for. It answers from the binary itself, so");
        Line("# there is no vault to find and no directory to walk.");
        Line("__okf_skills() {");
        Line("    COMPREPLY=( $(compgen -W \"$(\"$2\" skills list --names 2>/dev/null)\" -- \"$1\") )");
        Line("}");
        Line("");
        Line("_okf() {");
        Line("    local cur prev verb sub word skip index");
        Line("");
        Line("    # bash does not clear this between completions; a function that leaves it");
        Line("    # alone offers the previous word's answers.");
        Line("    COMPREPLY=()");
        Line("    cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        Line("    prev=\"\"");
        Line("    if [ \"$COMP_CWORD\" -gt 0 ]; then");
        Line("        prev=\"${COMP_WORDS[COMP_CWORD-1]}\"");
        Line("    fi");
        Line("");
        Line("    # The verb and its subcommand are the first two bare words that are not some");
        Line("    # flag's value, which is why the value-taking flags are listed here.");
        Line("    verb=\"\"");
        Line("    sub=\"\"");
        Line("    skip=0");
        Line("    index=1");
        Line("    while [ \"$index\" -lt \"$COMP_CWORD\" ]; do");
        Line("        word=\"${COMP_WORDS[index]}\"");
        Line("        index=$((index + 1))");
        Line("        if [ \"$skip\" = 1 ]; then");
        Line("            skip=0");
        Line("            continue");
        Line("        fi");
        Line("        case \"$word\" in");
        Line("            --*=*) ;;");
        Line($"            {string.Join("|", ValueFlags)}) skip=1 ;;");
        Line("            -*) ;;");
        Line("            *)");
        Line("                if [ -z \"$verb\" ]; then");
        Line("                    verb=\"$word\"");
        Line("                elif [ -z \"$sub\" ]; then");
        Line("                    sub=\"$word\"");
        Line("                fi");
        Line("                ;;");
        Line("        esac");
        Line("    done");
        Line("");
        Line("    if [ -z \"$verb\" ]; then");
        Line($"        __okf_words \"{Words(CompletionTable.Names)} --help -h --version\" \"$cur\"");
        Line("        return");
        Line("    fi");
        Line("");
        Line("    case \"$verb\" in");

        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line($"        {verb.Name})");
            List<(CompletionValue Value, IReadOnlyList<string> Flags)> groups = ValueGroups(verb).ToList();
            if (groups.Count > 0)
            {
                Line("            case \"$prev\" in");
                foreach ((CompletionValue value, IReadOnlyList<string> flags) in groups)
                {
                    string pattern = string.Join("|", flags);
                    Line(value.Kind switch
                    {
                        "path" => $"                {pattern}) __okf_files \"$cur\"; return ;;",
                        "skill" => $"                {pattern}) __okf_skills \"$cur\" \"${{COMP_WORDS[0]}}\"; return ;;",
                        "text" => $"                {pattern}) return ;;",
                        _ => $"                {pattern}) __okf_words \"{Words(value.Candidates)}\" \"$cur\"; return ;;",
                    });
                }

                Line("            esac");
            }

            if (verb.Flags.Count > 0)
            {
                Line("            case \"$cur\" in");
                Line($"                -*) __okf_words \"{FlagWords(verb)}\" \"$cur\"; return ;;");
                Line("            esac");
            }

            if (verb.Subcommands.Count > 0)
            {
                Line("            if [ -z \"$sub\" ]; then");
                Line($"                __okf_words \"{Words(verb.Subcommands)}\" \"$cur\"");
                Line("                return");
                Line("            fi");
            }

            string guard = OperandCondition(verb, "$sub");
            string indent = guard.Length > 0 ? "                " : "            ";
            if (guard.Length > 0)
            {
                Line($"            if [ {guard} ]; then");
            }

            switch (verb.Operand.Kind)
            {
                case "path":
                    Line($"{indent}__okf_files \"$cur\"");
                    break;

                case "skill":
                    Line($"{indent}__okf_skills \"$cur\" \"${{COMP_WORDS[0]}}\"");
                    break;

                case "none" or "text":
                    break;

                default:
                    Line($"{indent}__okf_words \"{Words(verb.Operand.Candidates)}\" \"$cur\"");
                    break;
            }

            if (guard.Length > 0)
            {
                Line("            fi");
            }

            Line("            ;;");
        }

        Line("    esac");
        Line("}");
        Line("");
        Line("complete -F _okf okf");
        return script.ToString();
    }

    private static string Zsh()
    {
        StringBuilder script = new StringBuilder();
        void Line(string text) => script.Append(text).Append('\n');

        Line("#compdef okf");
        Line("# okf completion for zsh. Generated by `okf completion zsh`; do not edit.");
        Line("#");
        Line("# Load it into this shell:  eval \"$(okf completion zsh)\"");
        Line("# Install it permanently:   okf completion zsh > \\");
        Line("#     \"${XDG_DATA_HOME:-$HOME/.local/share}/zsh/site-functions/_okf\"");
        Line("# (that directory has to be on $fpath before compinit runs)");
        Line("");
        Line("__okf_skill_names() {");
        Line("    local -a names");
        Line("    names=(${(f)\"$(okf skills list --names 2>/dev/null)\"})");
        Line("    compadd -a names");
        Line("}");
        Line("");
        Line("_okf() {");
        Line("    local cur prev verb sub word index skip");
        Line("    local -a verbs options");
        Line("    cur=\"${words[CURRENT]}\"");
        Line("    prev=\"\"");
        Line("    (( CURRENT > 1 )) && prev=\"${words[CURRENT-1]}\"");
        Line("");
        Line("    # The verb and its subcommand are the first two bare words that are not some");
        Line("    # flag's value, which is why the value-taking flags are listed here.");
        Line("    verb=\"\"");
        Line("    sub=\"\"");
        Line("    skip=0");
        Line("    index=2");
        Line("    while (( index < CURRENT )); do");
        Line("        word=\"${words[index]}\"");
        Line("        (( index++ ))");
        Line("        if (( skip )); then");
        Line("            skip=0");
        Line("            continue");
        Line("        fi");
        Line("        case \"$word\" in");
        Line("            (--*=*) ;;");
        Line($"            ({string.Join("|", ValueFlags)}) skip=1 ;;");
        Line("            (-*) ;;");
        Line("            (*)");
        Line("                if [[ -z \"$verb\" ]]; then");
        Line("                    verb=\"$word\"");
        Line("                elif [[ -z \"$sub\" ]]; then");
        Line("                    sub=\"$word\"");
        Line("                fi");
        Line("                ;;");
        Line("        esac");
        Line("    done");
        Line("");
        Line("    if [[ -z \"$verb\" ]]; then");
        Line("        verbs=(");
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line($"            '{verb.Name}:{verb.Summary}'");
        }

        Line("        )");
        Line("        _describe -t verbs 'okf verb' verbs");
        Line("        return");
        Line("    fi");
        Line("");
        Line("    case \"$verb\" in");

        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line($"        ({verb.Name})");
            List<(CompletionValue Value, IReadOnlyList<string> Flags)> groups = ValueGroups(verb).ToList();
            if (groups.Count > 0)
            {
                Line("            case \"$prev\" in");
                foreach ((CompletionValue value, IReadOnlyList<string> flags) in groups)
                {
                    string pattern = string.Join("|", flags);
                    Line(value.Kind switch
                    {
                        "path" => $"                ({pattern}) _files; return ;;",
                        "skill" => $"                ({pattern}) __okf_skill_names; return ;;",
                        "text" => $"                ({pattern}) return ;;",
                        _ => $"                ({pattern}) compadd -- {Words(value.Candidates)}; return ;;",
                    });
                }

                Line("            esac");
            }

            if (verb.Flags.Count > 0)
            {
                Line("            if [[ \"$cur\" == -* ]]; then");
                Line("                options=(");
                foreach (CompletionFlag flag in verb.Flags)
                {
                    Line($"                    '{flag.Name}:{flag.Description}'");
                }

                Line("                )");
                Line("                _describe -t options 'option' options");
                Line("                return");
                Line("            fi");
            }

            if (verb.Subcommands.Count > 0)
            {
                Line("            if [[ -z \"$sub\" ]]; then");
                Line($"                compadd -- {Words(verb.Subcommands)}");
                Line("                return");
                Line("            fi");
            }

            string guard = OperandCondition(verb, "$sub");
            string indent = guard.Length > 0 ? "                " : "            ";
            if (guard.Length > 0)
            {
                Line($"            if [[ {guard.Replace(" -o ", " || ", StringComparison.Ordinal)} ]]; then");
            }

            switch (verb.Operand.Kind)
            {
                case "path":
                    Line($"{indent}_files");
                    break;

                case "skill":
                    Line($"{indent}__okf_skill_names");
                    break;

                case "none" or "text":
                    break;

                default:
                    Line($"{indent}compadd -- {Words(verb.Operand.Candidates)}");
                    break;
            }

            if (guard.Length > 0)
            {
                Line("            fi");
            }

            Line("            ;;");
        }

        Line("    esac");
        Line("}");
        Line("");
        Line("# Both entry points: sourced (`eval \"$(okf completion zsh)\"`) the function has to");
        Line("# register itself, autoloaded from $fpath it is already the completer being called.");
        Line("if [ \"$funcstack[1]\" = \"_okf\" ]; then");
        Line("    _okf \"$@\"");
        Line("else");
        Line("    compdef _okf okf");
        Line("fi");
        return script.ToString();
    }

    private static string Fish()
    {
        StringBuilder script = new StringBuilder();
        void Line(string text) => script.Append(text).Append('\n');

        Line("# okf completion for fish. Generated by `okf completion fish`; do not edit.");
        Line("#");
        Line("# Load it into this shell:  okf completion fish | source");
        Line("# Install it permanently:   okf completion fish > \\");
        Line("#     \"$XDG_CONFIG_HOME/fish/completions/okf.fish\"  (else ~/.config/fish/completions)");
        Line("");
        Line("# The verb position: no files here, only verbs.");
        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line($"complete -c okf -n '__fish_use_subcommand' -f -a '{verb.Name}' -d '{verb.Summary}'");
        }

        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line("");
            Line($"# okf {verb.Name}");
            string condition = $"__fish_seen_subcommand_from {verb.Name}";

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

                Line($"complete -c okf -n '{condition}' {spelling}{value} -d '{flag.Description}'");
            }

            if (verb.Subcommands.Count > 0)
            {
                Line(
                    $"complete -c okf -n '{condition}; and not __fish_seen_subcommand_from " +
                    $"{Words(verb.Subcommands)}' -f -a '{Words(verb.Subcommands)}'");
            }

            string seen = verb.OperandSubcommands.Count > 0
                ? $"{condition}; and __fish_seen_subcommand_from {Words(verb.OperandSubcommands)}"
                : condition;

            switch (verb.Operand.Kind)
            {
                case "skill":
                    Line($"complete -c okf -n '{seen}' -f -a '(okf skills list --names 2>/dev/null)'");
                    break;

                case "none":
                    if (verb.Subcommands.Count == 0)
                    {
                        Line($"complete -c okf -n '{seen}' -f");
                    }

                    break;

                case "path" or "text":
                    break;

                default:
                    Line($"complete -c okf -n '{seen}' -f -a '{Words(verb.Operand.Candidates)}'");
                    break;
            }
        }

        return script.ToString();
    }

    private static string PowerShell()
    {
        StringBuilder script = new StringBuilder();
        void Line(string text) => script.Append(text).Append('\n');

        Line("# okf completion for PowerShell. Generated by `okf completion pwsh`; do not edit.");
        Line("#");
        Line("# Load it into this session:  okf completion pwsh | Out-String | Invoke-Expression");
        Line("# Install it permanently:     add that line to your $PROFILE");
        Line("");
        Line("Register-ArgumentCompleter -Native -CommandName okf -ScriptBlock {");
        Line("    param($wordToComplete, $commandAst, $cursorPosition)");
        Line("");
        Line("    $words = @($commandAst.CommandElements | ForEach-Object { $_.ToString() })");
        Line($"    $valueFlags = @({string.Join(", ", ValueFlags.Select(flag => $"'{flag}'"))})");
        Line("");
        Line("    # The verb and its subcommand are the first two bare words that are not some");
        Line("    # flag's value.");
        Line("    $verb = ''");
        Line("    $sub = ''");
        Line("    $skip = $false");
        Line("    if ($words.Count -gt 1) {");
        Line("        foreach ($word in $words[1..($words.Count - 1)]) {");
        Line("            if ($skip) { $skip = $false; continue }");
        Line("            if ($word -like '--*=*') { continue }");
        Line("            if ($valueFlags -contains $word) { $skip = $true; continue }");
        Line("            if ($word -like '-*') { continue }");
        Line("            if (-not $verb) { $verb = $word } elseif (-not $sub) { $sub = $word }");
        Line("        }");
        Line("    }");
        Line("    if ($words.Count -gt 1 -and $wordToComplete) {");
        Line("        # The word under the cursor is not a decided verb yet.");
        Line("        if ($sub -eq $wordToComplete) { $sub = '' } elseif ($verb -eq $wordToComplete) { $verb = '' }");
        Line("    }");
        Line("");
        Line("    $previous = ''");
        Line("    if ($words.Count -gt 1) {");
        Line("        $previous = if ($wordToComplete) { $words[$words.Count - 2] } else { $words[$words.Count - 1] }");
        Line("    }");
        Line("");
        Line("    $candidates = @()");
        Line("    switch ($verb) {");

        foreach (CompletionVerb verb in CompletionTable.Verbs)
        {
            Line($"        '{verb.Name}' {{");
            Line("            switch ($previous) {");
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
                    Line($"                '{flag}' {{ $candidates = {words} }}");
                }
            }

            List<string> fallback = new List<string>();
            fallback.AddRange(verb.Flags.Select(flag => $"'{flag.Name}'"));
            if (verb.Subcommands.Count > 0)
            {
                fallback.AddRange(verb.Subcommands.Select(subcommand => $"'{subcommand}'"));
            }

            Line($"                default {{ $candidates = @({string.Join(", ", fallback)}) }}");
            Line("            }");
            if (verb.Operand.Kind == "skill")
            {
                Line("            if (-not $previous.StartsWith('-') -and $sub -eq 'path') {");
                Line("                $candidates += @(& okf skills list --names 2>$null)");
                Line("            }");
            }
            else if (verb.Operand.Candidates.Count > 0)
            {
                string operands = string.Join(", ", verb.Operand.Candidates.Select(candidate => $"'{candidate}'"));
                Line("            if (-not $previous.StartsWith('-')) {");
                Line($"                $candidates += @({operands})");
                Line("            }");
            }

            Line("        }");
        }

        string verbNames = string.Join(", ", CompletionTable.Names.Select(name => $"'{name}'"));
        Line($"        default {{ $candidates = @({verbNames}) }}");
        Line("    }");
        Line("");
        Line("    $candidates |");
        Line("        Where-Object { $_ -like \"$wordToComplete*\" } |");
        Line("        Sort-Object -Unique |");
        Line("        ForEach-Object {");
        Line("            [System.Management.Automation.CompletionResult]::new(");
        Line("                $_, $_, 'ParameterValue', $_)");
        Line("        }");
        Line("}");
        return script.ToString();
    }
}
