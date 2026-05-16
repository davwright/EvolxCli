using System.Text.Json;
using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

/// <summary>
/// Diff engine for plugin step sync. The high-value testable surface — the apply
/// phase is REST mechanics; the planning phase is where bugs cost real downtime.
/// </summary>
public class PluginSyncDiffTests
{
    private static PluginManifest MakeManifest(params PluginManifestStep[] steps) => new()
    {
        AssemblyName = "Evolx.Xrm.Plugins",
        AssemblyVersion = "1.0.0.0",
        Types = new[]
        {
            new PluginManifestType { TypeName = "Evolx.Xrm.Plugins.Foo", Steps = steps },
        },
    };

    private static PluginManifestStep MakeStep(string name, int stage = 20, int mode = 0,
        int rank = 1, string filter = "", string config = "") => new()
    {
        StepName = name,
        Message = "Create",
        Entity = "evo_foo",
        Stage = stage,
        Mode = mode,
        Rank = rank,
        FilteredAttributes = filter,
        Configuration = config,
    };

    private static JsonElement MakeCurrentSteps(params (string name, string id, int stage, int mode, int rank, string filter)[] rows)
    {
        var value = rows.Select(r => new
        {
            sdkmessageprocessingstepid = r.id,
            name = r.name,
            stage = r.stage,
            mode = r.mode,
            rank = r.rank,
            filteringattributes = r.filter,
            configuration = "",
            supporteddeployment = 0,
            statecode = 0,
        }).ToArray();
        var json = JsonSerializer.Serialize(new { value });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Empty_state_creates_all()
    {
        var manifest = MakeManifest(MakeStep("S1"), MakeStep("S2"));
        var current = MakeCurrentSteps();

        var diff = PluginSyncDiff.Compute(manifest, current);

        diff.Creates.Should().HaveCount(2);
        diff.Creates.Select(c => c.Step.StepName).Should().BeEquivalentTo(new[] { "S1", "S2" });
        diff.Updates.Should().BeEmpty();
        diff.Deletes.Should().BeEmpty();
    }

    [Fact]
    public void Identical_state_is_no_op()
    {
        var manifest = MakeManifest(MakeStep("S1", stage: 20, mode: 0, rank: 1));
        var current = MakeCurrentSteps(("S1", "11111111-2222-3333-4444-555555555555", 20, 0, 1, ""));

        var diff = PluginSyncDiff.Compute(manifest, current);

        diff.Creates.Should().BeEmpty();
        diff.Updates.Should().BeEmpty();
        diff.Deletes.Should().BeEmpty();
    }

    [Fact]
    public void Stage_change_is_update()
    {
        var manifest = MakeManifest(MakeStep("S1", stage: 40));
        var current = MakeCurrentSteps(("S1", "id-1", 20, 0, 1, ""));

        var diff = PluginSyncDiff.Compute(manifest, current);

        diff.Updates.Should().HaveCount(1);
        diff.Updates[0].StepId.Should().Be("id-1");
        diff.Updates[0].Step.Stage.Should().Be(40);
    }

    [Fact]
    public void Filter_change_is_update()
    {
        var manifest = MakeManifest(MakeStep("S1", filter: "name,statuscode"));
        var current = MakeCurrentSteps(("S1", "id-1", 20, 0, 1, "name"));

        var diff = PluginSyncDiff.Compute(manifest, current);
        diff.Updates.Should().ContainSingle();
    }

    [Fact]
    public void Step_only_in_dataverse_is_delete()
    {
        var manifest = MakeManifest(MakeStep("S1"));
        var current = MakeCurrentSteps(
            ("S1", "id-1", 20, 0, 1, ""),
            ("Orphan", "id-orphan", 20, 0, 1, ""));

        var diff = PluginSyncDiff.Compute(manifest, current);
        diff.Deletes.Should().ContainSingle()
            .Which.Name.Should().Be("Orphan");
    }

    [Fact]
    public void Mixed_changes_are_correctly_classified()
    {
        var manifest = MakeManifest(
            MakeStep("S-create"),               // new
            MakeStep("S-update", stage: 40),    // update
            MakeStep("S-stable"));              // no-op
        var current = MakeCurrentSteps(
            ("S-update", "id-u", 20, 0, 1, ""),
            ("S-stable", "id-s", 20, 0, 1, ""),
            ("S-delete", "id-d", 20, 0, 1, ""));

        var diff = PluginSyncDiff.Compute(manifest, current);
        diff.Creates.Should().ContainSingle().Which.Step.StepName.Should().Be("S-create");
        diff.Updates.Should().ContainSingle().Which.StepId.Should().Be("id-u");
        diff.Deletes.Should().ContainSingle().Which.StepId.Should().Be("id-d");
    }
}
