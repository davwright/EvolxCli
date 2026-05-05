using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Column;

public sealed class NewColumnCommand : DvCommandBase<NewColumnCommand.Settings>
{
    public sealed class Settings : SchemaSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Table LogicalName the column will belong to.")]
        public string Table { get; set; } = "";

        [CommandArgument(1, "<SCHEMA-NAME>")]
        [Description("Column SchemaName (publisher prefix + name).")]
        public string SchemaName { get; set; } = "";

        [CommandOption("--type <TYPE>")]
        [Description("text | memo | integer | decimal | money | boolean | date | datetime | choice | multi-choice | lookup | customer | image. Use polymorphic-lookup for polymorphic.")]
        public string Type { get; set; } = "text";

        [CommandOption("--display-name <X>")]
        public string? DisplayName { get; set; }

        [CommandOption("--display-name-de <X>")]
        public string? DisplayNameDe { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--description-de <X>")]
        public string? DescriptionDe { get; set; }

        [CommandOption("--required-level <X>")]
        [Description("None | Recommended | ApplicationRequired (default None).")]
        public string RequiredLevel { get; set; } = "None";

        [CommandOption("--max-length <N>")]
        [Description("text/memo: max length.")]
        public int? MaxLength { get; set; }

        [CommandOption("--precision <N>")]
        [Description("decimal/money: decimal precision.")]
        public int? Precision { get; set; }

        [CommandOption("--min <X>")]
        [Description("integer/decimal/money: minimum value.")]
        public decimal? Min { get; set; }

        [CommandOption("--max <X>")]
        [Description("integer/decimal/money: maximum value.")]
        public decimal? Max { get; set; }

        [CommandOption("--true-label <X>")]
        public string? TrueLabel { get; set; }

        [CommandOption("--false-label <X>")]
        public string? FalseLabel { get; set; }

        [CommandOption("--choices <X>")]
        [Description("choice/multi-choice: semicolon-separated EN labels.")]
        public string? Choices { get; set; }

        [CommandOption("--choices-de <X>")]
        [Description("choice/multi-choice: semicolon-separated DE labels (must match count of --choices).")]
        public string? ChoicesDe { get; set; }

        [CommandOption("--global-option-set <X>")]
        [Description("choice/multi-choice: bind to an existing global option set by Name.")]
        public string? GlobalOptionSet { get; set; }

        [CommandOption("--target <X>")]
        [Description("lookup: referenced table LogicalName.")]
        public string? Target { get; set; }

        [CommandOption("--max-size-kb <N>")]
        [Description("image: max size in KB.")]
        public int? MaxSizeKb { get; set; }

        [CommandOption("--can-store-full-image")]
        public bool CanStoreFullImage { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var (path, body) = ColumnTypeBodies.Build(s.Table, s.Type, s);
        var logical = ColumnTypeBodies.LogicalName(s.SchemaName);

        await SilentSkipGuard.RunAsync(
            description: $"create column {s.Table}.{s.SchemaName} ({s.Type})",
            mutate: () => dv.PostMetadataAsync(path, body, s.Solution, ct),
            verify: async () => await dv.TryGetAttributeAsync(s.Table, logical, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Created[/] column [bold]{Markup.Escape(s.Table)}.{Markup.Escape(s.SchemaName)}[/] ({s.Type})");

        if (s.Publish)
        {
            await PublishHelper.PublishEntityAsync(dv, s.Table, ct);
            AnsiConsole.MarkupLine("[green]Published[/]");
        }
        return 0;
    }
}
