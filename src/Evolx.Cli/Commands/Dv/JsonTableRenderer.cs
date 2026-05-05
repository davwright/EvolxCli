using System.Text.Json;
using Spectre.Console;

namespace Evolx.Cli.Commands.Dv;

/// <summary>
/// Renders a list of OData rows (JSON objects) as a Spectre table. Centralizes the
/// column-pick / odata.* skip / value-formatting logic so every `ev dv` read verb
/// behaves the same when a user does NOT pass --json.
/// </summary>
internal static class JsonTableRenderer
{
    /// <summary>
    /// Render rows. If <paramref name="explicitColumns"/> is null/empty, picks the first
    /// few non-odata.* keys from row 0 (capped by <paramref name="maxAutoColumns"/>).
    /// </summary>
    public static void Render(
        IReadOnlyList<JsonElement> rows,
        IReadOnlyList<string>? explicitColumns = null,
        int maxAutoColumns = 6)
    {
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]0 rows.[/]");
            return;
        }

        var columns = (explicitColumns is { Count: > 0 })
            ? explicitColumns
            : rows[0].EnumerateObject()
                .Where(p => !p.Name.StartsWith("@odata", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .Take(maxAutoColumns)
                .ToList();

        var table = new Table().Border(TableBorder.Minimal);
        foreach (var c in columns) table.AddColumn(c);

        foreach (var row in rows)
        {
            var cells = columns.Select(c => FormatCell(row, c)).ToArray();
            table.AddRow(cells);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} row(s)[/]");
    }

    /// <summary>
    /// Format a single cell value with consistent treatment for null/string/number/bool/object.
    /// Returned string is already Markup-escaped where needed.
    /// </summary>
    public static string FormatCell(JsonElement row, string column)
    {
        if (!row.TryGetProperty(column, out var prop)) return "[dim]-[/]";
        return prop.ValueKind switch
        {
            JsonValueKind.Null => "[dim]null[/]",
            JsonValueKind.String => Markup.Escape(prop.GetString() ?? ""),
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => Markup.Escape(prop.GetRawText()),
        };
    }

    /// <summary>Pretty-print an element as JSON (used when --json is passed).</summary>
    public static void RenderJson(JsonElement element)
    {
        AnsiConsole.WriteLine(JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true }));
    }
}
