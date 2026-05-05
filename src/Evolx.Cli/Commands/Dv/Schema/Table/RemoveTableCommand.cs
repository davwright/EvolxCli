using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Table;

public sealed class RemoveTableCommand : DvCommandBase<RemoveTableCommand.Settings>
{
    public sealed class Settings : SchemaRemoveSettings
    {
        [CommandArgument(0, "<LOGICAL>")]
        [Description("Table LogicalName.")]
        public string Logical { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (!s.Yes)
        {
            AnsiConsole.MarkupLine($"[red]Refusing to delete table without --yes.[/]");
            return 2;
        }

        if (await dv.TryGetEntityDefinitionAsync(s.Logical, ct) is not { } existing)
        {
            AnsiConsole.MarkupLine($"[yellow]Table '{Markup.Escape(s.Logical)}' does not exist — nothing to do.[/]");
            return 0;
        }

        var metadataId = DataverseLabels.String(existing, "MetadataId");
        await dv.DeleteMetadataAsync($"EntityDefinitions({metadataId})", ct);

        // Verify the deletion landed.
        if (await dv.TryGetEntityDefinitionAsync(s.Logical, ct) is not null)
            throw new SchemaMutationDidNotApplyException($"delete table {s.Logical}");

        AnsiConsole.MarkupLine($"[green]Deleted[/] table [bold]{Markup.Escape(s.Logical)}[/]");
        return 0;
    }
}
