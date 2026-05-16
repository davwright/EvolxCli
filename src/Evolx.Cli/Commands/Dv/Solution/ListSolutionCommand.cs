using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Solution;

/// <summary>
/// `ev dv solution list` — list solutions in the bound env.
/// </summary>
public sealed class ListSolutionCommand : DvCommandBase<ListSolutionCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--unmanaged-only")]
        [Description("Show only unmanaged solutions.")]
        public bool UnmanagedOnly { get; set; }

        [CommandOption("--json")]
        [Description("Print raw JSON.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var result = await dv.ListSolutionsAsync(s.UnmanagedOnly, ct);

        if (s.Json)
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]No solutions in response.[/]");
            return 1;
        }

        var rows = value.EnumerateArray().ToList();
        var t = new Table().Border(TableBorder.Minimal)
            .AddColumns("UniqueName", "FriendlyName", "Version", "Managed", "InstalledOn");
        foreach (var row in rows)
        {
            t.AddRow(
                Markup.Escape(DataverseLabels.String(row, "uniquename")),
                Markup.Escape(DataverseLabels.String(row, "friendlyname")),
                Markup.Escape(DataverseLabels.String(row, "version")),
                row.TryGetProperty("ismanaged", out var im) && im.ValueKind == JsonValueKind.True ? "yes" : "no",
                Markup.Escape(DataverseLabels.String(row, "installedon")));
        }
        AnsiConsole.Write(t);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} solution(s)[/]");
        return 0;
    }
}
