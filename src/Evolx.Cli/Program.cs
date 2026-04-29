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

        // ev ado wi ... (alias for work-item)
        ado.AddBranch("wi", wi =>
        {
            wi.SetDescription("Work item commands.");
            wi.AddCommand<CreateCommand>("create").WithDescription("Create a new work item.");
            wi.AddCommand<CloseCommand>("close").WithDescription("Set state to Done (or another closed state).");
            wi.AddCommand<GetCommand>("get").WithDescription("Show one work item by id.");
            wi.AddCommand<ListCommand>("list").WithDescription("List work items, optionally filtered.");
        });
    });
});

return await app.RunAsync(args);
