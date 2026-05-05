using System.Text.Json;
using Evolx.Cli.Dataverse;
using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

/// <summary>
/// Verifies every metadata body factory serializes to the right Dataverse shape:
/// the @odata.type discriminator, AttributeType enum, and AttributeTypeName subobject
/// must all line up. Catches the easy regression "added a field, forgot the @odata.type".
/// </summary>
public sealed class SchemaBodiesTests
{
    private static string Serialize(object body) =>
        JsonSerializer.Serialize(body, HttpGateway.MetadataJsonOptions);

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void EntityMetadataBody_emits_EntityMetadata_odata_type_and_omits_null_fields()
    {
        var body = new EntityMetadataBody
        {
            SchemaName = "evo_demo",
            DisplayName = LocalizedLabel.Build("Demo"),
            DisplayCollectionName = LocalizedLabel.Build("Demos"),
            OwnershipType = "UserOwned",
        };
        var el = Parse(Serialize(body));

        el.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.EntityMetadata");
        el.GetProperty("SchemaName").GetString().Should().Be("evo_demo");
        // Null props are omitted by the JsonOptions ignore policy.
        el.TryGetProperty("Description", out _).Should().BeFalse();
        el.TryGetProperty("Attributes", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(StringAttributeBody), "StringAttributeMetadata", "String", "StringType")]
    [InlineData(typeof(MemoAttributeBody), "MemoAttributeMetadata", "Memo", "MemoType")]
    [InlineData(typeof(IntegerAttributeBody), "IntegerAttributeMetadata", "Integer", "IntegerType")]
    [InlineData(typeof(DecimalAttributeBody), "DecimalAttributeMetadata", "Decimal", "DecimalType")]
    [InlineData(typeof(MoneyAttributeBody), "MoneyAttributeMetadata", "Money", "MoneyType")]
    [InlineData(typeof(DateTimeAttributeBody), "DateTimeAttributeMetadata", "DateTime", "DateTimeType")]
    [InlineData(typeof(PicklistAttributeBody), "PicklistAttributeMetadata", "Picklist", "PicklistType")]
    [InlineData(typeof(MultiSelectPicklistAttributeBody), "MultiSelectPicklistAttributeMetadata", "Virtual", "MultiSelectPicklistType")]
    [InlineData(typeof(LookupAttributeBody), "LookupAttributeMetadata", "Lookup", "LookupType")]
    [InlineData(typeof(CustomerAttributeBody), "CustomerAttributeMetadata", "Customer", "CustomerType")]
    [InlineData(typeof(ImageAttributeBody), "ImageAttributeMetadata", "Virtual", "ImageType")]
    public void Each_attribute_type_carries_correct_odata_discriminator(
        Type bodyType, string odataLocalName, string attributeType, string typeName)
    {
        // Each AttributeMetadataBody requires SchemaName via init; build via reflection-friendly
        // instantiation of records — all of these use C# 11 required-init properties.
        // Use Activator + ObjectInitializer-equivalent: System.Text.Json's deserialize from a stub.
        var stub = $$"""{"SchemaName":"evo_field"}""";
        var body = JsonSerializer.Deserialize(stub, bodyType, HttpGateway.MetadataJsonOptions)!;

        var el = Parse(Serialize(body));
        el.GetProperty("@odata.type").GetString().Should().Be($"Microsoft.Dynamics.CRM.{odataLocalName}");
        el.GetProperty("AttributeType").GetString().Should().Be(attributeType);
        el.GetProperty("AttributeTypeName").GetProperty("Value").GetString().Should().Be(typeName);
        el.GetProperty("SchemaName").GetString().Should().Be("evo_field");
    }

