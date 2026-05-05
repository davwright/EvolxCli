using System.Text.Json.Serialization;

namespace Evolx.Cli.Dataverse;

// Strongly-typed Dataverse metadata bodies. Every @odata.type discriminator lives here —
// nowhere else in the codebase do we hand-roll a Dataverse metadata JSON shape. Properties
// with the JsonIgnoreCondition.WhenWritingNull policy from HttpGateway.JsonOptions are
// omitted from the serialized body when null, so optional fields can be left unset.

// -------------------------------------------------------------- Entity (Table)

/// <summary>
/// Body for POST /EntityDefinitions and PUT /EntityDefinitions(LogicalName='...').
/// Mirrors Microsoft.Dynamics.CRM.EntityMetadata; only the commonly-used fields are
/// represented here — extend as the verbs grow.
/// </summary>
internal sealed record EntityMetadataBody
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; init; } = "Microsoft.Dynamics.CRM.EntityMetadata";

    public required string SchemaName { get; init; }

    public LocalizedLabelSet? DisplayName { get; init; }
    public LocalizedLabelSet? DisplayCollectionName { get; init; }
    public LocalizedLabelSet? Description { get; init; }

    public string? OwnershipType { get; init; }            // UserOwned | OrganizationOwned
    public bool IsActivity { get; init; }
    public bool HasNotes { get; init; }
    public bool HasActivities { get; init; }
    public bool? IsAuditEnabled { get; init; }
    public bool? IsDuplicateDetectionEnabled { get; init; }
    public bool? IsAvailableOffline { get; init; }

    /// <summary>Required for table create — LogicalName of the primary attribute (lowercased).</summary>
    public string? PrimaryNameAttribute { get; init; }

    /// <summary>Used on create only; ignored on update PUTs.</summary>
    public AttributeMetadataBody[]? Attributes { get; init; }

    /// <summary>Set true on update PUTs to tell Dataverse to apply differences.</summary>
    public bool? HasChanged { get; init; }
}

// -------------------------------------------------------------- Attributes (Columns)

/// <summary>
/// Base for every column body. Concrete subclasses set the right <c>@odata.type</c>
/// discriminator via <see cref="ODataTypeName"/>. The <see cref="JsonDerivedTypeAttribute"/>
/// list tells <c>System.Text.Json</c> to serialize all derived-class properties when an
/// array of base elements is written — without these, only base-declared fields appear
/// in the JSON, which silently drops <c>MaxLength</c>, <c>IsPrimaryName</c>, etc.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(StringAttributeBody))]
[JsonDerivedType(typeof(MemoAttributeBody))]
[JsonDerivedType(typeof(IntegerAttributeBody))]
[JsonDerivedType(typeof(DecimalAttributeBody))]
[JsonDerivedType(typeof(MoneyAttributeBody))]
[JsonDerivedType(typeof(BooleanAttributeBody))]
[JsonDerivedType(typeof(DateTimeAttributeBody))]
[JsonDerivedType(typeof(PicklistAttributeBody))]
[JsonDerivedType(typeof(MultiSelectPicklistAttributeBody))]
[JsonDerivedType(typeof(CustomerAttributeBody))]
[JsonDerivedType(typeof(ImageAttributeBody))]
[JsonDerivedType(typeof(LookupAttributeBody))]
[JsonDerivedType(typeof(PolymorphicLookupAttributeBody))]
internal abstract record AttributeMetadataBody
{
    /// <summary>The Dataverse type discriminator, emitted as <c>@odata.type</c>.</summary>
    [JsonPropertyName("@odata.type")]
    public string ODataType => ODataTypeName;

    /// <summary>Subclasses declare which @odata.type they represent.</summary>
    [JsonIgnore]
    protected abstract string ODataTypeName { get; }

    public required string SchemaName { get; init; }

    public LocalizedLabelSet? DisplayName { get; init; }
    public LocalizedLabelSet? Description { get; init; }
    public RequiredLevelBody? RequiredLevel { get; init; }
    public bool? HasChanged { get; init; }
}

/// <summary>The boxed-int-with-CanBeChanged shape Dataverse uses for the RequiredLevel column.</summary>
internal sealed record RequiredLevelBody(string Value, bool CanBeChanged = true);

internal sealed record StringAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.StringAttributeMetadata";
    public string AttributeType { get; } = "String";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("StringType");
    public int? MaxLength { get; init; }
    public string? FormatName { get; init; }

    /// <summary>Set true on the table's primary-name attribute when it's part of a CreateEntity body.</summary>
    public bool? IsPrimaryName { get; init; }
}

internal sealed record MemoAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.MemoAttributeMetadata";
    public string AttributeType { get; } = "Memo";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("MemoType");
    public int? MaxLength { get; init; }
}

internal sealed record IntegerAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.IntegerAttributeMetadata";
    public string AttributeType { get; } = "Integer";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("IntegerType");
    public int? MinValue { get; init; }
    public int? MaxValue { get; init; }
}

internal sealed record DecimalAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.DecimalAttributeMetadata";
    public string AttributeType { get; } = "Decimal";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("DecimalType");
    public int? Precision { get; init; }
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
}

internal sealed record MoneyAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.MoneyAttributeMetadata";
    public string AttributeType { get; } = "Money";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("MoneyType");
    public int? Precision { get; init; }
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
}

internal sealed record BooleanAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.BooleanAttributeMetadata";
    public string AttributeType { get; } = "Boolean";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("BooleanType");
    public required BooleanOptionSet OptionSet { get; init; }
}

