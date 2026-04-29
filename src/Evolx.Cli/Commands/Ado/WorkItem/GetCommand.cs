using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.WorkItem;

public sealed class GetCommand : AsyncCommand<GetCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Work item id.")]
        public int Id { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var wi = await ado.GetWorkItemAsync(s.Id, ct);

        var table = new Table().Border(TableBorder.Minimal).AddColumns("Field", "Value");
        table.AddRow("Id", wi.Id.ToString());
        table.AddRow("Type", wi.Type);
        table.AddRow("State", wi.State);
        table.AddRow("Title", Markup.Escape(wi.Title));

        AnsiConsole.Write(table);
        return 0;
    }
}
