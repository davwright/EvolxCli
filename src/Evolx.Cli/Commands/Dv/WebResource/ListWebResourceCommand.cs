using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.WebResource;

/// <summary>`ev dv webresource list` — list web resources, optionally filtered by name substring.</summary>
public sealed class ListWebResourceCommand : DvCommandBase<ListWebResourceCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--name-contains <X>")]
        [Description("Filter to webresources whose name contains this substring.")]
        public string? NameContains { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var result = await dv.ListWebResourcesAsync(s.NameContains, ct);

        if (s.Json) { JsonTableRenderer.RenderJson(result); return 0; }

        if (!result.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]No `value` array.[/]");
            return 1;
        }

        var rows = arr.EnumerateArray().ToList();
        var t = new Table().Border(TableBorder.Minimal)
            .AddColumns("Name", "DisplayName", "Type", "Id");
        foreach (var r in rows)
        {
            t.AddRow(
                Markup.Escape(DataverseLabels.String(r, "name")),
                Markup.Escape(DataverseLabels.String(r, "displayname")),
                r.TryGetProperty("webresourcetype", out var wt) && wt.ValueKind == JsonValueKind.Number
                    ? wt.GetInt32().ToString()
                    : "",
                Markup.Escape(DataverseLabels.String(r, "webresourceid")));
        }
        AnsiConsole.Write(t);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} web resource(s)[/]");
        return 0;
    }
}
