using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Choice;

public sealed class RemoveChoiceCommand : DvCommandBase<RemoveChoiceCommand.Settings>
{
    public sealed class Settings : SchemaRemoveSettings
    {
        [CommandArgument(0, "<SCHEMA-NAME>")]
        public string SchemaName { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (!s.Yes)
        {
            AnsiConsole.MarkupLine("[red]Refusing to delete choice without --yes.[/]");
            return 2;
        }

        if (await dv.TryGetGlobalOptionSetAsync(s.SchemaName, ct) is not { } existing)
        {
            AnsiConsole.MarkupLine($"[yellow]Choice '{Markup.Escape(s.SchemaName)}' does not exist — nothing to do.[/]");
            return 0;
        }

        var metadataId = DataverseLabels.String(existing, "MetadataId");
        await dv.DeleteMetadataAsync($"GlobalOptionSetDefinitions({metadataId})", ct);

        if (await dv.TryGetGlobalOptionSetAsync(s.SchemaName, ct) is not null)
            throw new SchemaMutationDidNotApplyException($"delete choice {s.SchemaName}");

        AnsiConsole.MarkupLine($"[green]Deleted[/] choice [bold]{Markup.Escape(s.SchemaName)}[/]");
        return 0;
    }
}
