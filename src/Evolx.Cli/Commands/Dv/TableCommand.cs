using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class TableCommand : DvCommandBase<TableCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<LOGICAL>")]
        [Description("Table logical name (singular), e.g. evo_tour, account.")]
        public string Logical { get; set; } = "";

        [CommandOption("--json")]
        [Description("Print raw JSON instead of a summary.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var def = await dv.GetEntityDefinitionAsync(s.Logical, ct);

        if (s.Json) { JsonTableRenderer.RenderJson(def); return 0; }

        var header = new Table().Border(TableBorder.Minimal).AddColumns("Field", "Value");
        header.AddRow("LogicalName", Markup.Escape(DataverseLabels.String(def, "LogicalName")));
        header.AddRow("SchemaName", Markup.Escape(DataverseLabels.String(def, "SchemaName")));
        header.AddRow("EntitySetName", Markup.Escape(DataverseLabels.String(def, "EntitySetName")));
        header.AddRow("DisplayName", Markup.Escape(DataverseLabels.LocalizedLabel(def, "DisplayName")));
        AnsiConsole.Write(header);

        if (!def.TryGetProperty("Attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]No Attributes returned.[/]");
            return 0;
        }

        var attrRows = attrs.EnumerateArray()
            .OrderBy(a => DataverseLabels.String(a, "LogicalName"))
            .ToList();

        var t = new Table().Border(TableBorder.Minimal)
            .AddColumns("LogicalName", "Type", "Required", "Create", "Update");
        foreach (var a in attrRows)
        {
            t.AddRow(
                Markup.Escape(DataverseLabels.String(a, "LogicalName")),
                Markup.Escape(DataverseLabels.String(a, "AttributeType")),
                Markup.Escape(DataverseLabels.EnumValue(a, "RequiredLevel")),
                DataverseLabels.Bool(a, "IsValidForCreate") ? "yes" : "no",
                DataverseLabels.Bool(a, "IsValidForUpdate") ? "yes" : "no");
        }
        AnsiConsole.Write(t);
        AnsiConsole.MarkupLine($"[dim]{attrRows.Count} attribute(s)[/]");
        return 0;
    }
}
