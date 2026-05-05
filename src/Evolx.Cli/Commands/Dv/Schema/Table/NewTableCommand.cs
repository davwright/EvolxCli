using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Table;

public sealed class NewTableCommand : DvCommandBase<NewTableCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<SCHEMA-NAME>")]
        [Description("Table SchemaName (publisher prefix + name, e.g. evo_demo).")]
        public string SchemaName { get; set; } = "";

        [CommandOption("--display-name <X>")]
        [Description("English DisplayName.")]
        public string? DisplayName { get; set; }

        [CommandOption("--display-name-de <X>")]
        [Description("German DisplayName.")]
        public string? DisplayNameDe { get; set; }

        [CommandOption("--plural-name <X>")]
        [Description("English DisplayCollectionName (plural).")]
        public string? PluralName { get; set; }

        [CommandOption("--plural-name-de <X>")]
        [Description("German DisplayCollectionName.")]
        public string? PluralNameDe { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--description-de <X>")]
        public string? DescriptionDe { get; set; }

        [CommandOption("--primary-field-schema-name <X>")]
        [Description("Primary attribute SchemaName. Default: <prefix>_Name (derived from the table SchemaName).")]
        public string? PrimaryFieldSchemaName { get; set; }

        [CommandOption("--primary-field-display-name <X>")]
        [Description("Primary attribute DisplayName. Default: 'Name'.")]
        public string? PrimaryFieldDisplayName { get; set; }

        [CommandOption("--primary-field-max-length <N>")]
        public int PrimaryFieldMaxLength { get; set; } = 100;

        [CommandOption("--primary-field-required-level <X>")]
        [Description("None | Recommended | ApplicationRequired (default None).")]
        public string PrimaryFieldRequiredLevel { get; set; } = "None";

        [CommandOption("--ownership <X>")]
        [Description("UserOwned | OrganizationOwned (default UserOwned).")]
        public string Ownership { get; set; } = "UserOwned";

        [CommandOption("--activity")]
        public bool Activity { get; set; }

        [CommandOption("--enable-auditing")]
        public bool EnableAuditing { get; set; }

        [CommandOption("--enable-duplicate-detection")]
        public bool EnableDuplicateDetection { get; set; }

        [CommandOption("--enable-offline")]
        public bool EnableOffline { get; set; }

        [CommandOption("--enable-notes")]
        public bool EnableNotes { get; set; }

        [CommandOption("--enable-activities")]
        public bool EnableActivities { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var primarySchema = s.PrimaryFieldSchemaName
                            ?? DerivePrimaryFieldSchemaName(s.SchemaName);
        var primaryDisplay = s.PrimaryFieldDisplayName ?? "Name";

        var primary = new StringAttributeBody
        {
            SchemaName = primarySchema,
            DisplayName = LocalizedLabel.Build(primaryDisplay),
            MaxLength = s.PrimaryFieldMaxLength,
            RequiredLevel = new RequiredLevelBody(s.PrimaryFieldRequiredLevel),
            IsPrimaryName = true,
        };

        var body = new EntityMetadataBody
        {
            SchemaName = s.SchemaName,
            DisplayName = LocalizedLabel.Build(s.DisplayName ?? s.SchemaName, s.DisplayNameDe),
            DisplayCollectionName = LocalizedLabel.Build(s.PluralName ?? s.DisplayName ?? s.SchemaName, s.PluralNameDe),
            Description = LocalizedLabel.Build(s.Description, s.DescriptionDe),
            OwnershipType = s.Ownership,
            IsActivity = s.Activity,
            HasNotes = s.EnableNotes,
            HasActivities = s.EnableActivities,
            IsAuditEnabled = s.EnableAuditing ? true : null,
            IsDuplicateDetectionEnabled = s.EnableDuplicateDetection ? true : null,
            IsAvailableOffline = s.EnableOffline ? true : null,
            PrimaryNameAttribute = primarySchema.ToLowerInvariant(),
            Attributes = new AttributeMetadataBody[] { primary },
        };

        var logical = s.SchemaName.ToLowerInvariant();

        await SilentSkipGuard.RunAsync(
            description: $"create table {s.SchemaName}",
            mutate: () => dv.PostMetadataAsync("EntityDefinitions", body, s.Solution, ct),
            verify: async () => await dv.TryGetEntityDefinitionAsync(logical, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Created[/] table [bold]{Markup.Escape(s.SchemaName)}[/]");

        if (s.Publish)
        {
            await PublishHelper.PublishEntityAsync(dv, logical, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }

    /// <summary>For schema name "evo_demo" the primary attribute conventionally is "evo_Name".</summary>
    private static string DerivePrimaryFieldSchemaName(string tableSchemaName)
    {
        var idx = tableSchemaName.IndexOf('_');
        var prefix = idx > 0 ? tableSchemaName[..idx] : "new";
        return $"{prefix}_Name";
    }
}
