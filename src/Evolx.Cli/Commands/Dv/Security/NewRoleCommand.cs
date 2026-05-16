using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Security;

/// <summary>
/// `ev dv role new` — create a new security role in a business unit.
/// </summary>
public sealed class NewRoleCommand : DvCommandBase<NewRoleCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--name <X>")]
        [Description("Role name. Required.")]
        public string Name { get; set; } = "";

        [CommandOption("--businessunit <NAME-OR-ID>")]
        [Description("Business unit name or id. Default: the env's root BU.")]
        public string? BusinessUnit { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
            throw new ArgumentException("--name is required.");

        var buId = await ResolveBusinessUnitIdAsync(dv, s.BusinessUnit, ct);

        // Roles are created via POST /roles; the BU is bound via OData reference.
        var body = new Dictionary<string, object?>
        {
            ["name"] = s.Name,
            ["businessunitid@odata.bind"] = $"/businessunits({buId})",
        };
        var json = JsonSerializer.Serialize(body, Http.HttpGateway.JsonOptions);

        await SilentSkipGuard.RunAsync(
            description: $"create role {s.Name}",
            mutate: () => dv.CreateAsync("roles", json, ct),
            verify: async () =>
            {
                var match = await dv.FindRolesAsync(s.Name, ct);
                if (!match.TryGetProperty("value", out var arr)) return false;
                foreach (var r in arr.EnumerateArray())
                {
                    if (string.Equals(DataverseLabels.String(r, "name"), s.Name, StringComparison.Ordinal))
                        return true;
                }
                return false;
            });

        AnsiConsole.MarkupLine($"[green]Created[/] role [bold]{Markup.Escape(s.Name)}[/].");
        return 0;
    }

    /// <summary>
    /// Resolve a business unit identifier from a name, an exact id, or null (which
    /// means "use the root BU"). The root BU is the one with no parentbusinessunitid.
    /// </summary>
    internal static async Task<string> ResolveBusinessUnitIdAsync(DvClient dv, string? nameOrId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nameOrId))
        {
            var rootQs = "businessunits?$select=businessunitid,name&$filter=parentbusinessunitid eq null";
            var root = await dv.GetJsonAsync(rootQs, ct);
            if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var bu in arr.EnumerateArray())
                    return DataverseLabels.String(bu, "businessunitid");
            }
            throw new InvalidOperationException("Could not resolve the root business unit.");
        }

        if (Guid.TryParse(nameOrId, out _)) return nameOrId;

        var match = await dv.GetJsonAsync(
            $"businessunits?$select=businessunitid,name&$filter=name eq '{OData.EscapeLiteral(nameOrId)}'", ct);
        if (match.TryGetProperty("value", out var arr2) && arr2.ValueKind == JsonValueKind.Array)
        {
            foreach (var bu in arr2.EnumerateArray())
                return DataverseLabels.String(bu, "businessunitid");
        }
        throw new InvalidOperationException($"Business unit '{nameOrId}' not found.");
    }
}
