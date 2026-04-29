using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.WorkItem;

public sealed class ListCommand : AsyncCommand<ListCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandOption("--state <STATE>")]
        [Description("Filter by state (To Do, Doing, Done).")]
        public string? State { get; set; }

        [CommandOption("--type <TYPE>")]
        [Description("Filter by work item type (Issue, Epic, Bug...).")]
        public string? Type { get; set; }

        [CommandOption("--assigned-to <USER>")]
        [Description("Filter by AssignedTo email or @me for the current user.")]
        public string? AssignedTo { get; set; }

        [CommandOption("--top <N>")]
        [Description("Limit results (default 50).")]
        public int Top { get; set; } = 50;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);

        var clauses = new List<string> { $"[System.TeamProject] = '{s.ResolvedProject.Replace("'", "''")}'" };
        if (!string.IsNullOrWhiteSpace(s.State)) clauses.Add($"[System.State] = '{s.State.Replace("'", "''")}'");
        if (!string.IsNullOrWhiteSpace(s.Type)) clauses.Add($"[System.WorkItemType] = '{s.Type.Replace("'", "''")}'");
        if (!string.IsNullOrWhiteSpace(s.AssignedTo)) clauses.Add($"[System.AssignedTo] = '{s.AssignedTo.Replace("'", "''")}'");

        var where = string.Join(" AND ", clauses);
        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {where} ORDER BY [System.ChangedDate] DESC";

        var items = await ado.QueryAsync(wiql, ct);
        if (items.Count > s.Top) items = items.Take(s.Top).ToList();

        var table = new Table().Border(TableBorder.Minimal)
            .AddColumns("Id", "Type", "State", "Title");
        foreach (var wi in items)
            table.AddRow(wi.Id.ToString(), wi.Type, wi.State, Markup.Escape(wi.Title));

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{items.Count} item(s)[/]");
        return 0;
    }
}
