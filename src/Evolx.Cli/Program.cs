using Evolx.Cli;
using Evolx.Cli.Auth;
using Evolx.Cli.Commands.Ado.PullRequest;
using Evolx.Cli.Commands.Ado.Repo;
using Evolx.Cli.Commands.Ado.WorkItem;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Http;
using Spectre.Console;
using Spectre.Console.Cli;


// One-line banner with version + description, on stderr. Skipped for `--help`-only invocations.
Banner.Print();

// Keep tenant inactivity clocks alive — runs at most once every 7 days, silent otherwise.
// Most invocations skip out in <1ms after a marker-file stat. The weekly run is awaited
// so the child az processes don't get killed when ev exits. See Auth/Keepalive.cs.
await Keepalive.RunIfDueAsync();

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("ev");

    // Make Spectre propagate exceptions to our top-level handler instead of
    // collapsing them to "Error: <message>". HttpFailure has rich detail we want
    // the user to see (URL, body, headers, attempts).
    config.PropagateExceptions();

    // Top-level branch: ev ado ...
    config.AddBranch("ado", ado =>
    {
        ado.SetDescription("Azure DevOps verbs (work items, repos, PRs).");

        // ev ado wi ...
        ado.AddBranch("wi", wi =>
        {
            wi.SetDescription("Work item commands.");
            wi.AddCommand<Evolx.Cli.Commands.Ado.WorkItem.CreateCommand>("create").WithDescription("Create a new work item.");
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

    // ev dv ... (Dataverse)
    config.AddBranch("dv", dv =>
    {
        dv.SetDescription("Dataverse verbs (query, create, delete, columns, env binding).");
        dv.AddCommand<ConnectCommand>("connect").WithDescription("Bind this shell to a Dataverse environment.");
        dv.AddCommand<WhoamiCommand>("whoami").WithDescription("Show the bound env + WhoAmI from Dataverse.");
        dv.AddCommand<QueryCommand>("query").WithDescription("OData GET against a table with filter/select/top.");
        dv.AddCommand<DataCommand>("data").WithDescription("Paged GET. --all follows @odata.nextLink.");
        dv.AddCommand<Evolx.Cli.Commands.Dv.CreateCommand>("create").WithDescription("POST a new row from JSON.");
        dv.AddCommand<UpdateCommand>("update").WithDescription("PATCH an existing row from JSON.");
        dv.AddCommand<DeleteCommand>("delete").WithDescription("DELETE a row by id.");
        dv.AddCommand<ColumnsCommand>("columns").WithDescription("List columns + types for a table.");
        dv.AddCommand<TablesCommand>("tables").WithDescription("List entity definitions.");
        dv.AddCommand<TableCommand>("table").WithDescription("Full metadata for one table.");
        dv.AddCommand<ChoicesCommand>("choices").WithDescription("List global option sets; --name expands one.");
        dv.AddCommand<MetadataCommand>("metadata").WithDescription("Download $metadata CSDL XML.");
        dv.AddCommand<RolesCommand>("roles").WithDescription("List security roles.");
        dv.AddCommand<RoleCommand>("role").WithDescription("Show one role; --privileges expands it.");
        dv.AddCommand<UserRolesCommand>("user-roles").WithDescription("Roles assigned to a user.");

        // ev dv schema ... — schema mutation
        dv.AddBranch("schema", schema =>
        {
            schema.SetDescription("Schema mutation: tables, columns, choices, relationships, publish.");

            schema.AddBranch("table", table =>
            {
                table.SetDescription("Table (entity) verbs.");
                table.AddCommand<Evolx.Cli.Commands.Dv.Schema.Table.NewTableCommand>("new").WithDescription("Create a new table.");
                table.AddCommand<Evolx.Cli.Commands.Dv.Schema.Table.UpdateTableCommand>("update").WithDescription("Update an existing table.");
                table.AddCommand<Evolx.Cli.Commands.Dv.Schema.Table.RemoveTableCommand>("remove").WithDescription("Delete a table (--yes required).");
            });

            schema.AddBranch("column", column =>
            {
                column.SetDescription("Column (attribute) verbs.");
                column.AddCommand<Evolx.Cli.Commands.Dv.Schema.Column.NewColumnCommand>("new").WithDescription("Create a column. --type dispatches to the right metadata shape.");
                column.AddCommand<Evolx.Cli.Commands.Dv.Schema.Column.UpdateColumnCommand>("update").WithDescription("Update labels / required level / max-length on a column.");
                column.AddCommand<Evolx.Cli.Commands.Dv.Schema.Column.RemoveColumnCommand>("remove").WithDescription("Delete a column (--yes required).");
                column.AddCommand<Evolx.Cli.Commands.Dv.Schema.Column.CopyColumnCommand>("copy").WithDescription("Copy a column's values from one env to another.");
            });

            schema.AddBranch("choice", choice =>
            {
                choice.SetDescription("Global option set (choice) verbs.");
                choice.AddCommand<Evolx.Cli.Commands.Dv.Schema.Choice.NewChoiceCommand>("new").WithDescription("Create a global option set.");
                choice.AddCommand<Evolx.Cli.Commands.Dv.Schema.Choice.UpdateChoiceCommand>("update").WithDescription("Update labels or add new options.");
                choice.AddCommand<Evolx.Cli.Commands.Dv.Schema.Choice.RemoveChoiceCommand>("remove").WithDescription("Delete a global option set (--yes required).");
            });

            schema.AddCommand<Evolx.Cli.Commands.Dv.Schema.ManyToManyCommand>("many-to-many").WithDescription("Create an N:N relationship.");
            schema.AddCommand<Evolx.Cli.Commands.Dv.Schema.PolymorphicLookupCommand>("polymorphic-lookup").WithDescription("Create a polymorphic lookup column.");
            schema.AddCommand<Evolx.Cli.Commands.Dv.Schema.PublishCommand>("publish").WithDescription("Publish entities and/or option sets.");
        });

        // ev dv solution ... — solution lifecycle via direct REST (not pac).
        dv.AddBranch("solution", solution =>
        {
            solution.SetDescription("Solution lifecycle: new/list/export/import/import-status/publish/remove-component.");
            solution.AddCommand<Evolx.Cli.Commands.Dv.Solution.NewSolutionCommand>("new").WithDescription("Create a new unmanaged solution.");
            solution.AddCommand<Evolx.Cli.Commands.Dv.Solution.ListSolutionCommand>("list").WithDescription("List solutions.");
            solution.AddCommand<Evolx.Cli.Commands.Dv.Solution.ExportSolutionCommand>("export").WithDescription("Export a solution to .zip.");
            solution.AddCommand<Evolx.Cli.Commands.Dv.Solution.ImportSolutionCommand>("import").WithDescription("Import a solution .zip and wait for completion.");
            solution.AddCommand<Evolx.Cli.Commands.Dv.Solution.ImportStatusCommand>("import-status").WithDescription("Show or watch an import job's progress.");
            solution.AddCommand<Evolx.Cli.Commands.Dv.Solution.PublishSolutionCommand>("publish").WithDescription("Publish customizations (all or by component).");
            solution.AddCommand<Evolx.Cli.Commands.Dv.Solution.RemoveComponentCommand>("remove-component").WithDescription("Remove a component from a solution.");
        });

        // ev dv webresource ... — web resource read/push/publish.
        dv.AddBranch("webresource", wr =>
        {
            wr.SetDescription("Web resource verbs: list, push (create/update), publish.");
            wr.AddCommand<Evolx.Cli.Commands.Dv.WebResource.ListWebResourceCommand>("list").WithDescription("List web resources.");
            wr.AddCommand<Evolx.Cli.Commands.Dv.WebResource.PushWebResourceCommand>("push").WithDescription("Push a local file as a web resource (create or update).");
            wr.AddCommand<Evolx.Cli.Commands.Dv.WebResource.PublishWebResourceCommand>("publish").WithDescription("Publish one web resource by name.");
        });

        // ev dv plugin ... — plugin registration read + manifest-driven sync.
        dv.AddBranch("plugin", plugin =>
        {
            plugin.SetDescription("Plugin registration verbs: list and (manifest-driven) sync.");
            plugin.AddCommand<Evolx.Cli.Commands.Dv.Plugin.ListPluginCommand>("list").WithDescription("List plugin assemblies / types / steps.");
            plugin.AddCommand<Evolx.Cli.Commands.Dv.Plugin.SyncPluginCommand>("sync").WithDescription("Diff a plugin manifest vs Dataverse and print the plan.");
        });

        // ev dv user-role ... — assign / unassign roles to users.
        dv.AddBranch("user-role", ur =>
        {
            ur.SetDescription("User-role assignments: assign / unassign.");
            ur.AddCommand<Evolx.Cli.Commands.Dv.Security.AssignUserRoleCommand>("assign").WithDescription("Assign a role to a user.");
            ur.AddCommand<Evolx.Cli.Commands.Dv.Security.UnassignUserRoleCommand>("unassign").WithDescription("Remove a role from a user.");
        });

        // ev dv table-access ... — CRUD privileges by table (show/set).
        dv.AddBranch("table-access", ta =>
        {
            ta.SetDescription("CRUD privilege view + grant for a table.");
            ta.AddCommand<Evolx.Cli.Commands.Dv.Security.TableAccessCommand>("show").WithDescription("Show roles + CRUD depth on a table.");
            ta.AddCommand<Evolx.Cli.Commands.Dv.Security.SetTableAccessCommand>("set").WithDescription("Grant CRUD privileges on a table to a role.");
        });

        // Role mutation verbs. The existing leaf `role <name>` (show one role) stays as-is —
        // these mutating verbs sit alongside it.
        dv.AddCommand<Evolx.Cli.Commands.Dv.Security.NewRoleCommand>("role-new")
            .WithDescription("Create a new security role.");
        dv.AddCommand<Evolx.Cli.Commands.Dv.Security.SetRolePrivilegeCommand>("role-set-privilege")
            .WithDescription("Grant a privilege at a depth to a role.");
    });

    // ev pp ... (Power Platform admin)
    config.AddBranch("pp", pp =>
    {
        pp.SetDescription("Power Platform admin verbs.");
        pp.AddCommand<Evolx.Cli.Commands.Pp.EnvsCommand>("envs").WithDescription("List Power Platform environments (BAP).");
    });

    // ev canvas ... (canvas-app local file ops; the one place we delegate to `pac`)
    config.AddBranch("canvas", canvas =>
    {
        canvas.SetDescription("Canvas App verbs. Pack/unpack delegate to `pac`; no `az` needed.");
        canvas.AddCommand<Evolx.Cli.Commands.Canvas.PackCommand>("pack").WithDescription("Pack a source directory into a .msapp via pac.");
        canvas.AddCommand<Evolx.Cli.Commands.Canvas.UnpackCommand>("unpack").WithDescription("Unpack a .msapp into source files via pac.");
    });
});

// Run the command. Catch HttpFailure at the boundary to render its rich
// diagnostic context (URL, status, response body, headers); other exceptions
// get the default Spectre treatment. Either way, non-zero exit on failure —
// no try/catch/continue inside the runtime.
try
{
    return await app.RunAsync(args);
}
catch (HttpFailure http)
{
    Console.Error.Write(http.ToString());
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
