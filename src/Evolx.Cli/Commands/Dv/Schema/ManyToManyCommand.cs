using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema;

public sealed class ManyToManyCommand : DvCommandBase<ManyToManyCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<SCHEMA-NAME>")]
        [Description("Relationship SchemaName.")]
        public string SchemaName { get; set; } = "";

        [CommandOption("--table-a <X>")]
        [Description("Logical name of the first entity in the M:N relationship.")]
        public string TableA { get; set; } = "";

        [CommandOption("--table-b <X>")]
        [Description("Logical name of the second entity.")]
        public string TableB { get; set; } = "";

        [CommandOption("--intersect-name <X>")]
        [Description("Intersect entity name. Default: <SchemaName> lowercased.")]
        public string? IntersectName { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.TableA) || string.IsNullOrWhiteSpace(s.TableB))
            throw new ArgumentException("--table-a and --table-b are both required.");

        var body = new ManyToManyRelationshipBody
        {
            SchemaName = s.SchemaName,
            IntersectEntityName = s.IntersectName ?? s.SchemaName.ToLowerInvariant(),
            Entity1LogicalName = s.TableA,
            Entity2LogicalName = s.TableB,
            Entity1AssociatedMenuConfiguration = new AssociatedMenuConfigurationBody(),
            Entity2AssociatedMenuConfiguration = new AssociatedMenuConfigurationBody(),
        };

        await SilentSkipGuard.RunAsync(
            description: $"create many-to-many {s.SchemaName} ({s.TableA}↔{s.TableB})",
            mutate: () => dv.PostMetadataAsync("RelationshipDefinitions", body, s.Solution, ct),
            verify: async () => await dv.TryGetRelationshipAsync(s.SchemaName, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Created[/] N:N [bold]{Markup.Escape(s.SchemaName)}[/]");

        if (s.Publish)
        {
            // Publish both endpoints — either side may host views/forms that reference the relationship.
            await PublishHelper.PublishEntityAsync(dv, s.TableA, ct);
            await PublishHelper.PublishEntityAsync(dv, s.TableB, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }
}
