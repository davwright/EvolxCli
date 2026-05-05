using Evolx.Cli.Dataverse;

namespace Evolx.Cli.Commands.Dv.Schema.Column;

/// <summary>
/// Single dispatch point that turns a flat <see cref="NewColumnCommand.Settings"/> into the
/// right strongly-typed metadata body and the right <c>POST</c> path. Centralized so no
/// command wires up an <c>@odata.type</c> by hand.
/// </summary>
internal static class ColumnTypeBodies
{
    /// <summary>Logical name of the column being created (lowercased <c>SchemaName</c>).</summary>
    public static string LogicalName(string schemaName) => schemaName.ToLowerInvariant();

    /// <summary>
    /// Build the request for a new column. Returns (path, body) — for most types path is
    /// <c>EntityDefinitions(LogicalName='&lt;table&gt;')/Attributes</c>, but Lookup goes to
    /// <c>RelationshipDefinitions</c> and Polymorphic goes through a custom action which
    /// the caller handles separately (<see cref="IsRelationship"/> / <see cref="IsPolymorphic"/>).
    /// </summary>
    public static (string Path, object Body) Build(string table, string columnType, NewColumnCommand.Settings s)
    {
        var name = LocalizedLabel.Build(s.DisplayName ?? s.SchemaName, s.DisplayNameDe);
        var desc = LocalizedLabel.Build(s.Description, s.DescriptionDe);
        var required = new RequiredLevelBody(s.RequiredLevel);

        var attrPath = $"EntityDefinitions(LogicalName='{OData.EscapeLiteral(table)}')/Attributes";

        return columnType.ToLowerInvariant() switch
        {
            "text" => (attrPath, new StringAttributeBody
            {
                SchemaName = s.SchemaName,
                DisplayName = name,
                Description = desc,
                RequiredLevel = required,
                MaxLength = s.MaxLength ?? 100,
            }),
            "memo" => (attrPath, new MemoAttributeBody
            {
                SchemaName = s.SchemaName,
                DisplayName = name,
                Description = desc,
                RequiredLevel = required,
                MaxLength = s.MaxLength ?? 2000,
            }),
            "integer" => (attrPath, new IntegerAttributeBody
            {
                SchemaName = s.SchemaName,
                DisplayName = name,
                Description = desc,
                RequiredLevel = required,
                MinValue = (int?)s.Min,
                MaxValue = (int?)s.Max,
            }),
            "decimal" => (attrPath, new DecimalAttributeBody
            {
                SchemaName = s.SchemaName,
                DisplayName = name,
                Description = desc,
                RequiredLevel = required,
                Precision = s.Precision ?? 2,
                MinValue = s.Min,
                MaxValue = s.Max,
            }),
            "money" => (attrPath, new MoneyAttributeBody
            {
                SchemaName = s.SchemaName,
                DisplayName = name,
                Description = desc,
                RequiredLevel = required,
                Precision = s.Precision ?? 2,
                MinValue = s.Min,
                MaxValue = s.Max,
            }),
            "boolean" => (attrPath, BuildBoolean(s, name, desc, required)),
            "date" => (attrPath, BuildDateTime(s, name, desc, required, format: "DateOnly")),
            "datetime" => (attrPath, BuildDateTime(s, name, desc, required, format: "DateAndTime")),
            "choice" => (attrPath, BuildPicklist(s, name, desc, required)),
            "multi-choice" => (attrPath, BuildMultiPicklist(s, name, desc, required)),
            "customer" => (attrPath, new CustomerAttributeBody
            {
                SchemaName = s.SchemaName,
                DisplayName = name,
                Description = desc,
                RequiredLevel = required,
            }),
            "image" => (attrPath, new ImageAttributeBody
            {
                SchemaName = s.SchemaName,
                DisplayName = name,
                Description = desc,
                RequiredLevel = required,
                MaxSizeInKB = s.MaxSizeKb,
                CanStoreFullImage = s.CanStoreFullImage ? true : null,
            }),
            "lookup" => ("RelationshipDefinitions", BuildLookupRelationship(table, s, name, desc, required)),
            "polymorphic" => throw new InvalidOperationException(
                "Polymorphic lookups go through CreatePolymorphicLookupAttribute — use the polymorphic-lookup command, not column new."),
            _ => throw new ArgumentException(
                $"Unknown column type '{columnType}'. Valid: text, memo, integer, decimal, money, boolean, date, datetime, choice, multi-choice, customer, image, lookup."),
        };
    }

    /// <summary>True for column types whose POST goes to RelationshipDefinitions (lookup).</summary>
    public static bool IsRelationship(string columnType) =>
        string.Equals(columnType, "lookup", StringComparison.OrdinalIgnoreCase);

