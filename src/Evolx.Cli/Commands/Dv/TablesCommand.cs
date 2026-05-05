using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class TablesCommand : DvCommandBase<TablesCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--include-system")]
        [Description("Include OOB tables (default: custom only).")]
        public bool IncludeSystem { get; set; }

        [CommandOption("--json")]
        [Description("Print raw JSON.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var result = await dv.ListEntityDefinitionsAsync(customOnly: !s.IncludeSystem, ct);
        if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]Response had no `value` array.[/]");
            return 1;
        }

        var rows = value.EnumerateArray().OrderBy(r => DataverseLabels.String(r, "LogicalName")).ToList();

        if (s.Json) { JsonTableRenderer.RenderJson(value); return 0; }

        var table = new Table().Border(TableBorder.Minimal)
            .AddColumns("LogicalName", "EntitySetName", "DisplayName", "Custom");
        foreach (var row in rows)
        {
            table.AddRow(
                Markup.Escape(DataverseLabels.String(row, "LogicalName")),
                Markup.Escape(DataverseLabels.String(row, "EntitySetName")),
                Markup.Escape(DataverseLabels.LocalizedLabel(row, "DisplayName")),
                DataverseLabels.Bool(row, "IsCustomEntity") ? "yes" : "no");
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} table(s)[/]");
        return 0;
    }
}
