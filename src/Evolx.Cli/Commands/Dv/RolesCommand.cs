using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class RolesCommand : DvCommandBase<RolesCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--json")]
        [Description("Print raw JSON.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var result = await dv.ListRolesAsync(ct);
        if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]Response had no `value` array.[/]");
            return 1;
        }

        if (s.Json) { JsonTableRenderer.RenderJson(value); return 0; }

        var rows = value.EnumerateArray().ToList();
        var t = new Table().Border(TableBorder.Minimal).AddColumns("Name", "RoleId", "BusinessUnit");
        foreach (var r in rows)
        {
            string buName = r.TryGetProperty("businessunitid", out var bu) && bu.ValueKind == JsonValueKind.Object
                ? DataverseLabels.String(bu, "name")
                : "";
            t.AddRow(
                Markup.Escape(DataverseLabels.String(r, "name")),
                Markup.Escape(DataverseLabels.String(r, "roleid")),
                Markup.Escape(buName));
        }
        AnsiConsole.Write(t);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} role(s)[/]");
        return 0;
    }
}
