using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class DataCommand : DvCommandBase<DataCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Entity set name, e.g. evo_sites, accounts.")]
        public string Table { get; set; } = "";

        [CommandOption("--filter <ODATA>")]
        [Description("OData $filter expression.")]
        public string? Filter { get; set; }

        [CommandOption("--select <COLS>")]
        [Description("Comma-separated columns to return.")]
        public string? Select { get; set; }

        [CommandOption("--page-size <N>")]
        [Description("Rows per page (also sets odata.maxpagesize). Default 5000.")]
        public int PageSize { get; set; } = 5000;

        [CommandOption("--all")]
        [Description("Follow @odata.nextLink and accumulate every page.")]
        public bool All { get; set; }

        [CommandOption("--json")]
        [Description("Print raw JSON instead of a table.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        DvClient.PagedResult result;

        // Show a live row count when we're paging through (only useful for --all + non-tiny pages).
        if (s.All && !s.Json)
        {
            result = await AnsiConsole.Status()
                .StartAsync($"Reading {s.Table}…", async ctx =>
                {
                    return await dv.QueryPagedAsync(
                        s.Table, s.Filter, s.Select, s.PageSize, followAll: true,
                        onPage: count => ctx.Status($"Reading {s.Table} — {count} row(s)…"),
                        ct);
                });
        }
        else
        {
            result = await dv.QueryPagedAsync(s.Table, s.Filter, s.Select, s.PageSize, s.All, ct: ct);
        }

        if (s.Json)
        {
            // Emit a single JSON document with the rows array. Use JsonSerializer over a strongly-
            // typed wrapper so the output is structurally valid (no string concat).
            var doc = new { value = result.Rows, hasMore = result.HasMore };
            AnsiConsole.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var explicitColumns = s.Select?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        JsonTableRenderer.Render(result.Rows, explicitColumns);

        if (result.HasMore)
        {
            AnsiConsole.MarkupLine("[yellow]More rows available — re-run with --all to fetch every page.[/]");
        }
        return 0;
    }
}
