using System.Text.Json.Serialization;

namespace Evolx.Cli.Dataverse;

// Strongly-typed Dataverse solution-lifecycle bodies. As with SchemaBodies, no
// JSON shape for solution endpoints is hand-rolled anywhere else in the codebase.

// -------------------------------------------------------------- Solution create

/// <summary>
/// Body for POST /solutions. The publisher is bound via the
/// <c>publisherid@odata.bind</c> JSON name, which Dataverse uses for OData
/// reference linking. <see cref="PublisherIdBind"/> carries the
/// <c>/publishers(id)</c> string; the JSON property name on the wire is the
/// at-sign form, set via <see cref="JsonPropertyNameAttribute"/>.
/// </summary>
internal sealed record SolutionCreateBody
{
    public required string UniqueName { get; init; }
    public required string FriendlyName { get; init; }
    public string Version { get; init; } = "1.0.0.0";

    public string? Description { get; init; }

    [JsonPropertyName("publisherid@odata.bind")]
    public required string PublisherIdBind { get; init; }
}

// -------------------------------------------------------------- ExportSolution action

/// <summary>
/// Body for the ExportSolution unbound action.
///
/// <para>
/// Defaults match the pac/PowerShell-cmdlet flavour: unmanaged export, no auto-numbering
/// reset, no calendar inclusion. The boolean flags control what optional metadata
/// Dataverse bundles into the .zip; leaving them <c>false</c> keeps exports lean.
/// </para>
/// </summary>
internal sealed record ExportSolutionBody
{
    public required string SolutionName { get; init; }
    public bool Managed { get; init; }
    public bool ExportAutoNumberingSettings { get; init; }
    public bool ExportCalendarSettings { get; init; }
    public bool ExportCustomizationSettings { get; init; }
    public bool ExportEmailTrackingSettings { get; init; }
    public bool ExportGeneralSettings { get; init; }
    public bool ExportIsvConfig { get; init; }
    public bool ExportMarketingSettings { get; init; }
    public bool ExportOutlookSynchronizationSettings { get; init; }
    public bool ExportRelationshipRoles { get; init; }
    public bool ExportSales { get; init; }
}

// -------------------------------------------------------------- ImportSolutionAsync action

/// <summary>
/// Body for the ImportSolutionAsync action. <see cref="CustomizationFile"/> must be the
/// base64 of the solution .zip bytes — Dataverse rejects anything else with a 400 body
/// that includes the literal "Invalid character" string.
/// </summary>
internal sealed record ImportSolutionBody
{
    public required string CustomizationFile { get; init; }
    public bool OverwriteUnmanagedCustomizations { get; init; }
    public bool PublishWorkflows { get; init; }
    public Guid ImportJobId { get; init; } = Guid.NewGuid();
    public bool ConvertToManaged { get; init; }
    public bool SkipProductUpdateDependencies { get; init; }
    public bool HoldingSolution { get; init; }
}

// -------------------------------------------------------------- RemoveSolutionComponent action

/// <summary>
/// Body for the RemoveSolutionComponent action. <see cref="ComponentType"/> uses
/// Dataverse's component-type enum (1 = Entity, 9 = Attribute, 61 = WebResource, etc.).
/// </summary>
internal sealed record RemoveSolutionComponentBody
{
    public required Guid ComponentId { get; init; }
    public required int ComponentType { get; init; }
    public required string SolutionUniqueName { get; init; }
}
