using System.Text.Json.Serialization;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Manifest format consumed by <c>ev dv plugin sync</c>. Decoupled from any
/// particular .NET reflection scheme so the sync engine can be unit-tested
/// without a live DLL. Future work: a small build-time tool in the plugin
/// project that emits this manifest from <c>PluginProcessingStepConfigs()</c>
/// or attribute-decorated step classes.
/// </summary>
public sealed record PluginManifest
{
    /// <summary>Assembly logical name (DLL name without .dll).</summary>
    public required string AssemblyName { get; init; }

    /// <summary>Assembly version, e.g. "1.0.0.0".</summary>
    public required string AssemblyVersion { get; init; }

    /// <summary>Plugin types contained in this assembly.</summary>
    public PluginManifestType[] Types { get; init; } = Array.Empty<PluginManifestType>();
}

public sealed record PluginManifestType
{
    /// <summary>Fully-qualified type name (Namespace.ClassName).</summary>
    public required string TypeName { get; init; }

    /// <summary>Steps registered for this type.</summary>
    public PluginManifestStep[] Steps { get; init; } = Array.Empty<PluginManifestStep>();
}

public sealed record PluginManifestStep
{
    /// <summary>Step display name (the SDK <c>name</c> column).</summary>
    public required string StepName { get; init; }

    /// <summary>SDK message name (e.g. Create, Update, Delete).</summary>
    public required string Message { get; init; }

    /// <summary>Primary entity logical name. Empty string for global / cross-entity steps.</summary>
    public string Entity { get; init; } = "";

    /// <summary>Stage: 10=PreValidation, 20=PreOperation, 40=PostOperation.</summary>
    public required int Stage { get; init; }

    /// <summary>Mode: 0=Synchronous, 1=Asynchronous.</summary>
    public required int Mode { get; init; }

    /// <summary>Rank (execution order within the stage).</summary>
    public int Rank { get; init; } = 1;

    /// <summary>Comma-separated list of filtered attribute logical names. Empty for "all attributes".</summary>
    public string FilteredAttributes { get; init; } = "";

    /// <summary>SupportedDeployment: 0=ServerOnly, 1=OfflineOnly, 2=Both.</summary>
    public int SupportedDeployment { get; init; }

    /// <summary>Optional configuration string passed to plugin context.</summary>
    public string Configuration { get; init; } = "";
}
