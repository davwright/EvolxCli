using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.Repo;

public sealed class ListReposCommand : AsyncCommand<ListReposCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandOption("--filter <SUBSTR>")]
        [Description("Case-insensitive substring filter on repo name.")]
        public string? Filter { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var repos = await ado.ListRepositoriesAsync(ct);

        var filtered = string.IsNullOrWhiteSpace(s.Filter)
            ? repos
            : repos.Where(r => r.Name.Contains(s.Filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var table = new Table().Border(TableBorder.Minimal)
            .AddColumns("Name", "Default Branch", "Clone URL");
        foreach (var r in filtered.OrderBy(x => x.Name))
        {
            var branch = r.DefaultBranch?.Replace("refs/heads/", "") ?? "-";
            table.AddRow(Markup.Escape(r.Name), Markup.Escape(branch), Markup.Escape(r.RemoteUrl ?? r.Url ?? ""));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{filtered.Count} repo(s)[/]");
        return 0;
    }
}
