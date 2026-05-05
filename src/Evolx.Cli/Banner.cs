using System.Reflection;
using Spectre.Console;

namespace Evolx.Cli;

/// <summary>
/// Prints a one-line "ev x.y.z — &lt;description&gt;" banner to stderr at startup.
/// Reads the values from the entry assembly's metadata attributes — no hardcoding,
/// no separate registry. Goes to stderr so it never pollutes stdout consumers
/// (e.g. `ev dv data foo --json | jq`).
/// </summary>
internal static class Banner
{
    public static void Print()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(Banner).Assembly;
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "0.0.0";
        // Strip a trailing "+<commit-hash>" appended by the SDK in source-link builds.
        var plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];

        var description = asm.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
                          ?? "Evolx CLI.";

        AnsiConsole.Console.Profile.Out = new AnsiConsoleOutput(Console.Error);
        AnsiConsole.MarkupLine($"[dim]ev {Markup.Escape(version)} — {Markup.Escape(description)}[/]");
        // Restore stdout for the rest of the run so command output goes where users expect.
        AnsiConsole.Console.Profile.Out = new AnsiConsoleOutput(Console.Out);
    }
}
