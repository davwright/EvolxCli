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
            JsonTableRenderer.RenderJson(value);
            return 0;
        }

        var rows = value.EnumerateArray().ToList();
        var explicitColumns = s.Select?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        JsonTableRenderer.Render(rows, explicitColumns);
        return 0;
    }
}
