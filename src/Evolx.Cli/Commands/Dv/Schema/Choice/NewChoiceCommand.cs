using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Choice;

public sealed class NewChoiceCommand : DvCommandBase<NewChoiceCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<SCHEMA-NAME>")]
        [Description("Global option set Name (publisher prefix + name).")]
        public string SchemaName { get; set; } = "";

        [CommandOption("--display-name <X>")]
        public string? DisplayName { get; set; }

        [CommandOption("--display-name-de <X>")]
        public string? DisplayNameDe { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--description-de <X>")]
        public string? DescriptionDe { get; set; }

        [CommandOption("--options <X>")]
        [Description("Semicolon-separated EN labels.")]
        public string Options { get; set; } = "";

        [CommandOption("--options-de <X>")]
        [Description("Semicolon-separated DE labels (must match count of --options).")]
        public string? OptionsDe { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var en = s.Options.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var de = s.OptionsDe?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 ?? Array.Empty<string>();
        if (en.Length == 0)
            throw new ArgumentException("--options is required and must contain at least one entry.");
        if (de.Length > 0 && de.Length != en.Length)
            throw new ArgumentException($"--options ({en.Length}) and --options-de ({de.Length}) must have the same number of entries.");

        var options = new OptionMetadataBody[en.Length];
        for (int i = 0; i < en.Length; i++)
        {
            var label = LocalizedLabel.Build(en[i], i < de.Length ? de[i] : null)!;
            options[i] = new OptionMetadataBody(Value: 100_000_000 + i, Label: label);
        }

        var body = new OptionSetBody
        {
            Name = s.SchemaName,
            IsGlobal = true,
            DisplayName = LocalizedLabel.Build(s.DisplayName ?? s.SchemaName, s.DisplayNameDe),
            Description = LocalizedLabel.Build(s.Description, s.DescriptionDe),
            Options = options,
        };

        await SilentSkipGuard.RunAsync(
            description: $"create choice {s.SchemaName}",
            mutate: () => dv.PostMetadataAsync("GlobalOptionSetDefinitions", body, s.Solution, ct),
            verify: async () => await dv.TryGetGlobalOptionSetAsync(s.SchemaName, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Created[/] choice [bold]{Markup.Escape(s.SchemaName)}[/] ({en.Length} option(s))");

        if (s.Publish)
        {
            await PublishHelper.PublishOptionSetAsync(dv, s.SchemaName, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }
}
