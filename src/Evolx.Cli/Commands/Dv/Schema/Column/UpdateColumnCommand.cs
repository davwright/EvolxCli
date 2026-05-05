using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Column;

public sealed class UpdateColumnCommand : DvCommandBase<UpdateColumnCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<TABLE>")]
        public string Table { get; set; } = "";

        [CommandArgument(1, "<COLUMN>")]
        [Description("Column LogicalName.")]
        public string Column { get; set; } = "";

        [CommandOption("--display-name <X>")]
        public string? DisplayName { get; set; }

        [CommandOption("--display-name-de <X>")]
        public string? DisplayNameDe { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--description-de <X>")]
        public string? DescriptionDe { get; set; }

        [CommandOption("--required-level <X>")]
        public string? RequiredLevel { get; set; }

        [CommandOption("--max-length <N>")]
        public int? MaxLength { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var existingNullable = await dv.TryGetAttributeAsync(s.Table, s.Column, ct);
        if (existingNullable is not { } existing)
            throw new InvalidOperationException($"Column '{s.Table}.{s.Column}' not found.");

        // Preserve the original @odata.type so Dataverse routes the PUT to the right
        // metadata handler (StringAttributeMetadata/MemoAttributeMetadata/...).
        var odataType = existing.TryGetProperty("@odata.type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? throw new InvalidOperationException("Existing column metadata missing @odata.type.")
            : throw new InvalidOperationException("Existing column metadata missing @odata.type.");

        var schemaName = DataverseLabels.String(existing, "SchemaName");
        var metadataId = DataverseLabels.String(existing, "MetadataId");

        // Build a partial body using a JsonObject — every column type shares these few
        // fields, and we don't want to enumerate the full per-type DTO just to update labels.
        // Going through System.Text.Json keeps this structurally typed; no string concat.
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["@odata.type"] = odataType,
            ["SchemaName"] = schemaName,
            ["MetadataId"] = metadataId,
            ["HasChanged"] = true,
        };
        AddLabel(body, "DisplayName", s.DisplayName, s.DisplayNameDe);
        AddLabel(body, "Description", s.Description, s.DescriptionDe);
        if (!string.IsNullOrWhiteSpace(s.RequiredLevel))
            body["RequiredLevel"] = System.Text.Json.JsonSerializer.SerializeToNode(
                new RequiredLevelBody(s.RequiredLevel), Evolx.Cli.Http.HttpGateway.MetadataJsonOptions);
        if (s.MaxLength is { } len) body["MaxLength"] = len;

        await SilentSkipGuard.RunAsync(
            description: $"update column {s.Table}.{s.Column}",
            mutate: () => dv.PutMetadataAsync(
                $"EntityDefinitions(LogicalName='{OData.EscapeLiteral(s.Table)}')/Attributes(LogicalName='{OData.EscapeLiteral(s.Column)}')",
                body, s.Solution, ct),
            verify: async () => await dv.TryGetAttributeAsync(s.Table, s.Column, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Updated[/] column [bold]{Markup.Escape(s.Table)}.{Markup.Escape(s.Column)}[/]");

        if (s.Publish)
        {
            await PublishHelper.PublishEntityAsync(dv, s.Table, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }

    private static void AddLabel(System.Text.Json.Nodes.JsonObject body, string name, string? en, string? de)
    {
        var label = LocalizedLabel.Build(en, de);
        if (label is null) return;
        body[name] = System.Text.Json.JsonSerializer.SerializeToNode(label, Evolx.Cli.Http.HttpGateway.MetadataJsonOptions);
    }
}
