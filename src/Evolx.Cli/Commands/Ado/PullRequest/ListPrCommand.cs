using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.PullRequest;

public sealed class ListPrCommand : AsyncCommand<ListPrCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandOption("--repo <NAME>")]
        [Description("Repo name. If omitted, lists across the whole project.")]
        public string? Repo { get; set; }

        [CommandOption("--status <STATUS>")]
        [Description("Status: active (default) | abandoned | completed | all.")]
        public string Status { get; set; } = "active";

        [CommandOption("--mine")]
        [Description("Only PRs created by the current user (resolves identity via `az ad signed-in-user`).")]
        public bool Mine { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);

        string? creatorId = null;
        if (s.Mine)
        {
            try { creatorId = await Auth.AzAuth.GetCurrentUserObjectIdAsync(ct); }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Could not resolve current user id ({Markup.Escape(ex.Message)}); listing all.[/]");
            }
        }

        var prs = string.IsNullOrWhiteSpace(s.Repo)
            ? await ado.ListProjectPullRequestsAsync(s.Status, creatorId, ct)
            : await ado.ListPullRequestsAsync(s.Repo, s.Status, creatorId, ct);

        var table = new Table().Border(TableBorder.Minimal)
            .AddColumns("Id", "Repo", "Title", "By", "Source -> Target", "Status");
        foreach (var pr in prs)
        {
            var src = pr.SourceRefName.Replace("refs/heads/", "");
            var tgt = pr.TargetRefName.Replace("refs/heads/", "");
            var by = pr.CreatedBy?.DisplayName ?? "";
            var draft = pr.IsDraft ? " [dim](draft)[/]" : "";
            table.AddRow(
                pr.PullRequestId.ToString(),
                Markup.Escape(pr.Repository?.Name ?? ""),
                Markup.Escape(pr.Title) + draft,
                Markup.Escape(by),
                Markup.Escape($"{src} -> {tgt}"),
                Markup.Escape(pr.Status));
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{prs.Count} PR(s)[/]");
        return 0;
    }
}
