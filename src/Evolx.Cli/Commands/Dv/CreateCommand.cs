using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Entity set name, e.g. evo_tours.")]
        public string Table { get; set; } = "";

        [CommandOption("--json <BODY>")]
        [Description("JSON body. Use literal JSON or @path/to/file.json to read from disk.")]
        public string Json { get; set; } = "";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Json))
        {
            AnsiConsole.MarkupLine("[red]--json <body> is required.[/]");
            return 2;
        }

        var body = s.Json.StartsWith('@')
            ? await File.ReadAllTextAsync(s.Json[1..], ct)
            : s.Json;

        string envUrl;
        try { envUrl = DvProfile.Resolve(s.EnvUrl); }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }

        using var dv = await DvClient.CreateAsync(envUrl, ct);
        JsonElement created;
        try
        {
            created = await dv.CreateAsync(s.Table, body, ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        // Pull out the primary id (the column named <table-singular>id) — best-effort.
        // For evo_tours -> evo_tourid; for systemusers -> systemuserid; etc.
        var idCol = GuessIdColumn(s.Table);
        var id = created.TryGetProperty(idCol, out var idEl) ? idEl.GetString() : null;

        AnsiConsole.MarkupLine($"[green]Created[/] in [bold]{Markup.Escape(s.Table)}[/]");
        if (id != null) AnsiConsole.MarkupLine($"  Id: [cyan]{id}[/]");

        // Also dump a few interesting columns if we can find them.
        foreach (var prop in created.EnumerateObject().Take(8))
        {
            if (prop.Name.StartsWith("@odata", StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.Name == idCol) continue;
            var val = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString() ?? ""
                : prop.Value.ToString();
            if (string.IsNullOrEmpty(val)) continue;
            AnsiConsole.MarkupLine($"  {Markup.Escape(prop.Name)}: {Markup.Escape(val)}");
        }
        return 0;
    }

    /// <summary>
    /// Convention: an entity set's primary key column is the singular table name + "id".
    /// `evo_tours` -> `evo_tourid`; `systemusers` -> `systemuserid`. Falls back to the
    /// plural form if singularization is ambiguous.
    /// </summary>
    private static string GuessIdColumn(string entitySet)
    {
        // Trim a single trailing `s` for the common case. Edge cases (boxes, addresses)
        // exist but aren't worth pulling in pluralizer libs for.
        var singular = entitySet.EndsWith('s') ? entitySet[..^1] : entitySet;
        return singular + "id";
    }
}
