using Evolx.Cli.Ado;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Live;

/// <summary>
/// Integration tests that hit the real osis ADO. Skipped unless the trait is selected:
///   dotnet test --filter Category=Live
///
/// Requires `az login` to be valid. Read-only (no writes) so safe to run repeatedly.
/// </summary>
[Trait("Category", "Live")]
public class AdoLiveTests
{
    private const string Org = "evolx";
    private const string Project = "Evolx.Intern.Microsoft";

    [Fact]
    public async Task ListRepositories_returns_canvas_app_tester()
    {
        using var ado = await AdoClient.CreateAsync(Org, Project);
        var repos = await ado.ListRepositoriesAsync();

        repos.Should().NotBeEmpty();
        repos.Select(r => r.Name).Should().Contain("canvas-app-tester",
            "we created this repo earlier in the project; if missing, the test infrastructure changed");
    }

    [Fact]
    public async Task GetWorkItem_known_id_returns_the_POC_issue()
    {
        // Issue 85 was the POC ticket for canvas-app-tester. State and title are stable.
        using var ado = await AdoClient.CreateAsync(Org, Project);
        var wi = await ado.GetWorkItemAsync(85);

        wi.Id.Should().Be(85);
        wi.Type.Should().Be("Issue");
        wi.State.Should().Be("Done");
        wi.Title.Should().Contain("POC");
    }

    [Fact]
    public async Task QueryWiql_returns_at_least_one_done_issue()
    {
        using var ado = await AdoClient.CreateAsync(Org, Project);
        var items = await ado.QueryAsync(
            "SELECT [System.Id] FROM WorkItems " +
            "WHERE [System.TeamProject] = 'Evolx.Intern.Microsoft' " +
            "AND [System.WorkItemType] = 'Issue' " +
            "AND [System.State] = 'Done' " +
            "ORDER BY [System.Id] DESC");

        items.Should().NotBeEmpty();
        items.All(i => i.Type == "Issue").Should().BeTrue();
        items.All(i => i.State == "Done").Should().BeTrue();
    }
}
