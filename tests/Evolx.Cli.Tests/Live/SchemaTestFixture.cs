using Evolx.Cli.Dataverse;
using Xunit;

namespace Evolx.Cli.Tests.Live;

/// <summary>
/// Shared setup/teardown for live schema tests. On <see cref="InitializeAsync"/> sweeps any
/// leftover schema components whose Name starts with the test prefix from the
/// <see cref="TestSolution"/> solution — i.e. anything a previous crashed run left behind.
/// </summary>
public sealed class SchemaTestFixture : IAsyncLifetime
{
    /// <summary>Solution every live schema test scopes its mutations to.</summary>
    public const string TestSolution = "ev_test_delete";

    /// <summary>Schema-name prefix every live schema test uses for tables / columns / choices.</summary>
    public const string Prefix = "evo_evtest_";

    public DvClient Dv { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var url = DvProfile.Resolve(null);
        Dv = await DvClient.CreateAsync(url);

        await SweepLeftoverChoicesAsync();
        await SweepLeftoverTablesAsync();
    }

    public Task DisposeAsync()
    {
        Dv.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Generate a fresh test name with the GUID-suffix used by every test.</summary>
    public static string TestName(string kind) => $"{Prefix}{kind}_{Guid.NewGuid():N}";

    /// <summary>
    /// Best-effort delete of any tables whose LogicalName starts with the prefix.
    /// Failure here is non-fatal — tests can still create their own and clean up at the end.
    /// </summary>
    private async Task SweepLeftoverTablesAsync()
    {
        var defs = await Dv.ListEntityDefinitionsAsync(customOnly: true);
        if (!defs.TryGetProperty("value", out var arr)) return;
        foreach (var def in arr.EnumerateArray())
        {
            var logical = DataverseLabels.String(def, "LogicalName");
            if (!logical.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var metadataId = DataverseLabels.String(def, "MetadataId");
            try { await Dv.DeleteMetadataAsync($"EntityDefinitions({metadataId})"); }
            catch { /* swallow — likely a partial state from an earlier crash */ }
        }
    }

    /// <summary>Sweep leftover global option sets with the test prefix.</summary>
    private async Task SweepLeftoverChoicesAsync()
    {
        var sets = await Dv.GetGlobalOptionSetsAsync(name: null);
        if (!sets.TryGetProperty("value", out var arr)) return;
        foreach (var set in arr.EnumerateArray())
        {
            var name = DataverseLabels.String(set, "Name");
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var metadataId = DataverseLabels.String(set, "MetadataId");
            try { await Dv.DeleteMetadataAsync($"GlobalOptionSetDefinitions({metadataId})"); }
            catch { /* swallow */ }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SchemaTestCollection : ICollectionFixture<SchemaTestFixture>
{
    public const string Name = nameof(SchemaTestCollection);
}
