using System.Text.Json;
using Evolx.Cli.Dataverse;
using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class EntityCreateBodyTest
{
    private readonly ITestOutputHelper _out;
    public EntityCreateBodyTest(ITestOutputHelper @out) { _out = @out; }

    [Fact]
    public void Full_create_table_body_has_PrimaryNameAttribute_and_IsPrimaryName_on_attribute()
    {
        var primary = new StringAttributeBody
        {
            SchemaName = "evo_test_Name",
            DisplayName = LocalizedLabel.Build("Name"),
            MaxLength = 100,
            RequiredLevel = new RequiredLevelBody("None"),
            IsPrimaryName = true,
        };

        var body = new EntityMetadataBody
        {
            SchemaName = "evo_test",
            DisplayName = LocalizedLabel.Build("Test"),
            DisplayCollectionName = LocalizedLabel.Build("Tests"),
            OwnershipType = "UserOwned",
            HasNotes = false,
            HasActivities = false,
            PrimaryNameAttribute = "evo_test_name",
            Attributes = new AttributeMetadataBody[] { primary },
        };

        var json = JsonSerializer.Serialize(body, HttpGateway.MetadataJsonOptions);
        _out.WriteLine(json);
        var doc = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("PrimaryNameAttribute").GetString().Should().Be("evo_test_name");
        var attr0 = doc.GetProperty("Attributes").EnumerateArray().First();
        attr0.GetProperty("IsPrimaryName").GetBoolean().Should().BeTrue();
        attr0.GetProperty("SchemaName").GetString().Should().Be("evo_test_Name");
    }
}
