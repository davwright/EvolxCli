using System.ComponentModel;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.PullRequest;

public sealed class CreatePrCommand : AsyncCommand<CreatePrCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandOption("--repo <NAME>")]
        [Description("Repo name (required).")]
        public string Repo { get; set; } = "";

        [CommandOption("--source <BRANCH>")]
        [Description("Source branch (the branch with your changes). 'refs/heads/' prefix is added if missing.")]
        public string Source { get; set; } = "";

        [CommandOption("--target <BRANCH>")]
        [Description("Target branch (default: main).")]
        public string Target { get; set; } = "main";

        [CommandOption("--title <TEXT>")]
        [Description("PR title (required).")]
        public string Title { get; set; } = "";

        [CommandOption("--description <TEXT>")]
        [Description("PR description (markdown).")]
        public string? Description { get; set; }

        [CommandOption("--draft")]
        [Description("Open as a draft PR.")]
        public bool Draft { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Repo) || string.IsNullOrWhiteSpace(s.Source) || string.IsNullOrWhiteSpace(s.Title))
        {
            AnsiConsole.MarkupLine("[red]--repo, --source and --title are required.[/]");
            return 2;
        }

        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var pr = await ado.CreatePullRequestAsync(s.Repo, s.Source, s.Target, s.Title, s.Description, s.Draft, ct);

        AnsiConsole.MarkupLine($"[green]Created PR {pr.PullRequestId}[/]: {Markup.Escape(pr.Title)}");
        AnsiConsole.MarkupLine($"  [dim]https://dev.azure.com/{s.ResolvedOrganization}/{s.ResolvedProject}/_git/{Uri.EscapeDataString(s.Repo)}/pullrequest/{pr.PullRequestId}[/]");
        return 0;
    }
}
