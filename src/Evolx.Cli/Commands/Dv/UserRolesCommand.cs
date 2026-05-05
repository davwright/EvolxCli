using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class UserRolesCommand : DvCommandBase<UserRolesCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<USER>")]
        [Description("User: GUID, exact email (containing @), or partial fullname.")]
        public string User { get; set; } = "";

        [CommandOption("--json")]
        [Description("Print raw JSON.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var user = await IdentityResolver.ResolveUserAsync(dv, s.User, ct);
        if (user is null) return 1;

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(user.Value.Label)}[/]  [dim]({user.Value.Id})[/]");

        var roles = await dv.GetUserRolesAsync(user.Value.Id, ct);

        if (s.Json) { JsonTableRenderer.RenderJson(roles); return 0; }

        if (!roles.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]No `value` array on response.[/]");
            return 1;
        }

        var rows = value.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim](no roles assigned)[/]");
            return 0;
        }

        var t = new Table().Border(TableBorder.Minimal).AddColumns("Role", "RoleId", "BusinessUnit");
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
