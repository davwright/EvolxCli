using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Column;

public sealed class RemoveColumnCommand : DvCommandBase<RemoveColumnCommand.Settings>
{
    public sealed class Settings : SchemaRemoveSettings
    {
        [CommandArgument(0, "<TABLE>")]
        public string Table { get; set; } = "";

        [CommandArgument(1, "<COLUMN>")]
        public string Column { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (!s.Yes)
        {
            AnsiConsole.MarkupLine($"[red]Refusing to delete column without --yes.[/]");
            return 2;
        }

        if (await dv.TryGetAttributeAsync(s.Table, s.Column, ct) is not { } existing)
        {
            AnsiConsole.MarkupLine($"[yellow]Column '{Markup.Escape(s.Table)}.{Markup.Escape(s.Column)}' does not exist — nothing to do.[/]");
            return 0;
        }

        var metadataId = DataverseLabels.String(existing, "MetadataId");
        await dv.DeleteMetadataAsync(
            $"EntityDefinitions(LogicalName='{OData.EscapeLiteral(s.Table)}')/Attributes({metadataId})",
            ct);

        if (await dv.TryGetAttributeAsync(s.Table, s.Column, ct) is not null)
            throw new SchemaMutationDidNotApplyException($"delete column {s.Table}.{s.Column}");

        AnsiConsole.MarkupLine($"[green]Deleted[/] column [bold]{Markup.Escape(s.Table)}.{Markup.Escape(s.Column)}[/]");
        return 0;
    }
}
