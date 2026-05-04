using System.Text.Json;
using Evolx.Cli.Auth;
using Evolx.Cli.Http;

namespace Evolx.Cli.Ado;

/// <summary>
/// Typed REST client for Azure DevOps. All HTTP goes through HttpGateway.
/// One instance per (organization, project).
///
/// Implements IDisposable for source-compat with existing `using var ado = ...`
/// callers, but holds no per-instance resources — the underlying HttpClient
/// is process-shared in the gateway.
/// </summary>
public sealed class AdoClient : IDisposable
{
    private readonly string _organization;
    private readonly string _project;
    private readonly string _baseUrl;
    private readonly string _token;
    private const string ApiVersion = "7.1";

    private AdoClient(string organization, string project, string token)
    {
        _organization = organization;
        _project = project;
        _baseUrl = $"https://dev.azure.com/{organization}/";
        _token = token;
    }

    public static async Task<AdoClient> CreateAsync(string organization, string project, CancellationToken ct = default)
    {
        var token = await AzAuth.GetAccessTokenAsync(AzAuth.AzureDevOpsResource, ct);
        return new AdoClient(organization, project, token);
    }

    public string Organization => _organization;
    public string Project => _project;

    private string Url(string path) => _baseUrl + path;
    private string ProjectUrl(string path) => _baseUrl + _project + "/" + path;

    // ---------------------------------------------------------------- Work items

