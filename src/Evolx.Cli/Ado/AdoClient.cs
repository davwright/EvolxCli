using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Evolx.Cli.Auth;

namespace Evolx.Cli.Ado;

/// <summary>
/// Typed HTTP client for Azure DevOps REST. Auth via `az` access token.
/// One instance per (organization, project).
/// </summary>
public sealed class AdoClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _organization;
    private readonly string _project;
    private const string ApiVersion = "7.1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private AdoClient(HttpClient http, string organization, string project)
    {
        _http = http;
        _organization = organization;
        _project = project;
    }

    public static async Task<AdoClient> CreateAsync(string organization, string project, CancellationToken ct = default)
    {
        var token = await AzAuth.GetAccessTokenAsync(AzAuth.AzureDevOpsResource, ct);
        var http = new HttpClient { BaseAddress = new Uri($"https://dev.azure.com/{organization}/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new AdoClient(http, organization, project);
    }

    public string Organization => _organization;
    public string Project => _project;

    // ---------------------------------------------------------------- Work items

    public async Task<WorkItem> GetWorkItemAsync(int id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{_project}/_apis/wit/workitems/{id}?api-version={ApiVersion}", ct);
        await ThrowIfErrorAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<WorkItem>(JsonOptions, ct))!;
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var idList = string.Join(",", ids);
        if (string.IsNullOrEmpty(idList)) return Array.Empty<WorkItem>();

        var resp = await _http.GetAsync(
            $"{_project}/_apis/wit/workitems?ids={idList}&api-version={ApiVersion}", ct);
        await ThrowIfErrorAsync(resp, ct);
        var batch = await resp.Content.ReadFromJsonAsync<WorkItemBatch>(JsonOptions, ct);
        return batch?.Value ?? new List<WorkItem>();
    }

    public async Task<WorkItem> CreateWorkItemAsync(
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

        var content = JsonContent.Create(ops, options: JsonOptions);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");

        var resp = await _http.PostAsync(
            $"{_project}/_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version={ApiVersion}",
            content, ct);
        await ThrowIfErrorAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<WorkItem>(JsonOptions, ct))!;
    }

    public async Task<WorkItem> UpdateWorkItemAsync(
        int id,
        IEnumerable<JsonPatchOp> ops,
        CancellationToken ct = default)
    {
        var content = JsonContent.Create(ops, options: JsonOptions);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");

        var req = new HttpRequestMessage(HttpMethod.Patch,
            $"{_project}/_apis/wit/workitems/{id}?api-version={ApiVersion}")
        { Content = content };

        var resp = await _http.SendAsync(req, ct);
        await ThrowIfErrorAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<WorkItem>(JsonOptions, ct))!;
    }

    public Task<WorkItem> SetWorkItemStateAsync(int id, string state, CancellationToken ct = default)
        => UpdateWorkItemAsync(id, new[] { new JsonPatchOp("add", "/fields/System.State", state) }, ct);

    public async Task<IReadOnlyList<WorkItem>> QueryAsync(string wiql, CancellationToken ct = default)
    {
        // POST WIQL -> get list of ids -> batch GET full items
        var resp = await _http.PostAsJsonAsync(
            $"{_project}/_apis/wit/wiql?api-version={ApiVersion}",
            new { query = wiql }, ct);
        await ThrowIfErrorAsync(resp, ct);

        var queryResult = await resp.Content.ReadFromJsonAsync<WiqlQueryResult>(JsonOptions, ct)
            ?? new WiqlQueryResult();
        var ids = queryResult.WorkItems.Select(x => x.Id).ToList();
        return await GetWorkItemsAsync(ids, ct);
    }

    public async Task<IReadOnlyList<string>> GetValidStatesAsync(string workItemType, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(
            $"{_project}/_apis/wit/workitemtypes/{Uri.EscapeDataString(workItemType)}/states?api-version={ApiVersion}", ct);
        await ThrowIfErrorAsync(resp, ct);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var names = new List<string>();
        foreach (var el in doc.RootElement.GetProperty("value").EnumerateArray())
            names.Add(el.GetProperty("name").GetString() ?? "");
        return names;
    }

    /// <summary>Add a comment to a work item.</summary>
    public async Task<int> AddCommentAsync(int workItemId, string text, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{_project}/_apis/wit/workItems/{workItemId}/comments?api-version=7.1-preview.4",
            new { text }, ct);
        await ThrowIfErrorAsync(resp, ct);
        var body = await resp.Content.ReadFromJsonAsync<CommentResponse>(JsonOptions, ct);
        return body?.Id ?? 0;
    }

    /// <summary>
    /// Link two work items.
    /// Common rels: "System.LinkTypes.Hierarchy-Forward" (parent->child),
    /// "System.LinkTypes.Hierarchy-Reverse" (child->parent),
    /// "System.LinkTypes.Related", "System.LinkTypes.Dependency-forward",
    /// "System.LinkTypes.Dependency-reverse".
    /// </summary>
    public Task<WorkItem> LinkWorkItemsAsync(int sourceId, int targetId, string rel, CancellationToken ct = default)
        => UpdateWorkItemAsync(sourceId, new[]
        {
            new JsonPatchOp("add", "/relations/-", new
            {
                rel,
                url = $"https://dev.azure.com/{_organization}/_apis/wit/workItems/{targetId}",
            })
        }, ct);

    // ---------------------------------------------------------------- Repos

    public async Task<IReadOnlyList<GitRepository>> ListRepositoriesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(
            $"{_project}/_apis/git/repositories?api-version={ApiVersion}", ct);
        await ThrowIfErrorAsync(resp, ct);
        var body = await resp.Content.ReadFromJsonAsync<GitRepoListResponse>(JsonOptions, ct);
        return body?.Value ?? new List<GitRepository>();
    }

    public async Task<GitRepository> GetRepositoryAsync(string nameOrId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(
            $"{_project}/_apis/git/repositories/{Uri.EscapeDataString(nameOrId)}?api-version={ApiVersion}", ct);
        await ThrowIfErrorAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<GitRepository>(JsonOptions, ct))!;
    }

    // ---------------------------------------------------------------- Pull requests

    /// <summary>List PRs in a repo. Status: active | abandoned | completed | all (default active).</summary>
    public async Task<IReadOnlyList<PullRequest>> ListPullRequestsAsync(
        string repoNameOrId,
        string status = "active",
        string? creatorId = null,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"searchCriteria.status={Uri.EscapeDataString(status)}" };
        if (!string.IsNullOrWhiteSpace(creatorId)) qs.Add($"searchCriteria.creatorId={Uri.EscapeDataString(creatorId)}");
        qs.Add($"api-version={ApiVersion}");

        var resp = await _http.GetAsync(
            $"{_project}/_apis/git/repositories/{Uri.EscapeDataString(repoNameOrId)}/pullrequests?{string.Join("&", qs)}", ct);
        await ThrowIfErrorAsync(resp, ct);
        var body = await resp.Content.ReadFromJsonAsync<PullRequestListResponse>(JsonOptions, ct);
        return body?.Value ?? new List<PullRequest>();
    }

    /// <summary>List PRs across the whole project (any repo).</summary>
    public async Task<IReadOnlyList<PullRequest>> ListProjectPullRequestsAsync(
        string status = "active",
        string? creatorId = null,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"searchCriteria.status={Uri.EscapeDataString(status)}" };
        if (!string.IsNullOrWhiteSpace(creatorId)) qs.Add($"searchCriteria.creatorId={Uri.EscapeDataString(creatorId)}");
        qs.Add($"api-version={ApiVersion}");

        var resp = await _http.GetAsync(
            $"{_project}/_apis/git/pullrequests?{string.Join("&", qs)}", ct);
        await ThrowIfErrorAsync(resp, ct);
        var body = await resp.Content.ReadFromJsonAsync<PullRequestListResponse>(JsonOptions, ct);
        return body?.Value ?? new List<PullRequest>();
    }

    public async Task<PullRequest> GetPullRequestAsync(int pullRequestId, CancellationToken ct = default)
    {
        // The project-level endpoint accepts just the PR id (no need to know the repo).
        var resp = await _http.GetAsync(
            $"{_project}/_apis/git/pullrequests/{pullRequestId}?api-version={ApiVersion}", ct);
        await ThrowIfErrorAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<PullRequest>(JsonOptions, ct))!;
    }

    public async Task<PullRequest> CreatePullRequestAsync(
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

        var resp = await _http.PostAsJsonAsync(
            $"{_project}/_apis/git/repositories/{Uri.EscapeDataString(repoNameOrId)}/pullrequests?api-version={ApiVersion}",
            body, JsonOptions, ct);
        await ThrowIfErrorAsync(resp, ct);
        return (await resp.Content.ReadFromJsonAsync<PullRequest>(JsonOptions, ct))!;
    }

    /// <summary>Add a comment thread to a PR with a single comment in it (the most common need).</summary>
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
        var resp = await _http.PostAsJsonAsync(
            $"{_project}/_apis/git/repositories/{Uri.EscapeDataString(repoNameOrId)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}",
            body, ct);
        await ThrowIfErrorAsync(resp, ct);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    // ---------------------------------------------------------------- helpers

    private static async Task ThrowIfErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        string detail;
        try
        {
            var err = JsonSerializer.Deserialize<AdoError>(body, JsonOptions);
            detail = err?.Message ?? body;
        }
        catch { detail = body; }
        throw new HttpRequestException($"ADO {(int)resp.StatusCode} {resp.ReasonPhrase}: {detail}");
    }

    public void Dispose() => _http.Dispose();
}
