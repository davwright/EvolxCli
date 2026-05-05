using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class ChoicesCommand : DvCommandBase<ChoicesCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--name <SCHEMA>")]
        [Description("Show options for a single global option set by Name.")]
        public string? Name { get; set; }

        [CommandOption("--json")]
        [Description("Print raw JSON.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var result = await dv.GetGlobalOptionSetsAsync(s.Name, ct);

        if (s.Json) { JsonTableRenderer.RenderJson(result); return 0; }

        if (string.IsNullOrWhiteSpace(s.Name))
        {
            if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                AnsiConsole.MarkupLine("[yellow]Response had no `value` array.[/]");
                return 1;
            }

            var rows = value.EnumerateArray().OrderBy(r => DataverseLabels.String(r, "Name")).ToList();
            var t = new Table().Border(TableBorder.Minimal).AddColumns("Name", "DisplayName", "Type");
            foreach (var r in rows)
            {
                t.AddRow(
                    Markup.Escape(DataverseLabels.String(r, "Name")),
                    Markup.Escape(DataverseLabels.LocalizedLabel(r, "DisplayName")),
                    Markup.Escape(DataverseLabels.String(r, "OptionSetType")));
            }
            AnsiConsole.Write(t);
            AnsiConsole.MarkupLine($"[dim]{rows.Count} option set(s)[/]");
            return 0;
        }

        // Single set view: render its options
        AnsiConsole.MarkupLine(
            $"[bold]{Markup.Escape(DataverseLabels.String(result, "Name"))}[/]" +
            $"  {Markup.Escape(DataverseLabels.LocalizedLabel(result, "DisplayName"))}");

        if (!result.TryGetProperty("Options", out var options) || options.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]No Options array on this set.[/]");
            return 0;
        }

        var optRows = options.EnumerateArray().ToList();
        var ot = new Table().Border(TableBorder.Minimal).AddColumns("Value", "Label", "Description");
        foreach (var o in optRows)
        {
            var value = o.TryGetProperty("Value", out var v) ? v.GetRawText() : "";
            ot.AddRow(
                Markup.Escape(value),
                Markup.Escape(DataverseLabels.LocalizedLabel(o, "Label")),
                Markup.Escape(DataverseLabels.LocalizedLabel(o, "Description")));
        }
        AnsiConsole.Write(ot);
        AnsiConsole.MarkupLine($"[dim]{optRows.Count} option(s)[/]");
        return 0;
    }
}
