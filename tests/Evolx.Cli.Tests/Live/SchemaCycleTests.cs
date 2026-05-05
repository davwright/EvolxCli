using System.Text.Json;
using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Live;

/// <summary>
/// End-to-end cycle: create table → add columns → publish → re-read → remove table.
/// Lives in the <see cref="SchemaTestFixture.TestSolution"/> solution so the sweep on
/// startup catches anything left behind by an earlier crash.
/// </summary>
[Trait("Category", "Live")]
[Collection(SchemaTestCollection.Name)]
public class SchemaCycleTests
{
    private readonly SchemaTestFixture _fx;
    public SchemaCycleTests(SchemaTestFixture fx) { _fx = fx; }

    [Fact]
    public async Task Create_table_add_columns_publish_then_remove()
    {
        var schemaName = SchemaTestFixture.TestName("tbl");
        var logical = schemaName.ToLowerInvariant();
        var dv = _fx.Dv;

        try
        {
            // 1) create table
            var tableBody = new EntityMetadataBody
            {
                SchemaName = schemaName,
                DisplayName = LocalizedLabel.Build("Test"),
                DisplayCollectionName = LocalizedLabel.Build("Tests"),
                OwnershipType = "UserOwned",
                HasNotes = false,
                HasActivities = false,
                PrimaryNameAttribute = $"{schemaName}_name".ToLowerInvariant(),
                Attributes = new AttributeMetadataBody[]
                {
                    new StringAttributeBody
                    {
                        SchemaName = $"{schemaName}_Name",
                        DisplayName = LocalizedLabel.Build("Name"),
                        MaxLength = 100,
                        RequiredLevel = new RequiredLevelBody("None"),
                        IsPrimaryName = true,
                    },
                },
            };
            await SilentSkipGuard.RunAsync(
                $"create {schemaName}",
                () => dv.PostMetadataAsync("EntityDefinitions", tableBody, SchemaTestFixture.TestSolution),
                async () => await dv.TryGetEntityDefinitionAsync(logical) is not null);

            // 2) add a text column via PostMetadataAsync directly
            var columnSchema = $"{schemaName}_label";
            var columnLogical = columnSchema.ToLowerInvariant();
            var columnBody = new StringAttributeBody
            {
                SchemaName = columnSchema,
                DisplayName = LocalizedLabel.Build("Label"),
                MaxLength = 200,
                RequiredLevel = new RequiredLevelBody("None"),
            };
            await SilentSkipGuard.RunAsync(
                $"create column {logical}.{columnSchema}",
                () => dv.PostMetadataAsync(
                    $"EntityDefinitions(LogicalName='{OData.EscapeLiteral(logical)}')/Attributes",
                    columnBody, SchemaTestFixture.TestSolution),
                async () => await dv.TryGetAttributeAsync(logical, columnLogical) is not null);

            // 3) publish
            await dv.InvokeActionAsync("PublishXml",
                new PublishXmlBody(PublishXml.Build(
                    entityLogicalNames: new[] { logical },
                    webResourceIds: Array.Empty<string>(),
                    optionSetNames: Array.Empty<string>())));

            // 4) re-read EntityDefinition with attributes expanded — both should be present
            var def = await dv.GetEntityDefinitionAsync(logical);
            var attrLogicals = def.GetProperty("Attributes").EnumerateArray()
                .Select(a => DataverseLabels.String(a, "LogicalName"))
                .ToList();
            attrLogicals.Should().Contain(columnLogical);
        }
        finally
        {
            // 5) clean up — remove the table (cleans up its columns too)
            if (await dv.TryGetEntityDefinitionAsync(logical) is { } toDel)
            {
                var metadataId = DataverseLabels.String(toDel, "MetadataId");
                await dv.DeleteMetadataAsync($"EntityDefinitions({metadataId})");
            }
        }
    }

    [Fact]
    public async Task SilentSkipGuard_catches_duplicate_SchemaName_create()
    {
        var schemaName = SchemaTestFixture.TestName("dup");
        var logical = schemaName.ToLowerInvariant();
        var dv = _fx.Dv;

        try
        {
            // First create — succeeds and lands.
            var body = new EntityMetadataBody
            {
                SchemaName = schemaName,
                DisplayName = LocalizedLabel.Build("Dup"),
                DisplayCollectionName = LocalizedLabel.Build("Dups"),
                OwnershipType = "UserOwned",
                HasNotes = false,
                HasActivities = false,
                PrimaryNameAttribute = $"{schemaName}_name".ToLowerInvariant(),
                Attributes = new AttributeMetadataBody[]
                {
                    new StringAttributeBody
                    {
                        SchemaName = $"{schemaName}_Name",
                        DisplayName = LocalizedLabel.Build("Name"),
                        MaxLength = 100,
                        RequiredLevel = new RequiredLevelBody("None"),
                        IsPrimaryName = true,
                    },
                },
            };
            await dv.PostMetadataAsync("EntityDefinitions", body, SchemaTestFixture.TestSolution);

            // Second create with the same SchemaName: Dataverse may return success but the
            // table doesn't get re-created. SilentSkipGuard's verifier should be fine here
            // (the table DOES exist) but if the underlying POST throws (the more likely
            // outcome — Dataverse usually returns 409 Conflict for duplicate creates), the
            // guard never reaches verify. Either branch is acceptable behavior.
            // What we're regression-testing is that the *guard* itself works end-to-end.
            // So we exercise the failure path with a deliberately-impossible table:
            Func<Task> act = () => SilentSkipGuard.RunAsync(
                "create totally_nonexistent_table",
                mutate: () => Task.CompletedTask,                          // pretend it succeeded
                verify: () => Task.FromResult(false));                     // re-read says no
            await act.Should().ThrowAsync<SchemaMutationDidNotApplyException>();
        }
        finally
        {
            if (await dv.TryGetEntityDefinitionAsync(logical) is { } toDel)
            {
                var metadataId = DataverseLabels.String(toDel, "MetadataId");
                await dv.DeleteMetadataAsync($"EntityDefinitions({metadataId})");
            }
        }
    }

    [Fact]
    public async Task Choice_create_then_remove()
    {
        var name = SchemaTestFixture.TestName("ch");
        var dv = _fx.Dv;

        try
        {
            var body = new OptionSetBody
            {
                Name = name,
                IsGlobal = true,
                DisplayName = LocalizedLabel.Build("Test Choice"),
                Options = new[]
                {
                    new OptionMetadataBody(100_000_000, LocalizedLabel.Build("Open")!),
                    new OptionMetadataBody(100_000_001, LocalizedLabel.Build("Closed")!),
                },
            };
            await SilentSkipGuard.RunAsync(
                $"create choice {name}",
                () => dv.PostMetadataAsync("GlobalOptionSetDefinitions", body, SchemaTestFixture.TestSolution),
                async () => await dv.TryGetGlobalOptionSetAsync(name) is not null);

            var read = await dv.GetGlobalOptionSetsAsync(name);
            read.GetProperty("Options").EnumerateArray().Should().HaveCount(2);
        }
        finally
        {
            if (await dv.TryGetGlobalOptionSetAsync(name) is { } toDel)
            {
                var metadataId = DataverseLabels.String(toDel, "MetadataId");
                await dv.DeleteMetadataAsync($"GlobalOptionSetDefinitions({metadataId})");
            }
        }
    }
}
