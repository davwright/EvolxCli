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

    /// <summary>
    /// Test-only factory: builds a client with a known env URL and token, skipping the
    /// AzAuth round-trip. Production code goes through <see cref="CreateAsync"/>.
    /// </summary>
    internal static DvClient ForTesting(string envUrl, string token) => new(envUrl, token);

    public string EnvUrl => _envUrl;
    public string BaseUrl => _baseUrl;

    public static async Task<DvClient> CreateAsync(string envUrl, CancellationToken ct = default)
    {
        var token = await AzAuth.GetAccessTokenAsync(envUrl, ct);
        return new DvClient(envUrl, token);
    }

    // -------------------------------------------------------------- Core dispatch

    /// <summary>
    /// GET against any URL — relative (resolved against the data API base) or absolute
    /// (e.g. an `@odata.nextLink`). Single dispatch point so headers/auth are uniform.
    /// </summary>
    public Task<JsonElement> GetJsonAsync(string urlOrPath, CancellationToken ct = default)
        => HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Get, ResolveUrl(urlOrPath),
            headers: DataverseHeaders,
            bearerToken: _token,
            ct: ct);

    /// <summary>
    /// GET as raw bytes — used for `$metadata` (XML, not JSON). Caller decides how to parse.
    /// </summary>
    public Task<byte[]> GetBytesAsync(string urlOrPath, string accept, CancellationToken ct = default)
    {
        var headers = new Dictionary<string, string>(DataverseHeaders) { ["Accept"] = accept };
        return HttpGateway.SendBytesAsync(
            HttpMethod.Get, ResolveUrl(urlOrPath),
            headers: headers,
            bearerToken: _token,
            ct: ct);
    }

    private string ResolveUrl(string urlOrPath) =>
        urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? urlOrPath
            : _baseUrl + urlOrPath;

    // -------------------------------------------------------------- Read

    /// <summary>GET against an entity set with optional OData params. Returns the full root JsonElement.</summary>
    public Task<JsonElement> QueryAsync(
        string entitySet,
        string? filter = null,
        string? select = null,
        string? orderBy = null,
        int? top = null,
        CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$filter", filter),
            new("$select", select),
            new("$orderby", orderBy),
            new("$top", top?.ToString()),
        });
        return GetJsonAsync(entitySet + qs, ct);
    }

    /// <summary>
    /// Page through an entity set. Issues the first request with a maxpagesize hint, then
    /// follows `@odata.nextLink` while it's present (if <paramref name="followAll"/>).
    /// Calls <paramref name="onPage"/> once per page with the running row count.
    /// </summary>
    public async Task<PagedResult> QueryPagedAsync(
        string entitySet,
        string? filter = null,
        string? select = null,
        int pageSize = 5000,
        bool followAll = false,
        Action<int>? onPage = null,
        CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$filter", filter),
            new("$select", select),
        });

        var headers = new Dictionary<string, string>(DataverseHeaders)
        {
            ["Prefer"] = $"odata.maxpagesize={pageSize}",
        };

        var url = ResolveUrl(entitySet + qs);
        var rows = new List<JsonElement>();
        string? nextLink;

        while (true)
        {
            var page = await HttpGateway.SendJsonForJsonElementAsync(
                HttpMethod.Get, url,
                headers: headers,
                bearerToken: _token,
                ct: ct);

            if (page.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in value.EnumerateArray()) rows.Add(row.Clone());
            }
            onPage?.Invoke(rows.Count);

            nextLink = page.TryGetProperty("@odata.nextLink", out var nl) && nl.ValueKind == JsonValueKind.String
                ? nl.GetString()
                : null;

            if (!followAll || string.IsNullOrEmpty(nextLink)) break;
            url = nextLink;
        }

        return new PagedResult(rows, !string.IsNullOrEmpty(nextLink));
    }

    /// <summary>Result of a paged read. HasMore is true when more pages exist server-side.</summary>
    public sealed record PagedResult(IReadOnlyList<JsonElement> Rows, bool HasMore);

    // -------------------------------------------------------------- Write

    /// <summary>POST a record body (caller-formatted JSON string) with Prefer: return=representation.</summary>
    public Task<JsonElement> CreateAsync(string entitySet, string jsonBody, CancellationToken ct = default)
    {
        var headers = new Dictionary<string, string>(DataverseHeaders) { ["Prefer"] = "return=representation" };
        // Parse-and-resend: validates the JSON up front and lets HttpGateway encode it.
        // JsonContent.Create produces UTF-8 with charset header by default.
        using var doc = JsonDocument.Parse(jsonBody);
        return HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Post, _baseUrl + entitySet,
            body: doc.RootElement.Clone(),
            headers: headers,
            bearerToken: _token,
            ct: ct);
    }

    /// <summary>PATCH a record by id from caller-formatted JSON. Returns no body (Dataverse 204).</summary>
    public Task UpdateAsync(string entitySet, string id, string jsonBody, CancellationToken ct = default)
    {
        // Parse first to fail fast on malformed JSON before the round-trip.
        using var _ = JsonDocument.Parse(jsonBody);
        return HttpGateway.SendStringNoContentAsync(
            HttpMethod.Patch, _baseUrl + $"{entitySet}({id})",
            body: jsonBody,
            contentType: "application/json",
            headers: DataverseHeaders,
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

    // -------------------------------------------------------------- Schema mutation

    /// <summary>
    /// POST a metadata body (typed object, serialized via <see cref="HttpGateway.JsonOptions"/>).
    /// Used for creating tables, columns, choices, relationships, and invoking custom actions.
    /// Optional <paramref name="solutionUniqueName"/> sets the <c>MSCRM.SolutionUniqueName</c>
    /// header so the new component lands in the named solution.
    /// </summary>
    public Task<JsonElement> PostMetadataAsync(string path, object body, string? solutionUniqueName = null, CancellationToken ct = default)
    {
        var headers = BuildMetadataHeaders(solutionUniqueName, mergeLabels: false);
        return HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Post, _baseUrl + path,
            body: body,
            headers: headers,
            bearerToken: _token,
            jsonOptions: HttpGateway.MetadataJsonOptions,
            ct: ct);
    }

    /// <summary>
    /// PUT a metadata body (typed object). Used for table/column/choice updates. Sets
    /// <c>MSCRM.MergeLabels: true</c> so localized labels are merged rather than replaced
    /// (Dataverse default is replace, which would wipe non-EN labels on partial updates).
    /// </summary>
    public Task PutMetadataAsync(string path, object body, string? solutionUniqueName = null, CancellationToken ct = default)
    {
        var headers = BuildMetadataHeaders(solutionUniqueName, mergeLabels: true);
        // PUT to /EntityDefinitions returns 204 — use the no-content path. Serialize the
        // typed body with the same JsonContent helper used elsewhere so we never hand-roll JSON.
        return SendJsonNoContentAsync(HttpMethod.Put, _baseUrl + path, body, headers, ct);
    }

    /// <summary>DELETE a metadata path (e.g. <c>EntityDefinitions(metadataId)</c>).</summary>
    public Task DeleteMetadataAsync(string path, CancellationToken ct = default)
        => HttpGateway.SendNoContentAsync(
            HttpMethod.Delete, _baseUrl + path,
            headers: DataverseHeaders,
            bearerToken: _token,
            ct: ct);

    /// <summary>
    /// Invoke an unbound action (e.g. <c>PublishXml</c>, <c>CreatePolymorphicLookupAttribute</c>,
    /// <c>UpdateOptionValue</c>). Returns the parsed response body — many actions return useful
    /// structured data like new attribute IDs.
    /// </summary>
    public Task<JsonElement> InvokeActionAsync(string actionName, object body, string? solutionUniqueName = null, CancellationToken ct = default)
    {
        var headers = BuildMetadataHeaders(solutionUniqueName, mergeLabels: false);
        return HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Post, _baseUrl + actionName,
            body: body,
            headers: headers,
            bearerToken: _token,
            jsonOptions: HttpGateway.MetadataJsonOptions,
            ct: ct);
    }

    // -------------------------------------------------------------- Re-read helpers (for SilentSkipGuard)

    /// <summary>Returns the EntityDefinition for a logical name, or null on 404.</summary>
    public Task<JsonElement?> TryGetEntityDefinitionAsync(string logicalName, CancellationToken ct = default)
        => TryGetJsonAsync($"EntityDefinitions(LogicalName='{OData.EscapeLiteral(logicalName)}')", ct);

    /// <summary>Returns the AttributeDefinition for a column, or null on 404.</summary>
    public Task<JsonElement?> TryGetAttributeAsync(string table, string column, CancellationToken ct = default)
        => TryGetJsonAsync(
            $"EntityDefinitions(LogicalName='{OData.EscapeLiteral(table)}')/Attributes(LogicalName='{OData.EscapeLiteral(column)}')",
            ct);

    /// <summary>Returns the global option set definition, or null on 404.</summary>
    public Task<JsonElement?> TryGetGlobalOptionSetAsync(string name, CancellationToken ct = default)
        => TryGetJsonAsync($"GlobalOptionSetDefinitions(Name='{OData.EscapeLiteral(name)}')", ct);

    /// <summary>
    /// Returns the relationship metadata for either a 1:N or N:N relationship by SchemaName,
    /// or null on 404. Dataverse exposes this via the polymorphic <c>RelationshipDefinitions</c>
    /// set; the response @odata.type tells you which kind it is.
    /// </summary>
    public Task<JsonElement?> TryGetRelationshipAsync(string schemaName, CancellationToken ct = default)
        => TryGetJsonAsync($"RelationshipDefinitions(SchemaName='{OData.EscapeLiteral(schemaName)}')", ct);

    private async Task<JsonElement?> TryGetJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            return await GetJsonAsync(path, ct);
        }
        catch (HttpFailure ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // -------------------------------------------------------------- Internal helpers

    /// <summary>Build the header set for metadata mutations: standard Dataverse + optional solution + optional MergeLabels.</summary>
    private static Dictionary<string, string> BuildMetadataHeaders(string? solutionUniqueName, bool mergeLabels)
    {
        var headers = new Dictionary<string, string>(DataverseHeaders);
        if (!string.IsNullOrEmpty(solutionUniqueName))
            headers["MSCRM.SolutionUniqueName"] = solutionUniqueName;
        if (mergeLabels)
            headers["MSCRM.MergeLabels"] = "true";
        return headers;
    }

    /// <summary>
    /// Send a typed body and discard the response. Uses MetadataJsonOptions so PascalCase
    /// names go through verbatim (matches Dataverse's metadata-API conventions).
    /// </summary>
    private async Task SendJsonNoContentAsync(HttpMethod method, string url, object body, IDictionary<string, string> headers, CancellationToken ct)
    {
        await HttpGateway.SendJsonForJsonElementAsync(
            method, url,
            body: body,
            headers: headers,
            bearerToken: _token,
            jsonOptions: HttpGateway.MetadataJsonOptions,
            ct: ct);
    }

    // -------------------------------------------------------------- Metadata

    /// <summary>List all attributes on a table via the EntityDefinitions endpoint.</summary>
    public Task<JsonElement> GetEntityAttributesAsync(string tableLogicalName, CancellationToken ct = default)
        => GetJsonAsync($"EntityDefinitions(LogicalName='{OData.EscapeLiteral(tableLogicalName)}')/Attributes", ct);

    /// <summary>List all entity definitions, optionally filtered to custom only.</summary>
    public Task<JsonElement> ListEntityDefinitionsAsync(bool customOnly, CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "LogicalName,SchemaName,DisplayName,DisplayCollectionName,EntitySetName,IsCustomEntity"),
            new("$filter", customOnly ? "IsCustomEntity eq true" : null),
        });
        return GetJsonAsync("EntityDefinitions" + qs, ct);
    }

    /// <summary>Full EntityDefinition (with Attributes expanded) for one table.</summary>
    public Task<JsonElement> GetEntityDefinitionAsync(string tableLogicalName, CancellationToken ct = default)
    {
        var path = $"EntityDefinitions(LogicalName='{OData.EscapeLiteral(tableLogicalName)}')";
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$expand", "Attributes($select=LogicalName,AttributeType,RequiredLevel,IsValidForCreate,IsValidForUpdate,Description)"),
        });
        return GetJsonAsync(path + qs, ct);
    }

    /// <summary>List global option sets (choices), optionally one by name.</summary>
    public Task<JsonElement> GetGlobalOptionSetsAsync(string? name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var qs = QueryString.Build(new KeyValuePair<string, string?>[]
            {
                new("$select", "Name,DisplayName,Description,OptionSetType"),
            });
            return GetJsonAsync("GlobalOptionSetDefinitions" + qs, ct);
        }
        return GetJsonAsync($"GlobalOptionSetDefinitions(Name='{OData.EscapeLiteral(name)}')", ct);
    }

    /// <summary>Download the CSDL `$metadata` document as raw UTF-8 XML bytes.</summary>
    public Task<byte[]> GetCsdlMetadataAsync(CancellationToken ct = default)
        => GetBytesAsync("$metadata", accept: "application/xml", ct);

    // -------------------------------------------------------------- Security

    /// <summary>List security roles (id + name + business unit).</summary>
    public Task<JsonElement> ListRolesAsync(CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "roleid,name"),
            new("$expand", "businessunitid($select=name)"),
            new("$orderby", "name asc"),
        });
        return GetJsonAsync("roles" + qs, ct);
    }

    /// <summary>Roles assigned to a user, by user id.</summary>
    public Task<JsonElement> GetUserRolesAsync(string userId, CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "roleid,name"),
            new("$expand", "businessunitid($select=name)"),
        });
        return GetJsonAsync($"systemusers({userId})/systemuserroles_association" + qs, ct);
    }

    /// <summary>roleprivileges_association rows for a role (the privileges + access right).</summary>
    public Task<JsonElement> GetRolePrivilegesAsync(string roleId, CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "privilegeid,name,accessright"),
        });
        return GetJsonAsync($"roles({roleId})/roleprivileges_association" + qs, ct);
    }

    /// <summary>
    /// Depth bitmask for each privilege held by a role (for User/BU/Parent-BU/Org).
    /// `roleprivilegescollection` is a virtual entity exposing `roleid` and `privilegedepthmask`
    /// directly (no underscore-prefixed lookup field, unlike most relations).
    /// </summary>
    public Task<JsonElement> GetRolePrivilegeDepthsAsync(string roleId, CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "privilegeid,privilegedepthmask"),
            new("$filter", $"roleid eq {roleId}"),
        });
        return GetJsonAsync("roleprivilegescollection" + qs, ct);
    }

    /// <summary>Resolve a user by exact id, email, or partial display name. Email match is case-insensitive.</summary>
    public Task<JsonElement> FindUsersAsync(string nameOrEmailOrId, CancellationToken ct = default)
    {
        if (Guid.TryParse(nameOrEmailOrId, out _))
            return GetJsonAsync($"systemusers({nameOrEmailOrId})", ct);

        var hasAt = nameOrEmailOrId.Contains('@');
        var escaped = OData.EscapeLiteral(nameOrEmailOrId);

        // `eq` is case-sensitive on internalemailaddress (e.g. "David.Wright@..." won't match
        // "david.wright@..."), and `tolower()` is not implemented on Dataverse's OData. Fall back
        // to `contains()` which Dataverse implements as a case-insensitive search — slightly
        // looser semantics, but the trade-off is acceptable for a lookup by-email use case.
        var filter = hasAt
            ? $"contains(internalemailaddress,'{escaped}')"
            : $"contains(fullname,'{escaped}')";

        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "systemuserid,fullname,internalemailaddress,isdisabled"),
            new("$filter", filter),
        });
        return GetJsonAsync("systemusers" + qs, ct);
    }

    /// <summary>Resolve a role by exact id or partial name. Returns matches.</summary>
    public Task<JsonElement> FindRolesAsync(string nameOrId, CancellationToken ct = default)
    {
        if (Guid.TryParse(nameOrId, out _))
            return GetJsonAsync($"roles({nameOrId})", ct);

        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "roleid,name"),
            new("$filter", $"contains(name,'{OData.EscapeLiteral(nameOrId)}')"),
        });
        return GetJsonAsync("roles" + qs, ct);
    }

    // -------------------------------------------------------------- WhoAmI

    /// <summary>WhoAmI returns UserId/BU/Org.</summary>
    public Task<JsonElement> WhoAmIAsync(CancellationToken ct = default)
        => GetJsonAsync("WhoAmI", ct);

    // -------------------------------------------------------------- Solution lifecycle

    /// <summary>List unmanaged or all solutions (filtered to visible ones).</summary>
    public Task<JsonElement> ListSolutionsAsync(bool unmanagedOnly, CancellationToken ct = default)
    {
        var filterParts = new List<string> { "isvisible eq true" };
        if (unmanagedOnly) filterParts.Add("ismanaged eq false");

        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "solutionid,uniquename,friendlyname,version,ismanaged,installedon"),
            new("$filter", string.Join(" and ", filterParts)),
            new("$orderby", "uniquename asc"),
        });
        return GetJsonAsync("solutions" + qs, ct);
    }

    /// <summary>Look up a solution by unique name. Returns null when no match.</summary>
    public async Task<JsonElement?> TryGetSolutionAsync(string uniqueName, CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "solutionid,uniquename,friendlyname,version,ismanaged"),
            new("$filter", $"uniquename eq '{OData.EscapeLiteral(uniqueName)}'"),
        });
        var root = await GetJsonAsync("solutions" + qs, ct);
        if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in arr.EnumerateArray()) return row.Clone();
        }
        return null;
    }

    /// <summary>Look up a publisher by unique name. Returns null when no match.</summary>
    public async Task<JsonElement?> TryGetPublisherAsync(string uniqueName, CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$select", "publisherid,uniquename,friendlyname,customizationprefix"),
            new("$filter", $"uniquename eq '{OData.EscapeLiteral(uniqueName)}'"),
        });
        var root = await GetJsonAsync("publishers" + qs, ct);
        if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in arr.EnumerateArray()) return row.Clone();
        }
        return null;
    }

    /// <summary>Get one import job by id. Returns null on 404.</summary>
    public async Task<JsonElement?> TryGetImportJobAsync(Guid jobId, CancellationToken ct = default)
    {
        try { return await GetJsonAsync($"importjobs({jobId})", ct); }
        catch (HttpFailure ex) when (ex.Status == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public void Dispose() { /* no per-instance resources */ }
}
