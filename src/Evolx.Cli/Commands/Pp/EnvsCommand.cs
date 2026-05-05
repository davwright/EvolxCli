using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.PowerPlatform;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Pp;

public sealed class EnvsCommand : AsyncCommand<EnvsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--json")]
        [Description("Print raw JSON.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var bap = await BapClient.CreateAsync(ct);
        var result = await bap.ListEnvironmentsAsync(ct);

        if (s.Json)
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]Response had no `value` array.[/]");
            return 1;
        }

        var rows = value.EnumerateArray().ToList();
        var t = new Table().Border(TableBorder.Minimal)
            .AddColumns("DisplayName", "EnvironmentName", "Region", "Url", "Type");
        foreach (var env in rows)
        {
            var props = env.TryGetProperty("properties", out var p) ? p : default;
            var url = props.ValueKind == JsonValueKind.Object
                && props.TryGetProperty("linkedEnvironmentMetadata", out var lem)
                && lem.ValueKind == JsonValueKind.Object
                && lem.TryGetProperty("instanceUrl", out var iu) && iu.ValueKind == JsonValueKind.String
                ? iu.GetString() ?? ""
                : "";

            t.AddRow(
                Markup.Escape(GetString(props, "displayName")),
                Markup.Escape(GetString(env, "name")),
                Markup.Escape(GetString(props, "azureRegion")),
                Markup.Escape(url),
                Markup.Escape(GetString(props, "environmentSku")));
        }
        AnsiConsole.Write(t);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} environment(s)[/]");
        return 0;
    }

    private static string GetString(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";
}
