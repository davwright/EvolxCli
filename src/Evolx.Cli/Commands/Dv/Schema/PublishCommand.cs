using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema;

public sealed class PublishCommand : DvCommandBase<PublishCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--all")]
        [Description("Publish every customization. Mutually exclusive with --table / --option-set.")]
        public bool All { get; set; }

        [CommandOption("--table <X>")]
        [Description("Table LogicalName(s) to publish. Repeatable.")]
        public string[] Tables { get; set; } = Array.Empty<string>();

        [CommandOption("--option-set <X>")]
        [Description("Global option set name(s) to publish. Repeatable.")]
        public string[] OptionSets { get; set; } = Array.Empty<string>();
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var hasTargeted = s.Tables.Length > 0 || s.OptionSets.Length > 0;
        if (s.All && hasTargeted)
            throw new ArgumentException("--all cannot be combined with --table / --option-set.");

        var xml = (s.All, hasTargeted) switch
        {
            (true, _) => PublishXml.PublishAll(),
            (_, true) => PublishXml.Build(
                entityLogicalNames: s.Tables,
                webResourceIds: Array.Empty<string>(),
                optionSetNames: s.OptionSets),
            _ => throw new ArgumentException("Pass --all, --table, and/or --option-set."),
        };

        await dv.InvokeActionAsync("PublishXml", new PublishXmlBody(xml), ct: ct);

        if (s.All) AnsiConsole.MarkupLine("[green]Published[/] all customizations.");
        else AnsiConsole.MarkupLine($"[green]Published[/] {s.Tables.Length} table(s), {s.OptionSets.Length} option set(s).");
        return 0;
    }
}
