using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Security;

/// <summary>
/// `ev dv role privileges set` — grant a privilege to a role at a given depth.
///
/// Uses the AddPrivilegesRole bound action which accepts a list of
/// <c>RolePrivilege</c> records. Depth maps to the standard enum:
/// Basic=1, Local=2, Deep=4, Global=8.
/// </summary>
public sealed class SetRolePrivilegeCommand : DvCommandBase<SetRolePrivilegeCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<ROLE>")]
        [Description("Role name (partial match) or roleid GUID.")]
        public string Role { get; set; } = "";

        [CommandOption("--privilege <X>")]
        [Description("Privilege name (e.g. prvCreateAccount).")]
        public string Privilege { get; set; } = "";

        [CommandOption("--depth <X>")]
        [Description("Basic | Local | Deep | Global.")]
        public string Depth { get; set; } = "Basic";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var role = await IdentityResolver.ResolveRoleAsync(dv, s.Role, ct);
        if (role is null) return 1;

        if (!PrivilegeName.TryParseDepth(s.Depth, out var depthValue))
            throw new ArgumentException($"--depth must be Basic|Local|Deep|Global (got '{s.Depth}').");

        // Look up the privilege id from name.
        var match = await dv.GetJsonAsync(
            $"privileges?$select=privilegeid,name&$filter=name eq '{OData.EscapeLiteral(s.Privilege)}'", ct);
        if (!match.TryGetProperty("value", out var arr)
            || arr.ValueKind != System.Text.Json.JsonValueKind.Array
            || arr.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Privilege '{s.Privilege}' not found.");
        }
        var privId = DataverseLabels.String(arr[0], "privilegeid");

        var body = new
        {
            Privileges = new[]
            {
                new
                {
                    PrivilegeId = privId,
                    Depth = depthValue,
                }
            }
        };
        await dv.InvokeActionAsync($"roles({role.Value.Id})/Microsoft.Dynamics.CRM.AddPrivilegesRole", body, ct: ct);

        AnsiConsole.MarkupLine(
            $"[green]Set[/] privilege [bold]{Markup.Escape(s.Privilege)}[/] " +
            $"at depth [bold]{Markup.Escape(s.Depth)}[/] on role [bold]{Markup.Escape(role.Value.Label)}[/].");
        return 0;
    }
}
