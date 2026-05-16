using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Security;

/// <summary>
/// `ev dv table-access &lt;TABLE&gt;` — show roles + their CRUD depth on the table.
///
/// Filters the privilege catalog to <c>prv{Create,Read,Write,Delete}{Table}</c> for the
/// given LogicalName, then resolves each role's depth via roleprivilegescollection.
/// </summary>
public sealed class TableAccessCommand : DvCommandBase<TableAccessCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Table LogicalName (e.g. account, evo_demo).")]
        public string Table { get; set; } = "";

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Table))
            throw new ArgumentException("Table name is required.");

        // Pull the four CRUD privileges for this table. Dataverse names them
        // prvCreate<Table>, prvRead<Table>, etc.; case-sensitive but we eq-match.
        var actions = new[] { "Create", "Read", "Write", "Delete" };
        var privByAction = new Dictionary<string, (string Id, string Name)>();

        foreach (var action in actions)
        {
            var name = $"prv{action}{s.Table}";
            var match = await dv.GetJsonAsync(
                $"privileges?$select=privilegeid,name&$filter=name eq '{OData.EscapeLiteral(name)}'", ct);
            if (match.TryGetProperty("value", out var arr)
                && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0)
            {
                privByAction[action] = (DataverseLabels.String(arr[0], "privilegeid"), name);
            }
        }

        if (privByAction.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No CRUD privileges found for table '{Markup.Escape(s.Table)}'.[/]");
            AnsiConsole.MarkupLine("[dim]Possible causes: misspelled LogicalName (try evo_demo not evo_Demo), or the table has no privileges.[/]");
            return 1;
        }

        // For each privilege, walk the role assignments via roleprivilegescollection.
        // Aggregate to: role -> { Create:depth, Read:depth, Write:depth, Delete:depth }.
        var perRole = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (action, (privId, _)) in privByAction)
        {
            var rows = await dv.GetJsonAsync(
                $"roleprivilegescollection?$select=roleid,privilegedepthmask&$filter=privilegeid eq {privId}", ct);
            if (!rows.TryGetProperty("value", out var arr)) continue;

            foreach (var row in arr.EnumerateArray())
            {
                var roleId = DataverseLabels.String(row, "roleid");
                if (!row.TryGetProperty("privilegedepthmask", out var maskEl) || maskEl.ValueKind != JsonValueKind.Number)
                    continue;
                if (!perRole.TryGetValue(roleId, out var byAction))
                {
                    byAction = new Dictionary<string, int>();
                    perRole[roleId] = byAction;
                }
                byAction[action] = maskEl.GetInt32();
            }
        }

        if (perRole.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]No roles have CRUD privileges for [bold]{Markup.Escape(s.Table)}[/].[/]");
            return 0;
        }

        // Resolve role names (one batch).
        var allRoles = await dv.ListRolesAsync(ct);
        var roleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (allRoles.TryGetProperty("value", out var roleArr))
        {
            foreach (var r in roleArr.EnumerateArray())
                roleNames[DataverseLabels.String(r, "roleid")] = DataverseLabels.String(r, "name");
        }

        if (s.Json)
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(
                perRole.ToDictionary(
                    kv => roleNames.TryGetValue(kv.Key, out var n) ? n : kv.Key,
                    kv => kv.Value.ToDictionary(a => a.Key, a => PrivilegeName.DepthLabel(a.Value))),
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var t = new Table().Border(TableBorder.Minimal)
            .AddColumns("Role", "Create", "Read", "Write", "Delete");
        foreach (var (roleId, byAction) in perRole.OrderBy(kv => roleNames.TryGetValue(kv.Key, out var n) ? n : kv.Key))
        {
            var name = roleNames.TryGetValue(roleId, out var n) ? n : roleId;
            t.AddRow(
                Markup.Escape(name),
                PrivilegeName.DepthLabel(byAction.TryGetValue("Create", out var c) ? c : 0),
                PrivilegeName.DepthLabel(byAction.TryGetValue("Read",   out var r) ? r : 0),
                PrivilegeName.DepthLabel(byAction.TryGetValue("Write",  out var w) ? w : 0),
                PrivilegeName.DepthLabel(byAction.TryGetValue("Delete", out var d) ? d : 0));
        }
        AnsiConsole.Write(t);
        return 0;
    }
}
