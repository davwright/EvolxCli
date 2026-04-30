using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Evolx.Cli.Auth;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Typed HTTP client for Dataverse Web API. Auth via `az` access token where the resource
/// is the org URL itself. One instance per environment.
/// </summary>
public sealed class DvClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _envUrl;
    private const string ApiVersion = "9.2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private DvClient(HttpClient http, string envUrl)
    {
        _http = http;
        _envUrl = envUrl;
    }

    public string EnvUrl => _envUrl;

    public static async Task<DvClient> CreateAsync(string envUrl, CancellationToken ct = default)
    {
        var token = await AzAuth.GetAccessTokenAsync(envUrl, ct);
        var http = new HttpClient { BaseAddress = new Uri($"{envUrl}/api/data/v{ApiVersion}/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        http.DefaultRequestHeaders.Add("OData-Version", "4.0");
        return new DvClient(http, envUrl);
    }

    /// <summary>
    /// GET against an entity set (e.g. "evo_sites") with optional OData params.
    /// Returns the parsed JsonElement from the response's `value` array.
    /// </summary>
    public async Task<JsonElement> QueryAsync(
        string entitySet,
        string? filter = null,
        string? select = null,
        string? orderBy = null,
        int? top = null,
        CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter)) qs.Add($"$filter={Uri.EscapeDataString(filter)}");
        if (!string.IsNullOrWhiteSpace(select)) qs.Add($"$select={Uri.EscapeDataString(select)}");
        if (!string.IsNullOrWhiteSpace(orderBy)) qs.Add($"$orderby={Uri.EscapeDataString(orderBy)}");
        if (top.HasValue) qs.Add($"$top={top.Value}");

        var url = entitySet + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        var resp = await _http.GetAsync(url, ct);
        await ThrowIfErrorAsync(resp, ct);

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// POST a record. Uses Prefer: return=representation so the response includes the new row
    /// with all its server-assigned columns (e.g. the new GUID).
    /// </summary>
    public async Task<JsonElement> CreateAsync(string entitySet, string jsonBody, CancellationToken ct = default)
    {
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, entitySet) { Content = content };
        req.Headers.Add("Prefer", "return=representation");

        var resp = await _http.SendAsync(req, ct);
        await ThrowIfErrorAsync(resp, ct);

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>DELETE a record by id. Returns true on success (204 NoContent).</summary>
    public async Task DeleteAsync(string entitySet, string id, CancellationToken ct = default)
    {
        var url = $"{entitySet}({id})";
        var resp = await _http.DeleteAsync(url, ct);
        await ThrowIfErrorAsync(resp, ct);
    }

    /// <summary>List all attributes on a table via the EntityDefinitions endpoint.</summary>
    public async Task<JsonElement> GetEntityAttributesAsync(string tableLogicalName, CancellationToken ct = default)
    {
        var url = $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(tableLogicalName)}')/Attributes";
        var resp = await _http.GetAsync(url, ct);
        await ThrowIfErrorAsync(resp, ct);

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// WhoAmI returns the current user's UserId, BusinessUnitId, OrganizationId.
    /// Lighter than `az` to confirm the user can actually talk to this Dataverse env.
    /// </summary>
    public async Task<JsonElement> WhoAmIAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("WhoAmI", ct);
        await ThrowIfErrorAsync(resp, ct);
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    private static async Task ThrowIfErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        // Dataverse error envelope: {"error":{"code":"...", "message":"..."}}
        string detail = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                detail = msg.GetString() ?? body;
            }
        }
        catch { /* keep raw body */ }

        throw new HttpRequestException(
            $"Dataverse {(int)resp.StatusCode} {resp.ReasonPhrase}: {detail}");
    }

    public void Dispose() => _http.Dispose();
}
