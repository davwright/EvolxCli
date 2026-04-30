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

// ---------------------------------------------------------------- Repos / PRs

public sealed class GitRepository
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("webUrl")] public string? WebUrl { get; set; }
    [JsonPropertyName("remoteUrl")] public string? RemoteUrl { get; set; }
    [JsonPropertyName("defaultBranch")] public string? DefaultBranch { get; set; }
    [JsonPropertyName("project")] public ProjectRef? Project { get; set; }
}

public sealed class ProjectRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

internal sealed class GitRepoListResponse
{
    [JsonPropertyName("value")] public List<GitRepository> Value { get; set; } = new();
}

public sealed class IdentityRef
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("uniqueName")] public string? UniqueName { get; set; }
}

public sealed class PullRequest
{
    [JsonPropertyName("pullRequestId")] public int PullRequestId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("sourceRefName")] public string SourceRefName { get; set; } = "";
    [JsonPropertyName("targetRefName")] public string TargetRefName { get; set; } = "";
    [JsonPropertyName("createdBy")] public IdentityRef? CreatedBy { get; set; }
    [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
    [JsonPropertyName("repository")] public GitRepository? Repository { get; set; }
}

internal sealed class PullRequestListResponse
{
    [JsonPropertyName("value")] public List<PullRequest> Value { get; set; } = new();
}

/// <summary>Used to POST a new PR. Only the fields ADO needs.</summary>
public sealed class PullRequestCreate
{
    [JsonPropertyName("sourceRefName")] public string SourceRefName { get; set; } = "";
    [JsonPropertyName("targetRefName")] public string TargetRefName { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
}

internal sealed class CommentResponse
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
}
