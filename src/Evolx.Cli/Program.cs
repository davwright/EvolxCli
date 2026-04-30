using Evolx.Cli.Commands.Ado.PullRequest;
using Evolx.Cli.Commands.Ado.Repo;
using Evolx.Cli.Commands.Ado.WorkItem;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("ev");

    // Top-level branch: ev ado ...
    config.AddBranch("ado", ado =>
    {
        ado.SetDescription("Azure DevOps verbs (work items, repos, PRs).");

        // ev ado wi ...
        ado.AddBranch("wi", wi =>
        {
            wi.SetDescription("Work item commands.");
            wi.AddCommand<CreateCommand>("create").WithDescription("Create a new work item.");
            wi.AddCommand<CloseCommand>("close").WithDescription("Set state to Done (or another closed state).");
            wi.AddCommand<GetCommand>("get").WithDescription("Show one work item by id.");
            wi.AddCommand<ListCommand>("list").WithDescription("List work items, optionally filtered.");
            wi.AddCommand<CommentCommand>("comment").WithDescription("Add a comment to a work item.");
            wi.AddCommand<LinkCommand>("link").WithDescription("Link two work items (parent/child/related/dependency).");
        });

        // ev ado repo ...
        ado.AddBranch("repo", repo =>
        {
            repo.SetDescription("Git repository commands.");
            repo.AddCommand<ListReposCommand>("list").WithDescription("List repos in the project.");
            repo.AddCommand<CloneRepoCommand>("clone").WithDescription("Clone a repo by name into the current directory.");
        });

        // ev ado pr ...
        ado.AddBranch("pr", pr =>
        {
            pr.SetDescription("Pull request commands.");
            pr.AddCommand<ListPrCommand>("list").WithDescription("List PRs (across project or in one repo).");
            pr.AddCommand<GetPrCommand>("get").WithDescription("Show one PR by id.");
            pr.AddCommand<CreatePrCommand>("create").WithDescription("Open a new PR.");
            pr.AddCommand<CommentPrCommand>("comment").WithDescription("Add a comment to a PR.");
        });
    });
});

return await app.RunAsync(args);
