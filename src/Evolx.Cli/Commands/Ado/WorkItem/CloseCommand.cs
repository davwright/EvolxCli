using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.WorkItem;

public sealed class CloseCommand : AsyncCommand<CloseCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandArgument(0, "<IDS>")]
        [Description("One or more work item ids, comma-separated (e.g. 81,82,83).")]
        public string Ids { get; set; } = "";

        [CommandOption("-s|--state <STATE>")]
        [Description("Closed-state name. Default: 'Done' (Basic process). Try 'Closed', 'Resolved', 'Removed' for other processes.")]
        public string State { get; set; } = "Done";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        var ids = s.Ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToArray();
        if (ids.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]No ids provided.[/]");
            return 2;
        }

        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);

        int errors = 0;
        foreach (var id in ids)
        {
            try
            {
                var wi = await ado.SetWorkItemStateAsync(id, s.State, ct);
                AnsiConsole.MarkupLine($"[green]{id}[/] -> [bold]{wi.State}[/]: {Markup.Escape(wi.Title)}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]{id}[/] failed: {Markup.Escape(ex.Message)}");
                errors++;
            }
        }
        return errors == 0 ? 0 : 1;
    }
}