    private static BooleanAttributeBody BuildBoolean(
        NewColumnCommand.Settings s, LocalizedLabelSet? name, LocalizedLabelSet? desc, RequiredLevelBody required)
    {
        var trueLabel = LocalizedLabel.Build(s.TrueLabel ?? "Yes")!;
        var falseLabel = LocalizedLabel.Build(s.FalseLabel ?? "No")!;
        return new BooleanAttributeBody
        {
            SchemaName = s.SchemaName,
            DisplayName = name,
            Description = desc,
            RequiredLevel = required,
            OptionSet = new BooleanOptionSet(
                TrueOption: new BooleanOption(1, trueLabel),
                FalseOption: new BooleanOption(0, falseLabel)),
        };
    }

    private static DateTimeAttributeBody BuildDateTime(
        NewColumnCommand.Settings s, LocalizedLabelSet? name, LocalizedLabelSet? desc, RequiredLevelBody required, string format)
        => new()
        {
            SchemaName = s.SchemaName,
            DisplayName = name,
            Description = desc,
            RequiredLevel = required,
            Format = format,
            DateTimeBehavior = format == "DateOnly" ? "DateOnly" : "UserLocal",
        };

    private static PicklistAttributeBody BuildPicklist(
        NewColumnCommand.Settings s, LocalizedLabelSet? name, LocalizedLabelSet? desc, RequiredLevelBody required)
    {
        var (bind, optionSet) = ResolveOptionSet(s, isInlineMulti: false);
        return new PicklistAttributeBody
        {
            SchemaName = s.SchemaName,
            DisplayName = name,
            Description = desc,
            RequiredLevel = required,
            GlobalOptionSetBind = bind,
            OptionSet = optionSet,
        };
    }

    private static MultiSelectPicklistAttributeBody BuildMultiPicklist(
        NewColumnCommand.Settings s, LocalizedLabelSet? name, LocalizedLabelSet? desc, RequiredLevelBody required)
    {
        var (bind, optionSet) = ResolveOptionSet(s, isInlineMulti: true);
        return new MultiSelectPicklistAttributeBody
        {
            SchemaName = s.SchemaName,
            DisplayName = name,
            Description = desc,
            RequiredLevel = required,
            GlobalOptionSetBind = bind,
            OptionSet = optionSet,
        };
    }

    /// <summary>
    /// Resolve --global-option-set vs --choices into the right binding for a Picklist body.
    /// Exactly one of the two must be set; both or neither is a clear caller error.
    /// </summary>
    private static (string? Bind, OptionSetBody? OptionSet) ResolveOptionSet(NewColumnCommand.Settings s, bool isInlineMulti)
    {
        var hasGlobal = !string.IsNullOrWhiteSpace(s.GlobalOptionSet);
        var hasInline = !string.IsNullOrWhiteSpace(s.Choices);
        if (hasGlobal == hasInline)
            throw new ArgumentException("Choice columns require exactly one of --global-option-set or --choices.");

        if (hasGlobal)
            return ($"GlobalOptionSetDefinitions(Name='{s.GlobalOptionSet}')", null);

        var en = s.Choices!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var de = s.ChoicesDe?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 ?? Array.Empty<string>();
        if (de.Length > 0 && de.Length != en.Length)
            throw new ArgumentException($"--choices ({en.Length} options) and --choices-de ({de.Length}) must have the same number of entries.");

        var options = new OptionMetadataBody[en.Length];
        for (int i = 0; i < en.Length; i++)
        {
            var label = LocalizedLabel.Build(en[i], i < de.Length ? de[i] : null)!;
            options[i] = new OptionMetadataBody(Value: 100_000_000 + i, Label: label);
        }
        var inlineSet = new OptionSetBody
        {
            Name = $"{s.SchemaName}_OptionSet",
            IsGlobal = false,
            OptionSetType = isInlineMulti ? "MultiSelectPicklist" : "Picklist",
            Options = options,
        };
        return (null, inlineSet);
    }

    private static OneToManyRelationshipBody BuildLookupRelationship(
        string referencingTable, NewColumnCommand.Settings s, LocalizedLabelSet? name, LocalizedLabelSet? desc, RequiredLevelBody required)
    {
        if (string.IsNullOrWhiteSpace(s.Target))
            throw new ArgumentException("Lookup columns require --target <table>.");

        var lookup = new LookupAttributeBody
        {
            SchemaName = s.SchemaName,
            DisplayName = name,
            Description = desc,
            RequiredLevel = required,
        };

        return new OneToManyRelationshipBody
        {
            SchemaName = $"{s.SchemaName}_{referencingTable}",
            ReferencedEntity = s.Target,
            ReferencingEntity = referencingTable,
            Lookup = lookup,
            CascadeConfiguration = new CascadeConfigurationBody(),
        };
    }
}
