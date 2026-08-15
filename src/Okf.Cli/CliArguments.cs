using Okf.Core;

namespace Okf.Cli;

/// <summary>
/// The argument-parsing primitives every command's command line is built from. Parsing is
/// hand-rolled (see <see cref="CliApplication" />), so these are the pieces that keep
/// <c>--format=json</c>, <c>--format json</c>, and an unknown value behaving identically
/// wherever they appear.
/// </summary>
internal static class CliArguments
{
    /// <summary>Splits <c>--name=value</c> into its parts; anything else keeps a null value.</summary>
    /// <param name="argument">The raw argument.</param>
    /// <returns>The option name and its inline value, when it carried one.</returns>
    public static (string Name, string? Value) Split(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var separator = argument.IndexOf('=', StringComparison.Ordinal);
        return argument.StartsWith("--", StringComparison.Ordinal) && separator > 0
            ? (argument[..separator], argument[(separator + 1)..])
            : (argument, null);
    }

    /// <summary>Takes the next argument as an option's value, advancing the index.</summary>
    /// <param name="args">The full argument list.</param>
    /// <param name="index">The current position, advanced past the value.</param>
    /// <param name="option">The option's name, for the error message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="OkfConfigException">The option is the last argument.</exception>
    public static string Next(string[] args, ref int index, string option)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (index + 1 >= args.Length)
        {
            throw new OkfConfigException($"Option '{option}' requires a value.");
        }

        return args[++index];
    }

    /// <summary>Reads a <c>--format</c> value.</summary>
    /// <param name="format">The value.</param>
    /// <returns><see langword="true" /> for <c>json</c>, <see langword="false" /> for <c>text</c>.</returns>
    /// <exception cref="OkfConfigException">The value names neither format.</exception>
    public static bool ParseFormat(string format) => format switch
    {
        "json" => true,
        "text" => false,
        _ => throw new OkfConfigException($"Unknown --format value '{format}'; expected 'text' or 'json'."),
    };
}
