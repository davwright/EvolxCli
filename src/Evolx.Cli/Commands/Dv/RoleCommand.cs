using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class RoleCommand : DvCommandBase<RoleCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<NAME-OR-ID>")]
        [Description("Role name (partial match) or roleid GUID.")]
        public string NameOrId { get; set; } = "";

        [CommandOption("--privileges")]
        [Description("Also list privileges with action / table / depth.")]
        public bool Privileges { get; set; }

        [CommandOption("--json")]
        [Description("Print raw JSON for the privileges call.")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var role = await IdentityResolver.ResolveRoleAsync(dv, s.NameOrId, ct);
        if (role is null) return 1;

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(role.Value.Label)}[/]  [dim]({role.Value.Id})[/]");

        if (!s.Privileges) return 0;

        var privsTask = dv.GetRolePrivilegesAsync(role.Value.Id, ct);
        var depthsTask = dv.GetRolePrivilegeDepthsAsync(role.Value.Id, ct);
        await Task.WhenAll(privsTask, depthsTask);
        var privs = await privsTask;
        var depths = await depthsTask;

        if (s.Json)
        {
            var doc = new { privileges = privs, depths = depths };
            AnsiConsole.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var depthByPriv = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (depths.TryGetProperty("value", out var depthArr) && depthArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in depthArr.EnumerateArray())
            {
                var pid = DataverseLabels.String(d, "privilegeid");
                if (string.IsNullOrEmpty(pid)) continue;
                depthByPriv[pid] = d.TryGetProperty("privilegedepthmask", out var m) && m.ValueKind == JsonValueKind.Number
                    ? m.GetInt32()
                    : 0;
            }
        }

        if (!privs.TryGetProperty("value", out var privArr) || privArr.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]No privileges array.[/]");
            return 0;
        }

        var rows = privArr.EnumerateArray()
            .Select(p =>
            {
                var name = DataverseLabels.String(p, "name");
                var id = DataverseLabels.String(p, "privilegeid");
                var (action, table) = PrivilegeName.Split(name);
                depthByPriv.TryGetValue(id, out var mask);
                return (Action: action, Table: table, Depth: mask);
            })
            .OrderBy(r => r.Table)
            .ThenBy(r => r.Action)
            .ToList();

        var t = new Table().Border(TableBorder.Minimal).AddColumns("Table", "Action", "Depth");
        foreach (var r in rows)
        {
            t.AddRow(
                Markup.Escape(r.Table),
                Markup.Escape(r.Action),
                Markup.Escape(PrivilegeName.DepthLabel(r.Depth)));
        }
        AnsiConsole.Write(t);
        AnsiConsole.MarkupLine($"[dim]{rows.Count} privilege(s)[/]");
        return 0;
    }
}