internal sealed record BooleanOptionSet(BooleanOption TrueOption, BooleanOption FalseOption)
{
    [JsonPropertyName("@odata.type")]
    public string ODataType => "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata";
}

internal sealed record BooleanOption(int Value, LocalizedLabelSet Label)
{
    [JsonPropertyName("@odata.type")]
    public string ODataType => "Microsoft.Dynamics.CRM.OptionMetadata";
}

internal sealed record DateTimeAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata";
    public string AttributeType { get; } = "DateTime";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("DateTimeType");
    /// <summary>"DateOnly" | "DateAndTime".</summary>
    public string? Format { get; init; }
    public string? DateTimeBehavior { get; init; }
}

internal sealed record PicklistAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.PicklistAttributeMetadata";
    public string AttributeType { get; } = "Picklist";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("PicklistType");
    /// <summary>Used when binding to an existing global option set. Mutually exclusive with <see cref="OptionSet"/>.</summary>
    [JsonPropertyName("GlobalOptionSet@odata.bind")]
    public string? GlobalOptionSetBind { get; init; }
    /// <summary>Used for an inline option set defined alongside the column.</summary>
    public OptionSetBody? OptionSet { get; init; }
}

internal sealed record MultiSelectPicklistAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata";
    public string AttributeType { get; } = "Virtual";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("MultiSelectPicklistType");
    [JsonPropertyName("GlobalOptionSet@odata.bind")]
    public string? GlobalOptionSetBind { get; init; }
    public OptionSetBody? OptionSet { get; init; }
}

internal sealed record CustomerAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.CustomerAttributeMetadata";
    public string AttributeType { get; } = "Customer";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("CustomerType");
}

internal sealed record ImageAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.ImageAttributeMetadata";
    public string AttributeType { get; } = "Virtual";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("ImageType");
    public int? MaxSizeInKB { get; init; }
    public bool? CanStoreFullImage { get; init; }
}

/// <summary>The wrapper Dataverse uses for its enum-named "AttributeTypeName" property.</summary>
internal sealed record AttributeTypeNameBody(string Value);

// -------------------------------------------------------------- OptionSet (Choice)

/// <summary>Body for POST /GlobalOptionSetDefinitions and inline option sets.</summary>
internal sealed record OptionSetBody
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; init; } = "Microsoft.Dynamics.CRM.OptionSetMetadata";

    public required string Name { get; init; }
    public bool IsGlobal { get; init; } = true;
    public string OptionSetType { get; init; } = "Picklist";
    public LocalizedLabelSet? DisplayName { get; init; }
    public LocalizedLabelSet? Description { get; init; }
    public required OptionMetadataBody[] Options { get; init; }
    public bool? HasChanged { get; init; }
}

internal sealed record OptionMetadataBody(int Value, LocalizedLabelSet Label, LocalizedLabelSet? Description = null)
{
    [JsonPropertyName("@odata.type")]
    public string ODataType => "Microsoft.Dynamics.CRM.OptionMetadata";
}

// -------------------------------------------------------------- Relationships

/// <summary>1:N relationship body (also used for simple Lookup columns; the column comes for free).</summary>
internal sealed record OneToManyRelationshipBody
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; init; } = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata";

    public required string SchemaName { get; init; }
    public required string ReferencedEntity { get; init; }
    public required string ReferencingEntity { get; init; }
    public required LookupAttributeBody Lookup { get; init; }
    public CascadeConfigurationBody? CascadeConfiguration { get; init; }
}

internal sealed record LookupAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.LookupAttributeMetadata";
    public string AttributeType { get; } = "Lookup";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("LookupType");
}

internal sealed record CascadeConfigurationBody(
    string Assign = "NoCascade",
    string Delete = "RemoveLink",
    string Merge = "NoCascade",
    string Reparent = "NoCascade",
    string Share = "NoCascade",
    string Unshare = "NoCascade");

/// <summary>N:N relationship body (POST /RelationshipDefinitions).</summary>
internal sealed record ManyToManyRelationshipBody
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; init; } = "Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata";

    public required string SchemaName { get; init; }
    public required string IntersectEntityName { get; init; }
    public required string Entity1LogicalName { get; init; }
    public required string Entity2LogicalName { get; init; }
    public AssociatedMenuConfigurationBody? Entity1AssociatedMenuConfiguration { get; init; }
    public AssociatedMenuConfigurationBody? Entity2AssociatedMenuConfiguration { get; init; }
}

internal sealed record AssociatedMenuConfigurationBody(
    string Behavior = "UseLabel",
    string Group = "Details",
    int Order = 10000,
    LocalizedLabelSet? Label = null);

// -------------------------------------------------------------- Polymorphic lookup

/// <summary>Body for the CreatePolymorphicLookupAttribute action.</summary>
internal sealed record CreatePolymorphicLookupBody(
    OneToManyRelationshipBody[] OneToManyRelationships,
    PolymorphicLookupAttributeBody Lookup,
    bool SolutionUniqueName = false /* placeholder; never set */);

internal sealed record PolymorphicLookupAttributeBody : AttributeMetadataBody
{
    protected override string ODataTypeName => "Microsoft.Dynamics.CRM.ComplexLookupAttributeMetadata";
    public string AttributeType { get; } = "Lookup";
    public AttributeTypeNameBody AttributeTypeName { get; } = new("LookupType");
}

// -------------------------------------------------------------- Publish

/// <summary>Body for the PublishXml action: a single ParameterXml field with the import-export envelope.</summary>
internal sealed record PublishXmlBody(string ParameterXml);
