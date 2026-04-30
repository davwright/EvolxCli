using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class ColumnsCommand : AsyncCommand<ColumnsCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Table logical name (singular), e.g. evo_tour, account.")]
        public string Table { get; set; } = "";

        [CommandOption("--custom-only")]
        [Description("Only show custom columns (those starting with the publisher prefix).")]
        public bool CustomOnly { get; set; }

        [CommandOption("--required")]
        [Description("Only show required columns.")]
        public bool RequiredOnly { get; set; }

        [CommandOption("--type <TYPE>")]
        [Description("Filter to a single attribute type, e.g. Lookup, String, Picklist.")]
        public string? Type { get; set; }

        [CommandOption("--json")]
        [Description("Emit raw JSON instead of a table.")]
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
            result = await dv.GetEntityAttributesAsync(s.Table, ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        if (!result.TryGetProperty("value", out var value))
        {
            AnsiConsole.MarkupLine("[yellow]No `value` array in response.[/]");
            return 1;
        }

        var rows = value.EnumerateArray()
            .Where(a => MatchesFilters(a, s))
            .ToList();

        if (s.Json)
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal)
            .AddColumns("LogicalName", "Type", "Required", "DisplayName");
        foreach (var a in rows)
        {
            var name = a.TryGetProperty("LogicalName", out var n) ? n.GetString() ?? "" : "";
            var type = a.TryGetProperty("AttributeType", out var t) ? t.GetString() ?? "" : "";
            var req = a.TryGetProperty("RequiredLevel", out var r) && r.TryGetProperty("Value", out var rv)
                ? rv.GetString() ?? "" : "";
            var disp = a.TryGetProperty("DisplayName", out var d)
                && d.TryGetProperty("UserLocalizedLabel", out var ull)
                && ull.ValueKind != JsonValueKind.Null
                && ull.TryGetProperty("Label", out var lbl)
                ? lbl.GetString() ?? "" : "";

            table.AddRow(Markup.Escape(name), Markup.Escape(type), Markup.Escape(req), Markup.Escape(disp));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} column(s)[/]");
        return 0;
    }

    private static bool MatchesFilters(JsonElement attr, Settings s)
    {
        if (s.CustomOnly)
        {
            if (!attr.TryGetProperty("IsCustomAttribute", out var c) || !c.GetBoolean()) return false;
        }
        if (s.RequiredOnly)
        {
            // Required = "ApplicationRequired" or "SystemRequired"
            var lvl = attr.TryGetProperty("RequiredLevel", out var r) && r.TryGetProperty("Value", out var rv)
                ? rv.GetString() : null;
            if (lvl != "ApplicationRequired" && lvl != "SystemRequired") return false;
        }
        if (!string.IsNullOrWhiteSpace(s.Type))
        {
            var type = attr.TryGetProperty("AttributeType", out var t) ? t.GetString() : null;
            if (!string.Equals(type, s.Type, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
