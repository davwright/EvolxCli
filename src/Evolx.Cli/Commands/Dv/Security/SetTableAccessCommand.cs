using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Security;

/// <summary>
/// `ev dv table-access set &lt;TABLE&gt; --role &lt;NAME&gt; --create --read --write --delete --depth &lt;X&gt;` —
/// grant CRUD privileges to a role on a table at a given depth.
///
/// Only the flags passed are granted; nothing implicit. Use one verb per role/depth combination.
/// </summary>
public sealed class SetTableAccessCommand : DvCommandBase<SetTableAccessCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Table LogicalName (e.g. account, evo_demo).")]
        public string Table { get; set; } = "";

        [CommandOption("--role <X>")]
        [Description("Role name (partial match) or roleid GUID.")]
        public string Role { get; set; } = "";

        [CommandOption("--depth <X>")]
        [Description("Basic | Local | Deep | Global (default Basic).")]
        public string Depth { get; set; } = "Basic";

        [CommandOption("--create")] public bool Create { get; set; }
        [CommandOption("--read")] public bool Read { get; set; }
        [CommandOption("--write")] public bool Write { get; set; }
        [CommandOption("--delete")] public bool Delete { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Table)) throw new ArgumentException("Table is required.");
        if (string.IsNullOrWhiteSpace(s.Role)) throw new ArgumentException("--role is required.");

        if (!(s.Create || s.Read || s.Write || s.Delete))
            throw new ArgumentException("Pass one or more of --create, --read, --write, --delete.");

        if (!PrivilegeName.TryParseDepth(s.Depth, out var depthValue))
            throw new ArgumentException($"--depth must be Basic|Local|Deep|Global (got '{s.Depth}').");

        var role = await IdentityResolver.ResolveRoleAsync(dv, s.Role, ct);
        if (role is null) return 1;

        var actions = new List<string>();
        if (s.Create) actions.Add("Create");
        if (s.Read)   actions.Add("Read");
        if (s.Write)  actions.Add("Write");
        if (s.Delete) actions.Add("Delete");

        // Resolve all CRUD privilege ids in one pass.
        var privileges = new List<object>();
        foreach (var action in actions)
        {
            var name = $"prv{action}{s.Table}";
            var match = await dv.GetJsonAsync(
                $"privileges?$select=privilegeid,name&$filter=name eq '{OData.EscapeLiteral(name)}'", ct);
            if (!match.TryGetProperty("value", out var arr)
                || arr.ValueKind != JsonValueKind.Array
                || arr.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    $"Privilege '{name}' not found. Check the table LogicalName casing (Dataverse stores tables lowercase).");
            }
            privileges.Add(new
            {
                PrivilegeId = DataverseLabels.String(arr[0], "privilegeid"),
                Depth = depthValue,
            });
        }

        var body = new { Privileges = privileges.ToArray() };
        await dv.InvokeActionAsync(
            $"roles({role.Value.Id})/Microsoft.Dynamics.CRM.AddPrivilegesRole",
            body, ct: ct);

        AnsiConsole.MarkupLine(
            $"[green]Granted[/] {string.Join("+", actions)} at depth [bold]{Markup.Escape(s.Depth)}[/] " +
            $"on [bold]{Markup.Escape(s.Table)}[/] to role [bold]{Markup.Escape(role.Value.Label)}[/].");
        return 0;
    }
}