    public Task<WorkItem> GetWorkItemAsync(int id, CancellationToken ct = default)
        => HttpGateway.SendJsonAsync<WorkItem>(
            HttpMethod.Get,
            ProjectUrl($"_apis/wit/workitems/{id}?api-version={ApiVersion}"),
            bearerToken: _token, ct: ct);

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var idList = string.Join(",", ids);
        if (string.IsNullOrEmpty(idList)) return Array.Empty<WorkItem>();
        var batch = await HttpGateway.SendJsonAsync<WorkItemBatch>(
            HttpMethod.Get,
            ProjectUrl($"_apis/wit/workitems?ids={idList}&api-version={ApiVersion}"),
            bearerToken: _token, ct: ct);
        return batch?.Value ?? new List<WorkItem>();
    }

    public Task<WorkItem> CreateWorkItemAsync(
        string type,
        string title,
        string? description = null,
        int? parentId = null,
        IEnumerable<JsonPatchOp>? extraOps = null,
        CancellationToken ct = default)
    {
        var ops = new List<JsonPatchOp>
        {
            new("add", "/fields/System.Title", title),
        };
        if (!string.IsNullOrWhiteSpace(description))
            ops.Add(new("add", "/fields/System.Description", description));
        if (parentId.HasValue)
        {
            ops.Add(new("add", "/relations/-", new
            {
                rel = "System.LinkTypes.Hierarchy-Reverse",
                url = $"https://dev.azure.com/{_organization}/_apis/wit/workItems/{parentId.Value}",
            }));
        }
        if (extraOps != null) ops.AddRange(extraOps);

        // ADO requires application/json-patch+json for work-item ops.
        return HttpGateway.SendJsonAsync<WorkItem>(
            HttpMethod.Post,
            ProjectUrl($"_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version={ApiVersion}"),
            body: ops,
            contentType: "application/json-patch+json",
            bearerToken: _token, ct: ct);
    }

    public Task<WorkItem> UpdateWorkItemAsync(int id, IEnumerable<JsonPatchOp> ops, CancellationToken ct = default)
        => HttpGateway.SendJsonAsync<WorkItem>(
            new HttpMethod("PATCH"),
            ProjectUrl($"_apis/wit/workitems/{id}?api-version={ApiVersion}"),
            body: ops,
            contentType: "application/json-patch+json",
            bearerToken: _token, ct: ct);

    public Task<WorkItem> SetWorkItemStateAsync(int id, string state, CancellationToken ct = default)
        => UpdateWorkItemAsync(id, new[] { new JsonPatchOp("add", "/fields/System.State", state) }, ct);

    public async Task<IReadOnlyList<WorkItem>> QueryAsync(string wiql, CancellationToken ct = default)
    {
        var queryResult = await HttpGateway.SendJsonAsync<WiqlQueryResult>(
            HttpMethod.Post,
            ProjectUrl($"_apis/wit/wiql?api-version={ApiVersion}"),
            body: new { query = wiql },
            bearerToken: _token, ct: ct);
        var ids = (queryResult?.WorkItems ?? new List<WiqlRef>()).Select(x => x.Id).ToList();
        return await GetWorkItemsAsync(ids, ct);
    }

    public async Task<int> AddCommentAsync(int workItemId, string text, CancellationToken ct = default)
    {
        var resp = await HttpGateway.SendJsonAsync<CommentResponse>(
            HttpMethod.Post,
            ProjectUrl($"_apis/wit/workItems/{workItemId}/comments?api-version=7.1-preview.4"),
            body: new { text },
            bearerToken: _token, ct: ct);
        return resp?.Id ?? 0;
    }

    public Task<WorkItem> LinkWorkItemsAsync(int sourceId, int targetId, string rel, CancellationToken ct = default)
        => UpdateWorkItemAsync(sourceId, new[]
        {
            new JsonPatchOp("add", "/relations/-", new
            {
                rel,
                url = $"https://dev.azure.com/{_organization}/_apis/wit/workItems/{targetId}",
            })
        }, ct);

    public async Task<IReadOnlyList<string>> GetValidStatesAsync(string workItemType, CancellationToken ct = default)
    {
        var json = await HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Get,
            ProjectUrl($"_apis/wit/workitemtypes/{Uri.EscapeDataString(workItemType)}/states?api-version={ApiVersion}"),
            bearerToken: _token, ct: ct);
        var names = new List<string>();
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
                names.Add(el.GetProperty("name").GetString() ?? "");
        }
        return names;
    }

    // ---------------------------------------------------------------- Repos

    public async Task<IReadOnlyList<GitRepository>> ListRepositoriesAsync(CancellationToken ct = default)
    {
        var batch = await HttpGateway.SendJsonAsync<GitRepoListResponse>(
            HttpMethod.Get,
            ProjectUrl($"_apis/git/repositories?api-version={ApiVersion}"),
            bearerToken: _token, ct: ct);
        return batch?.Value ?? new List<GitRepository>();
    }

    public Task<GitRepository> GetRepositoryAsync(string nameOrId, CancellationToken ct = default)
        => HttpGateway.SendJsonAsync<GitRepository>(
            HttpMethod.Get,
            ProjectUrl($"_apis/git/repositories/{Uri.EscapeDataString(nameOrId)}?api-version={ApiVersion}"),
            bearerToken: _token, ct: ct);

    // ---------------------------------------------------------------- Pull requests

    public async Task<IReadOnlyList<PullRequest>> ListPullRequestsAsync(
        string repoNameOrId,
        string status = "active",
        string? creatorId = null,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"searchCriteria.status={Uri.EscapeDataString(status)}" };
        if (!string.IsNullOrWhiteSpace(creatorId)) qs.Add($"searchCriteria.creatorId={Uri.EscapeDataString(creatorId)}");
        qs.Add($"api-version={ApiVersion}");

        var batch = await HttpGateway.SendJsonAsync<PullRequestListResponse>(
            HttpMethod.Get,
            ProjectUrl($"_apis/git/repositories/{Uri.EscapeDataString(repoNameOrId)}/pullrequests?{string.Join("&", qs)}"),
            bearerToken: _token, ct: ct);
        return batch?.Value ?? new List<PullRequest>();
    }

    public async Task<IReadOnlyList<PullRequest>> ListProjectPullRequestsAsync(
        string status = "active",
        string? creatorId = null,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"searchCriteria.status={Uri.EscapeDataString(status)}" };
        if (!string.IsNullOrWhiteSpace(creatorId)) qs.Add($"searchCriteria.creatorId={Uri.EscapeDataString(creatorId)}");
        qs.Add($"api-version={ApiVersion}");

        var batch = await HttpGateway.SendJsonAsync<PullRequestListResponse>(
            HttpMethod.Get,
            ProjectUrl($"_apis/git/pullrequests?{string.Join("&", qs)}"),
            bearerToken: _token, ct: ct);
        return batch?.Value ?? new List<PullRequest>();
    }

    public Task<PullRequest> GetPullRequestAsync(int pullRequestId, CancellationToken ct = default)
        => HttpGateway.SendJsonAsync<PullRequest>(
            HttpMethod.Get,
            ProjectUrl($"_apis/git/pullrequests/{pullRequestId}?api-version={ApiVersion}"),
            bearerToken: _token, ct: ct);

    public Task<PullRequest> CreatePullRequestAsync(
        string repoNameOrId,
        string sourceBranch,
        string targetBranch,
        string title,
        string? description = null,
        bool isDraft = false,
        CancellationToken ct = default)
    {
        var body = new PullRequestCreate
        {
            SourceRefName = sourceBranch.StartsWith("refs/heads/") ? sourceBranch : $"refs/heads/{sourceBranch}",
            TargetRefName = targetBranch.StartsWith("refs/heads/") ? targetBranch : $"refs/heads/{targetBranch}",
            Title = title,
            Description = description,
            IsDraft = isDraft,
        };
        return HttpGateway.SendJsonAsync<PullRequest>(
            HttpMethod.Post,
            ProjectUrl($"_apis/git/repositories/{Uri.EscapeDataString(repoNameOrId)}/pullrequests?api-version={ApiVersion}"),
            body: body,
            bearerToken: _token, ct: ct);
    }

    public async Task<int> AddPullRequestCommentAsync(
        string repoNameOrId,
        int pullRequestId,
        string text,
        CancellationToken ct = default)
    {
        var body = new
        {
            comments = new[] { new { parentCommentId = 0, content = text, commentType = 1 } },
            status = 1, // active
        };
        var resp = await HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Post,
            ProjectUrl($"_apis/git/repositories/{Uri.EscapeDataString(repoNameOrId)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}"),
            body: body,
            bearerToken: _token, ct: ct);
        return resp.GetProperty("id").GetInt32();
    }

    public void Dispose() { /* no per-instance HttpClient to dispose anymore */ }
}
