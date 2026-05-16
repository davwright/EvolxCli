using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Security;

/// <summary>
/// `ev dv user-role unassign` — remove a role from a user.
/// </summary>
public sealed class UnassignUserRoleCommand : DvCommandBase<UnassignUserRoleCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<USER>")]
        [Description("User: GUID, exact email, or partial fullname.")]
        public string User { get; set; } = "";

        [CommandArgument(1, "<ROLE>")]
        [Description("Role: name (partial) or GUID.")]
        public string Role { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var user = await IdentityResolver.ResolveUserAsync(dv, s.User, ct);
        if (user is null) return 1;
        var role = await IdentityResolver.ResolveRoleAsync(dv, s.Role, ct);
        if (role is null) return 1;

        await dv.DisassociateAsync(
            sourceEntitySet: "systemusers",
            sourceId: user.Value.Id,
            collectionPath: "systemuserroles_association",
            targetEntitySet: "roles",
            targetId: role.Value.Id,
            ct: ct);

        AnsiConsole.MarkupLine(
            $"[green]Unassigned[/] role [bold]{Markup.Escape(role.Value.Label)}[/] " +
            $"from user [bold]{Markup.Escape(user.Value.Label)}[/].");
        return 0;
    }
}
