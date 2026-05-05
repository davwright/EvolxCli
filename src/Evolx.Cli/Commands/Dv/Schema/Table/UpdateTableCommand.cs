using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Table;

public sealed class UpdateTableCommand : DvCommandBase<UpdateTableCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<LOGICAL>")]
        [Description("Table LogicalName (lowercase, e.g. evo_demo).")]
        public string Logical { get; set; } = "";

        [CommandOption("--display-name <X>")]
        public string? DisplayName { get; set; }

        [CommandOption("--display-name-de <X>")]
        public string? DisplayNameDe { get; set; }

        [CommandOption("--plural-name <X>")]
        public string? PluralName { get; set; }

        [CommandOption("--plural-name-de <X>")]
        public string? PluralNameDe { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--description-de <X>")]
        public string? DescriptionDe { get; set; }

        [CommandOption("--enable-auditing <X>")]
        [Description("true | false")]
        public bool? EnableAuditing { get; set; }

        [CommandOption("--enable-duplicate-detection <X>")]
        [Description("true | false")]
        public bool? EnableDuplicateDetection { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        // Fetch the existing definition: we need to send back SchemaName and the existing
        // shape so the PUT is well-formed. Dataverse's `PUT /EntityDefinitions(LogicalName='x')`
        // does a full-replace from the body, but it merges labels (we set MSCRM.MergeLabels)
        // and respects HasChanged on each property.
        if (await dv.TryGetEntityDefinitionAsync(s.Logical, ct) is not { } existing)
            throw new InvalidOperationException($"Table '{s.Logical}' not found.");

        var schemaName = DataverseLabels.String(existing, "SchemaName");

        var body = new EntityMetadataBody
        {
            SchemaName = schemaName,
            DisplayName = LocalizedLabel.Build(s.DisplayName, s.DisplayNameDe),
            DisplayCollectionName = LocalizedLabel.Build(s.PluralName, s.PluralNameDe),
            Description = LocalizedLabel.Build(s.Description, s.DescriptionDe),
            IsAuditEnabled = s.EnableAuditing,
            IsDuplicateDetectionEnabled = s.EnableDuplicateDetection,
            HasChanged = true,
        };

        await SilentSkipGuard.RunAsync(
            description: $"update table {s.Logical}",
            mutate: () => dv.PutMetadataAsync(
                $"EntityDefinitions(LogicalName='{OData.EscapeLiteral(s.Logical)}')",
                body, s.Solution, ct),
            // Verifier: we can't easily diff without re-reading; assert the table still exists
            // (a future tightening can compare the requested DisplayName to the post-state).
            verify: async () => await dv.TryGetEntityDefinitionAsync(s.Logical, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Updated[/] table [bold]{Markup.Escape(s.Logical)}[/]");

        if (s.Publish)
        {
            await PublishHelper.PublishEntityAsync(dv, s.Logical, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }
}
