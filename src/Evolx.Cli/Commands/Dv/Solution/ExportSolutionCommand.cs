using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Solution;

/// <summary>
/// `ev dv solution export` — POST ExportSolution, write the returned .zip to disk.
/// The action returns the file body as base64 inside <c>ExportSolutionFile</c>.
/// </summary>
public sealed class ExportSolutionCommand : DvCommandBase<ExportSolutionCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Solution unique name.")]
        public string Name { get; set; } = "";

        [CommandOption("--out <FILE>")]
        [Description("Output .zip path. Default: <name>.zip in cwd.")]
        public string? Out { get; set; }

        [CommandOption("--managed")]
        [Description("Export the managed flavor instead of unmanaged.")]
        public bool Managed { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
            throw new ArgumentException("Solution name is required.");

        if (await dv.TryGetSolutionAsync(s.Name, ct) is null)
            throw new InvalidOperationException($"Solution '{s.Name}' not found in this env.");

        var body = new ExportSolutionBody { SolutionName = s.Name, Managed = s.Managed };

        AnsiConsole.MarkupLine($"[dim]Exporting[/] [bold]{Markup.Escape(s.Name)}[/] (managed={s.Managed})...");
        var response = await dv.InvokeActionAsync("ExportSolution", body, ct: ct);

        if (!response.TryGetProperty("ExportSolutionFile", out var fileProp)
            || fileProp.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw new InvalidOperationException("ExportSolution response did not include ExportSolutionFile.");
        }

        var base64 = fileProp.GetString() ?? "";
        var bytes = Convert.FromBase64String(base64);

        var output = Path.GetFullPath(s.Out ?? $"{s.Name}.zip");
        var dir = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(output, bytes, ct);

        AnsiConsole.MarkupLine($"[green]Exported[/] {bytes.Length:n0} bytes to [bold]{Markup.Escape(output)}[/]");
        return 0;
    }
}
