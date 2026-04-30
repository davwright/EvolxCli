using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class QueryCommand : AsyncCommand<QueryCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Entity set name, e.g. evo_sites, accounts, contacts.")]
        public string Table { get; set; } = "";

        [CommandOption("--filter <ODATA>")]
        [Description("OData $filter, e.g. \"evo_name eq 'Foo'\".")]
        public string? Filter { get; set; }

        [CommandOption("--select <COLS>")]
        [Description("Comma-separated columns, e.g. evo_siteid,evo_name.")]
        public string? Select { get; set; }

        [CommandOption("--orderby <ORDER>")]
        [Description("OData $orderby, e.g. \"createdon desc\".")]
        public string? OrderBy { get; set; }

        [CommandOption("--top <N>")]
        [Description("Limit rows (default 50).")]
        public int Top { get; set; } = 50;

        [CommandOption("--json")]
        [Description("Print raw JSON instead of a table.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        string envUrl;
        try { envUrl = DvProfile.Resolve(s.EnvUrl); }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }

        using var dv = await DvClient.CreateAsync(envUrl, ct);

        JsonElement result;
        try
        {
            result = await dv.QueryAsync(s.Table, s.Filter, s.Select, s.OrderBy, s.Top, ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]Response had no `value` array.[/]");
            return 1;
        }

        if (s.Json)
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        // Table view: pick columns from --select if given, else from the first row's keys
        // (skipping odata.* metadata properties).
        var rows = value.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]0 rows.[/]");
            return 0;
        }

        var columns = s.Select?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                      ?? rows[0].EnumerateObject()
                            .Where(p => !p.Name.StartsWith("@odata", StringComparison.OrdinalIgnoreCase))
                            .Select(p => p.Name)
                            .Take(6) // sane default for "no --select"
                            .ToList();

        var table = new Table().Border(TableBorder.Minimal);
        foreach (var c in columns) table.AddColumn(c);

        foreach (var row in rows)
        {
            var cells = columns.Select(c =>
            {
                if (!row.TryGetProperty(c, out var prop)) return "[dim]-[/]";
                return prop.ValueKind switch
                {
                    JsonValueKind.Null => "[dim]null[/]",
                    JsonValueKind.String => Markup.Escape(prop.GetString() ?? ""),
                    JsonValueKind.Number => prop.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => Markup.Escape(prop.GetRawText()),
                };
            }).ToArray();
            table.AddRow(cells);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} row(s)[/]");
        return 0;
    }
}
