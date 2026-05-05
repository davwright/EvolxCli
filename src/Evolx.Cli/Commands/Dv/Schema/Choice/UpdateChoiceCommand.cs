using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Choice;

/// <summary>
/// Updates label / description / options on an existing global option set. Adding a new
/// option goes through the <c>InsertOptionValue</c> action; updating an existing label
/// uses <c>UpdateOptionValue</c>. Both keep the option's numeric Value stable.
/// </summary>
public sealed class UpdateChoiceCommand : DvCommandBase<UpdateChoiceCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<SCHEMA-NAME>")]
        public string SchemaName { get; set; } = "";

        [CommandOption("--display-name <X>")]
        public string? DisplayName { get; set; }

        [CommandOption("--display-name-de <X>")]
        public string? DisplayNameDe { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--description-de <X>")]
        public string? DescriptionDe { get; set; }

        [CommandOption("--add-option <X>")]
        [Description("Add a new option with the given EN label. Repeatable.")]
        public string[] AddOption { get; set; } = Array.Empty<string>();

        [CommandOption("--add-option-de <X>")]
        [Description("DE labels for newly-added options (must match --add-option count).")]
        public string[] AddOptionDe { get; set; } = Array.Empty<string>();
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (await dv.TryGetGlobalOptionSetAsync(s.SchemaName, ct) is not { } existing)
            throw new InvalidOperationException($"Global option set '{s.SchemaName}' not found.");

        // PUT label/description updates if any
        var labelChanged = !string.IsNullOrWhiteSpace(s.DisplayName) || !string.IsNullOrWhiteSpace(s.DisplayNameDe)
                        || !string.IsNullOrWhiteSpace(s.Description) || !string.IsNullOrWhiteSpace(s.DescriptionDe);
        if (labelChanged)
        {
            var body = new System.Text.Json.Nodes.JsonObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadata",
                ["Name"] = s.SchemaName,
                ["IsGlobal"] = true,
                ["OptionSetType"] = "Picklist",
                ["HasChanged"] = true,
            };
            var displayLabel = LocalizedLabel.Build(s.DisplayName, s.DisplayNameDe);
            if (displayLabel is not null)
                body["DisplayName"] = System.Text.Json.JsonSerializer.SerializeToNode(displayLabel, Evolx.Cli.Http.HttpGateway.MetadataJsonOptions);
            var descLabel = LocalizedLabel.Build(s.Description, s.DescriptionDe);
            if (descLabel is not null)
                body["Description"] = System.Text.Json.JsonSerializer.SerializeToNode(descLabel, Evolx.Cli.Http.HttpGateway.MetadataJsonOptions);

            await dv.PutMetadataAsync(
                $"GlobalOptionSetDefinitions(Name='{OData.EscapeLiteral(s.SchemaName)}')",
                body, s.Solution, ct);
            AnsiConsole.MarkupLine($"[green]Updated labels on[/] [bold]{Markup.Escape(s.SchemaName)}[/]");
        }

        // Insert new options (one action call per new option — Dataverse doesn't accept a batch here)
        if (s.AddOption.Length > 0)
        {
            if (s.AddOptionDe.Length > 0 && s.AddOptionDe.Length != s.AddOption.Length)
                throw new ArgumentException("--add-option-de count must match --add-option count.");

            // Find the highest existing Value so new options don't collide.
            int nextValue = 100_000_000;
            if (existing.TryGetProperty("Options", out var optionsArr)
                && optionsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var opt in optionsArr.EnumerateArray())
                {
                    if (opt.TryGetProperty("Value", out var v) && v.TryGetInt32(out var intVal) && intVal >= nextValue)
                        nextValue = intVal + 1;
                }
            }

            for (int i = 0; i < s.AddOption.Length; i++)
            {
                var label = LocalizedLabel.Build(s.AddOption[i], i < s.AddOptionDe.Length ? s.AddOptionDe[i] : null)!;
                var insertBody = new
                {
                    OptionSetName = s.SchemaName,
                    Value = nextValue + i,
                    Label = label,
                };
                await dv.InvokeActionAsync("InsertOptionValue", insertBody, s.Solution, ct);
                AnsiConsole.MarkupLine($"  [green]+[/] {Markup.Escape(s.AddOption[i])} ({nextValue + i})");
            }
        }

        if (s.Publish)
        {
            await PublishHelper.PublishOptionSetAsync(dv, s.SchemaName, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }
}
