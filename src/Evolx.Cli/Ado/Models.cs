using System.Text.Json.Serialization;

namespace Evolx.Cli.Ado;

/// <summary>An Azure DevOps work item, keyed by id, plus the fields we read most often.</summary>
public sealed class WorkItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("rev")] public int Rev { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("fields")] public Dictionary<string, object?> Fields { get; set; } = new();

    public string Title => Fields.TryGetValue("System.Title", out var v) ? v?.ToString() ?? "" : "";
    public string State => Fields.TryGetValue("System.State", out var v) ? v?.ToString() ?? "" : "";
    public string Type => Fields.TryGetValue("System.WorkItemType", out var v) ? v?.ToString() ?? "" : "";
}

/// <summary>JSON Patch operation, the wire format ADO uses for work-item create/update.</summary>
public sealed record JsonPatchOp(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("value")] object? Value);

/// <summary>Standard error envelope ADO returns on 4xx.</summary>
public sealed class AdoError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("typeKey")] public string? TypeKey { get; set; }
}

internal sealed class WiqlQueryResult
{
    [JsonPropertyName("workItems")] public List<WiqlRef> WorkItems { get; set; } = new();
}

internal sealed class WiqlRef
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

internal sealed class WorkItemBatch
{
    [JsonPropertyName("value")] public List<WorkItem> Value { get; set; } = new();
}
