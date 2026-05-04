using System.Text.Json;
using Evolx.Cli.Auth;
using Evolx.Cli.Http;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Typed REST client for Dataverse Web API. All HTTP goes through HttpGateway.
/// One instance per environment.
///
/// Implements IDisposable for source-compat with existing `using` callers, but
/// holds no per-instance resources — the underlying HttpClient is process-shared.
/// </summary>
public sealed class DvClient : IDisposable
{
    private readonly string _envUrl;
    private readonly string _baseUrl;
    private readonly string _token;
    private const string ApiVersion = "9.2";

    private static readonly Dictionary<string, string> DataverseHeaders = new()
    {
        ["Accept"] = "application/json",
        ["OData-MaxVersion"] = "4.0",
        ["OData-Version"] = "4.0",
    };

    private DvClient(string envUrl, string token)
    {
        _envUrl = envUrl;
        _baseUrl = $"{envUrl}/api/data/v{ApiVersion}/";
        _token = token;
    }

    public string EnvUrl => _envUrl;

    public static async Task<DvClient> CreateAsync(string envUrl, CancellationToken ct = default)
    {
        var token = await AzAuth.GetAccessTokenAsync(envUrl, ct);
        return new DvClient(envUrl, token);
    }

    /// <summary>GET against an entity set with optional OData params. Returns the full root JsonElement.</summary>
    public Task<JsonElement> QueryAsync(
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

        var url = _baseUrl + entitySet + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Get, url,
            headers: DataverseHeaders,
            bearerToken: _token,
            ct: ct);
    }

    /// <summary>POST a record body (caller-formatted JSON string) with Prefer: return=representation.</summary>
    public Task<JsonElement> CreateAsync(string entitySet, string jsonBody, CancellationToken ct = default)
    {
        var headers = new Dictionary<string, string>(DataverseHeaders) { ["Prefer"] = "return=representation" };
        return HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Post, _baseUrl + entitySet,
            body: System.Text.Json.JsonDocument.Parse(jsonBody).RootElement,
            headers: headers,
            bearerToken: _token,
            ct: ct);
    }

    /// <summary>DELETE a record by id.</summary>
    public Task DeleteAsync(string entitySet, string id, CancellationToken ct = default)
        => HttpGateway.SendNoContentAsync(
            HttpMethod.Delete, _baseUrl + $"{entitySet}({id})",
            headers: DataverseHeaders,
            bearerToken: _token,
            ct: ct);

    /// <summary>List all attributes on a table via the EntityDefinitions endpoint.</summary>
    public Task<JsonElement> GetEntityAttributesAsync(string tableLogicalName, CancellationToken ct = default)
    {
        var url = _baseUrl + $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(tableLogicalName)}')/Attributes";
        return HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Get, url,
            headers: DataverseHeaders,
            bearerToken: _token,
            ct: ct);
    }

    /// <summary>WhoAmI returns UserId/BU/Org.</summary>
    public Task<JsonElement> WhoAmIAsync(CancellationToken ct = default)
        => HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Get, _baseUrl + "WhoAmI",
            headers: DataverseHeaders,
            bearerToken: _token,
            ct: ct);

    public void Dispose() { /* no per-instance resources */ }
}
