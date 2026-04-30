using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.WorkItem;

public sealed class CommentCommand : AsyncCommand<CommentCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Work item id.")]
        public int Id { get; set; }

        [CommandArgument(1, "<TEXT>")]
        [Description("Comment text. Can be HTML.")]
        public string Text { get; set; } = "";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var commentId = await ado.AddCommentAsync(s.Id, s.Text, ct);
        AnsiConsole.MarkupLine($"[green]Comment {commentId}[/] added to work item {s.Id}.");
        return 0;
    }
}
