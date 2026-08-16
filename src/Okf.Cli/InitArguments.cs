using Okf.Core;

namespace Okf.Cli;

/// <summary>The parsed form of <c>okf init</c>'s command line.</summary>
internal sealed class InitArguments
{
    private InitArguments()
    {
    }

    /// <summary>The explicit target path, or <see langword="null" /> for the working directory.</summary>
    public string? Path { get; private set; }

    /// <summary>The bundle directory's name, or <see langword="null" /> to derive one.</summary>
    public string? Name { get; private set; }

    /// <summary>
    /// Whether to scaffold the personal vault (<c>OKF_HOME</c>, else <c>~/okf</c>) rather
    /// than a project vault (decisions.md §6).
    /// </summary>
    public bool Personal { get; private set; }

    /// <summary>Whether to report how the target was resolved.</summary>
    public bool Verbose { get; private set; }

    /// <summary>
    /// Whether to skip writing the <c>AGENTS.md</c> / <c>CLAUDE.md</c> context pointer at
    /// the project root (<c>--no-agents-md</c>). Written by default.
    /// </summary>
    public bool NoAgentsMd { get; private set; }

    /// <summary>Whether the command should print its help and stop.</summary>
    public bool ShowHelp { get; private set; }

    /// <summary>
    /// Every option <see cref="Parse" /> accepts. <see cref="CompletionTable" /> is checked
    /// against this array, and this array against the parser's own <c>case</c> labels
    /// (issue #51), so an option cannot ship without a completion entry.
    /// </summary>
    public static readonly string[] Flags =
        ["--help", "-h", "--personal", "--verbose", "-v", "--name", "--no-agents-md"];

    /// <summary>Parses <c>okf init</c>'s arguments.</summary>
    /// <param name="args">The arguments after <c>init</c>.</param>
    /// <returns>The parsed arguments.</returns>
    /// <exception cref="OkfConfigException">An argument is unknown, malformed, or repeated.</exception>
    public static InitArguments Parse(string[] args)
    {
        var parsed = new InitArguments();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var (name, inlineValue) = CliArguments.Split(argument);

            switch (name)
            {
                case "--help" or "-h":
                    parsed.ShowHelp = true;
                    break;

                case "--personal":
                    parsed.Personal = true;
                    break;

                case "--verbose" or "-v":
                    parsed.Verbose = true;
                    break;

                case "--no-agents-md":
                    parsed.NoAgentsMd = true;
                    break;

                case "--name":
                    if (parsed.Name is not null)
                    {
                        throw new OkfConfigException("Option '--name' was given more than once.");
                    }

                    parsed.Name = inlineValue ?? CliArguments.Next(args, ref index, name);
                    break;

                default:
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OkfConfigException($"Unknown option '{argument}'.");
                    }

                    if (parsed.Path is not null)
                    {
                        throw new OkfConfigException(
                            $"`okf init` takes at most one path; got '{parsed.Path}' and '{argument}'.");
                    }

                    parsed.Path = argument;
                    break;
            }
        }

        // The personal vault's location comes from OKF_HOME or ~/okf and from nowhere
        // else (decisions.md §6). Accepting a path beside --personal would mean one of
        // the two silently lost, and which one lost would be the kind of thing a user
        // discovers by finding a vault somewhere they did not put it.
        if (parsed.Personal && parsed.Path is not null)
        {
            throw new OkfConfigException(
                $"`--personal` targets the personal vault (OKF_HOME, else ~/okf); it cannot be combined " +
                $"with the path '{parsed.Path}'.");
        }

        return parsed;
    }
}
