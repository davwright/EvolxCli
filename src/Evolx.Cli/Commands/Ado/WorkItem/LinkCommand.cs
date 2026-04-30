using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.WorkItem;

public sealed class LinkCommand : AsyncCommand<LinkCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandArgument(0, "<SOURCE>")]
        [Description("Source work item id (the link is added on this item).")]
        public int SourceId { get; set; }

        [CommandArgument(1, "<TARGET>")]
        [Description("Target work item id.")]
        public int TargetId { get; set; }

        [CommandOption("--rel <REL>")]
        [Description("Link type: parent | child | related (default) | dependency-of | depends-on. Or pass a full System.LinkTypes.* string.")]
        public string Rel { get; set; } = "related";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        var rel = ResolveRel(s.Rel);
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        await ado.LinkWorkItemsAsync(s.SourceId, s.TargetId, rel, ct);
        AnsiConsole.MarkupLine($"[green]Linked[/] {s.SourceId} -> {s.TargetId} as [bold]{rel}[/].");
        return 0;
    }

    private static string ResolveRel(string alias) => alias.ToLowerInvariant() switch
    {
        "parent" => "System.LinkTypes.Hierarchy-Reverse",
        "child" => "System.LinkTypes.Hierarchy-Forward",
        "related" => "System.LinkTypes.Related",
        "depends-on" => "System.LinkTypes.Dependency-Reverse",
        "dependency-of" => "System.LinkTypes.Dependency-Forward",
        _ => alias,
    };
}
