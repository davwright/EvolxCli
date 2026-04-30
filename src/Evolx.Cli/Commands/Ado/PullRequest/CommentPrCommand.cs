using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.PullRequest;

public sealed class CommentPrCommand : AsyncCommand<CommentPrCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandOption("--repo <NAME>")]
        [Description("Repo name (required).")]
        public string Repo { get; set; } = "";

        [CommandArgument(0, "<ID>")]
        [Description("Pull request id.")]
        public int Id { get; set; }

        [CommandArgument(1, "<TEXT>")]
        [Description("Comment text (markdown).")]
        public string Text { get; set; } = "";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Repo))
        {
            AnsiConsole.MarkupLine("[red]--repo is required.[/]");
            return 2;
        }

        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var threadId = await ado.AddPullRequestCommentAsync(s.Repo, s.Id, s.Text, ct);
        AnsiConsole.MarkupLine($"[green]Thread {threadId}[/] added to PR {s.Id}.");
        return 0;
    }
}
