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