    [Fact]
    public void BooleanAttributeBody_emits_OptionSet_with_TrueOption_and_FalseOption()
    {
        var body = new BooleanAttributeBody
        {
            SchemaName = "evo_flag",
            OptionSet = new BooleanOptionSet(
                TrueOption: new BooleanOption(1, LocalizedLabel.Build("Yes")!),
                FalseOption: new BooleanOption(0, LocalizedLabel.Build("No")!)),
        };
        var el = Parse(Serialize(body));

        el.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.BooleanAttributeMetadata");
        var os = el.GetProperty("OptionSet");
        os.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.BooleanOptionSetMetadata");
        os.GetProperty("TrueOption").GetProperty("Value").GetInt32().Should().Be(1);
        os.GetProperty("FalseOption").GetProperty("Value").GetInt32().Should().Be(0);
    }

    [Fact]
    public void OptionSetBody_emits_global_picklist_with_options_and_labels()
    {
        var body = new OptionSetBody
        {
            Name = "evo_status",
            DisplayName = LocalizedLabel.Build("Status"),
            Options = new[]
            {
                new OptionMetadataBody(100_000_000, LocalizedLabel.Build("Open", "Offen")!),
                new OptionMetadataBody(100_000_001, LocalizedLabel.Build("Closed")!),
            },
        };
        var el = Parse(Serialize(body));

        el.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.OptionSetMetadata");
        el.GetProperty("IsGlobal").GetBoolean().Should().BeTrue();
        el.GetProperty("OptionSetType").GetString().Should().Be("Picklist");
        var opts = el.GetProperty("Options").EnumerateArray().ToList();
        opts.Should().HaveCount(2);
        opts[0].GetProperty("Value").GetInt32().Should().Be(100_000_000);
        opts[0].GetProperty("Label").GetProperty("LocalizedLabels").EnumerateArray().Should().HaveCount(2);
        opts[1].GetProperty("Label").GetProperty("LocalizedLabels").EnumerateArray().Should().HaveCount(1);
    }

    [Fact]
    public void ManyToManyRelationshipBody_emits_correct_odata_type_and_endpoints()
    {
        var body = new ManyToManyRelationshipBody
        {
            SchemaName = "evo_account_demo",
            IntersectEntityName = "evo_account_demo",
            Entity1LogicalName = "account",
            Entity2LogicalName = "evo_demo",
        };
        var el = Parse(Serialize(body));

        el.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata");
        el.GetProperty("Entity1LogicalName").GetString().Should().Be("account");
        el.GetProperty("Entity2LogicalName").GetString().Should().Be("evo_demo");
    }

    [Fact]
    public void OneToManyRelationshipBody_with_Lookup_inlined_emits_LookupAttributeMetadata()
    {
        var body = new OneToManyRelationshipBody
        {
            SchemaName = "evo_demo_account",
            ReferencedEntity = "account",
            ReferencingEntity = "evo_demo",
            Lookup = new LookupAttributeBody { SchemaName = "evo_account" },
        };
        var el = Parse(Serialize(body));

        el.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata");
        var lookup = el.GetProperty("Lookup");
        lookup.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.LookupAttributeMetadata");
        lookup.GetProperty("AttributeType").GetString().Should().Be("Lookup");
    }

    [Fact]
    public void PicklistAttributeBody_with_GlobalOptionSetBind_emits_odata_bind_property_name()
    {
        var body = new PicklistAttributeBody
        {
            SchemaName = "evo_status_col",
            GlobalOptionSetBind = "GlobalOptionSetDefinitions(Name='evo_status')",
        };
        var el = Parse(Serialize(body));
        // The JsonPropertyName on the C# property maps to "GlobalOptionSet@odata.bind".
        // Read via JsonElement so we don't have to fight System.Text.Json's default
        // safe-escaping of single quotes.
        el.GetProperty("GlobalOptionSet@odata.bind").GetString()
            .Should().Be("GlobalOptionSetDefinitions(Name='evo_status')");
    }

    [Fact]
    public void Polymorphic_lookup_body_emits_ComplexLookupAttributeMetadata_odata_type()
    {
        var body = new PolymorphicLookupAttributeBody { SchemaName = "evo_x" };
        var el = Parse(Serialize(body));
        el.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.ComplexLookupAttributeMetadata");
        el.GetProperty("AttributeType").GetString().Should().Be("Lookup");
    }
}
