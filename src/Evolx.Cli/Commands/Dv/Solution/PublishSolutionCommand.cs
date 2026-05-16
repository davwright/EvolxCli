using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Solution;

/// <summary>
/// `ev dv solution publish` — convenience wrapper around <c>PublishXml</c>.
///
/// Without args (or with <c>--all</c>): publish-all-customizations.
/// With <c>--component</c>: each value is <c>kind:identifier</c>, e.g.
/// <c>entity:account</c>, <c>webresource:00000000-...</c>, <c>optionset:my_choice</c>,
/// <c>sitemap</c>, <c>ribbon</c>. Mirrors the existing <c>ev dv schema publish</c>
/// surface but lives here too for discoverability under the solution branch.
/// </summary>
public sealed class PublishSolutionCommand : DvCommandBase<PublishSolutionCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--all")]
        [Description("Publish all customizations (the default if no --component is given).")]
        public bool All { get; set; }

        [CommandOption("--component <X>")]
        [Description("Component to publish: 'entity:logicalname', 'optionset:name', 'webresource:GUID', 'sitemap', 'ribbon'. Repeatable.")]
        public string[] Components { get; set; } = Array.Empty<string>();
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (s.Components.Length == 0 || s.All)
        {
            await dv.InvokeActionAsync("PublishXml", new PublishXmlBody(PublishXml.PublishAll()), ct: ct);
            AnsiConsole.MarkupLine("[green]Published[/] all customizations.");
            return 0;
        }

        var entities = new List<string>();
        var optionSets = new List<string>();
        var webResources = new List<string>();
        bool siteMap = false, ribbon = false;
        foreach (var c in s.Components)
        {
            var colon = c.IndexOf(':');
            var kind = colon < 0 ? c : c[..colon];
            var value = colon < 0 ? "" : c[(colon + 1)..];

            switch (kind.ToLowerInvariant())
            {
                case "entity": entities.Add(value); break;
                case "optionset": optionSets.Add(value); break;
                case "webresource": webResources.Add(value); break;
                case "sitemap": siteMap = true; break;
                case "ribbon": ribbon = true; break;
                default:
                    throw new ArgumentException(
                        $"Unknown --component kind '{kind}'. Use entity / optionset / webresource / sitemap / ribbon.");
            }
        }

        var xml = PublishXml.Build(entities, webResources, optionSets,
            dashboardIds: null, siteMap: siteMap, ribbon: ribbon);
        await dv.InvokeActionAsync("PublishXml", new PublishXmlBody(xml), ct: ct);

        AnsiConsole.MarkupLine(
            $"[green]Published[/] {entities.Count} entity, " +
            $"{optionSets.Count} optionset, {webResources.Count} webresource" +
            (siteMap ? " + sitemap" : "") + (ribbon ? " + ribbon" : "") + ".");
        return 0;
    }
}
