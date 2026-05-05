using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema;

public sealed class PolymorphicLookupCommand : DvCommandBase<PolymorphicLookupCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Referencing table LogicalName (the table that gets the lookup column).")]
        public string Table { get; set; } = "";

        [CommandArgument(1, "<SCHEMA-NAME>")]
        [Description("Lookup attribute SchemaName.")]
        public string SchemaName { get; set; } = "";

        [CommandOption("--display-name <X>")]
        public string? DisplayName { get; set; }

        [CommandOption("--display-name-de <X>")]
        public string? DisplayNameDe { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--description-de <X>")]
        public string? DescriptionDe { get; set; }

        [CommandOption("--required-level <X>")]
        public string RequiredLevel { get; set; } = "None";

        [CommandOption("--targets <CSV>")]
        [Description("Comma-separated referenced table LogicalNames.")]
        public string Targets { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var targets = s.Targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targets.Length == 0)
            throw new ArgumentException("--targets is required and must list at least one referenced table.");

        var lookup = new PolymorphicLookupAttributeBody
        {
            SchemaName = s.SchemaName,
            DisplayName = LocalizedLabel.Build(s.DisplayName ?? s.SchemaName, s.DisplayNameDe),
            Description = LocalizedLabel.Build(s.Description, s.DescriptionDe),
            RequiredLevel = new RequiredLevelBody(s.RequiredLevel),
        };

        var relationships = targets.Select(target => new OneToManyRelationshipBody
        {
            SchemaName = $"{s.SchemaName}_{target}",
            ReferencedEntity = target,
            ReferencingEntity = s.Table,
            Lookup = new LookupAttributeBody { SchemaName = s.SchemaName },
            CascadeConfiguration = new CascadeConfigurationBody(),
        }).ToArray();

        var body = new
        {
            OneToManyRelationships = relationships,
            Lookup = lookup,
        };

        var logical = s.SchemaName.ToLowerInvariant();
        await SilentSkipGuard.RunAsync(
            description: $"create polymorphic lookup {s.Table}.{s.SchemaName} → [{string.Join(",", targets)}]",
            mutate: () => dv.InvokeActionAsync("CreatePolymorphicLookupAttribute", body, s.Solution, ct),
            verify: async () => await dv.TryGetAttributeAsync(s.Table, logical, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Created[/] polymorphic lookup [bold]{Markup.Escape(s.Table)}.{Markup.Escape(s.SchemaName)}[/] → {Markup.Escape(string.Join(", ", targets))}");

        if (s.Publish)
        {
            await PublishHelper.PublishEntityAsync(dv, s.Table, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }
}
