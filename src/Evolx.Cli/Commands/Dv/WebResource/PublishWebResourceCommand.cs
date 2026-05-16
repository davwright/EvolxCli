using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.WebResource;

/// <summary>
/// `ev dv webresource publish` — convenience wrapper around <c>PublishXml</c> targeting
/// a single web resource by name. Resolves name → webresourceid, then publishes.
/// </summary>
public sealed class PublishWebResourceCommand : DvCommandBase<PublishWebResourceCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Web resource logical name.")]
        public string Name { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var existing = await dv.TryGetWebResourceAsync(s.Name, ct)
            ?? throw new InvalidOperationException($"Web resource '{s.Name}' not found.");
        var id = DataverseLabels.String(existing, "webresourceid");

        var xml = PublishXml.Build(
            entityLogicalNames: Array.Empty<string>(),
            webResourceIds: new[] { id },
            optionSetNames: Array.Empty<string>());
        await dv.InvokeActionAsync("PublishXml", new PublishXmlBody(xml), ct: ct);

        AnsiConsole.MarkupLine($"[green]Published[/] [bold]{Markup.Escape(s.Name)}[/].");
        return 0;
    }
}
