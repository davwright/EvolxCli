using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;

namespace Evolx.Cli.Commands.Dv;

/// <summary>
/// Resolves friendly-string-or-GUID inputs (`role <X>`, `user-roles <Y>`) to a single
/// (id, label) pair. On ambiguity (>1 match) prints the matches and returns null so
/// the caller can exit cleanly. On no-match also returns null with an error.
/// </summary>
internal static class IdentityResolver
{
    /// <summary>Resolve a role by GUID or partial name. Returns null on 0 or &gt;1 matches.</summary>
    public static async Task<(string Id, string Label)?> ResolveRoleAsync(
        DvClient dv, string nameOrId, CancellationToken ct)
    {
        var result = await dv.FindRolesAsync(nameOrId, ct);
        return PickSingle(result, "roleid", "name", entityLabel: "role", input: nameOrId);
    }

    /// <summary>Resolve a user by GUID, exact email, or partial name. Returns null on 0 or &gt;1 matches.</summary>
    public static async Task<(string Id, string Label)?> ResolveUserAsync(
        DvClient dv, string nameOrEmailOrId, CancellationToken ct)
    {
        var result = await dv.FindUsersAsync(nameOrEmailOrId, ct);
        return PickSingle(result, "systemuserid", "fullname", entityLabel: "user", input: nameOrEmailOrId);
    }

    /// <summary>
    /// Pick the single match from a Dataverse response. Handles both "single object" (when caller
    /// passed a GUID and we hit `/entityset(id)`) and "value array" (filter-by-name) shapes.
    /// </summary>
    private static (string Id, string Label)? PickSingle(
        JsonElement result, string idField, string labelField, string entityLabel, string input)
    {
        // Single-object form
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty(idField, out var idEl))
        {
            return (
                idEl.GetString() ?? "",
                DataverseLabels.String(result, labelField));
        }

        // Array form
        if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine($"[red]Unexpected response shape for {entityLabel} lookup.[/]");
            return null;
        }

        var rows = value.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]No {entityLabel} matched[/] [yellow]{Markup.Escape(input)}[/]");
            return null;
        }
        if (rows.Count > 1)
        {
            AnsiConsole.MarkupLine($"[red]{rows.Count} {entityLabel}s matched[/] [yellow]{Markup.Escape(input)}[/] — be more specific:");
            foreach (var r in rows.Take(10))
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(DataverseLabels.String(r, labelField))} [dim]({DataverseLabels.String(r, idField)})[/]");
            }
            return null;
        }

        return (
            DataverseLabels.String(rows[0], idField),
            DataverseLabels.String(rows[0], labelField));
    }
}
