using System.Text.Json;
using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Live;

/// <summary>
/// Read-side integration tests for the new `ev dv` verbs against the bound Dataverse
/// environment. Use OOB shape (account / systemuser / System Administrator / $metadata
/// / global option sets) so they don't depend on tenant-specific data.
///
/// The PATCH (`ev dv update`) live test is deferred to Cluster B, which introduces
/// the throwaway custom table inside the `ev_test_delete` solution. Until then, unit
/// tests in <see cref="Dataverse.DvClientPagingTests"/> cover the PATCH path.
///
/// Run with `dotnet test --filter Category=Live` after `ev dv connect osis-dev.crm4`.
/// </summary>
[Trait("Category", "Live")]
public class DvLiveTests
{
    private static async Task<DvClient> ConnectAsync()
    {
        var url = DvProfile.Resolve(null);
        return await DvClient.CreateAsync(url);
    }

    // -------------------------------------------------------------- Read

    [Fact]
    public async Task Data_paged_read_returns_at_least_one_systemuser()
    {
        using var dv = await ConnectAsync();
        var result = await dv.QueryPagedAsync("systemusers",
            filter: null, select: "systemuserid,fullname",
            pageSize: 5, followAll: false);

        result.Rows.Should().NotBeEmpty();
        result.Rows.First().TryGetProperty("systemuserid", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Tables_returns_account_in_full_list()
    {
        using var dv = await ConnectAsync();
        var result = await dv.ListEntityDefinitionsAsync(customOnly: false);

        result.TryGetProperty("value", out var value).Should().BeTrue();
        value.EnumerateArray()
            .Select(t => DataverseLabels.String(t, "LogicalName"))
            .Should().Contain("account");
    }

    [Fact]
    public async Task Table_for_account_has_attributes_with_required_levels()
    {
        using var dv = await ConnectAsync();
        var def = await dv.GetEntityDefinitionAsync("account");

        DataverseLabels.String(def, "LogicalName").Should().Be("account");
        def.TryGetProperty("Attributes", out var attrs).Should().BeTrue();
        attrs.EnumerateArray()
            .Select(a => DataverseLabels.String(a, "LogicalName"))
            .Should().Contain("name");
    }

    [Fact]
    public async Task Choices_list_returns_global_option_sets()
    {
        using var dv = await ConnectAsync();
        var result = await dv.GetGlobalOptionSetsAsync(name: null);

        result.TryGetProperty("value", out var value).Should().BeTrue();
        value.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Metadata_csdl_starts_with_xml_declaration_and_is_substantial()
    {
        using var dv = await ConnectAsync();
        var bytes = await dv.GetCsdlMetadataAsync();

        bytes.Length.Should().BeGreaterThan(100_000, "real Dataverse $metadata is hundreds of KB");
        var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(50, bytes.Length));
        head.Should().StartWith("<?xml");
    }

    [Fact]
    public async Task Roles_returns_System_Administrator()
    {
        using var dv = await ConnectAsync();
        var result = await dv.ListRolesAsync();

        result.TryGetProperty("value", out var value).Should().BeTrue();
        value.EnumerateArray()
            .Select(r => DataverseLabels.String(r, "name"))
            .Should().Contain("System Administrator");
    }

    [Fact]
    public async Task Role_System_Administrator_has_privileges()
    {
        using var dv = await ConnectAsync();
        var matches = await dv.FindRolesAsync("System Administrator");
        matches.TryGetProperty("value", out var arr).Should().BeTrue();
        var sysAdmin = arr.EnumerateArray()
            .First(r => DataverseLabels.String(r, "name") == "System Administrator");
        var roleId = DataverseLabels.String(sysAdmin, "roleid");

        var privs = await dv.GetRolePrivilegesAsync(roleId);
        privs.TryGetProperty("value", out var pv).Should().BeTrue();
        pv.EnumerateArray().Should().NotBeEmpty("System Administrator has all privileges");
    }

    [Fact]
    public async Task UserRoles_for_signed_in_user_returns_a_value_array()
    {
        using var dv = await ConnectAsync();
        var who = await dv.WhoAmIAsync();
        var userId = who.GetProperty("UserId").GetString()!;

        var roles = await dv.GetUserRolesAsync(userId);
        roles.TryGetProperty("value", out var value).Should().BeTrue();
        value.ValueKind.Should().Be(JsonValueKind.Array);
    }

}
