using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.PullRequest;

public sealed class GetPrCommand : AsyncCommand<GetPrCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("Pull request id.")]
        public int Id { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var pr = await ado.GetPullRequestAsync(s.Id, ct);

        var src = pr.SourceRefName.Replace("refs/heads/", "");
        var tgt = pr.TargetRefName.Replace("refs/heads/", "");

        var table = new Table().Border(TableBorder.Minimal).AddColumns("Field", "Value");
        table.AddRow("Id", pr.PullRequestId.ToString());
        table.AddRow("Repo", Markup.Escape(pr.Repository?.Name ?? ""));
        table.AddRow("Title", Markup.Escape(pr.Title) + (pr.IsDraft ? " [dim](draft)[/]" : ""));
        table.AddRow("By", Markup.Escape(pr.CreatedBy?.DisplayName ?? ""));
        table.AddRow("Branches", Markup.Escape($"{src} -> {tgt}"));
        table.AddRow("Status", pr.Status);
        AnsiConsole.Write(table);

        if (!string.IsNullOrWhiteSpace(pr.Description))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Description[/]");
            AnsiConsole.WriteLine(pr.Description);
        }
        return 0;
    }
}
