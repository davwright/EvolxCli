using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.WorkItem;

public sealed class CreateCommand : AsyncCommand<CreateCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandArgument(0, "<TYPE>")]
        [Description("Work item type, e.g. Issue, Epic, Task, Bug.")]
        public string Type { get; set; } = "";

        [CommandArgument(1, "<TITLE>")]
        [Description("Work item title.")]
        public string Title { get; set; } = "";

        [CommandOption("-d|--description <TEXT>")]
        [Description("HTML or plain-text description.")]
        public string? Description { get; set; }

        [CommandOption("--parent <ID>")]
        [Description("Parent work item id (creates a hierarchy link).")]
        public int? ParentId { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var wi = await ado.CreateWorkItemAsync(s.Type, s.Title, s.Description, s.ParentId, ct: ct);

        AnsiConsole.MarkupLine($"[green]Created[/] {s.Type} [bold]{wi.Id}[/]: {Markup.Escape(wi.Title)}");
        AnsiConsole.MarkupLine($"  [dim]https://dev.azure.com/{s.ResolvedOrganization}/{s.ResolvedProject}/_workitems/edit/{wi.Id}[/]");
        return 0;
    }
}
