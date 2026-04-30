using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class DeleteCommand : AsyncCommand<DeleteCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Entity set name.")]
        public string Table { get; set; } = "";

        [CommandArgument(1, "<ID>")]
        [Description("Primary key GUID of the row to delete.")]
        public string Id { get; set; } = "";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        string envUrl;
        try { envUrl = DvProfile.Resolve(s.EnvUrl); }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }

        using var dv = await DvClient.CreateAsync(envUrl, ct);
        try
        {
            await dv.DeleteAsync(s.Table, s.Id, ct);
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Deleted[/] {Markup.Escape(s.Table)}({s.Id})");
        return 0;
    }
}
