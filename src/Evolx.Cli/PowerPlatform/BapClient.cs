using System.Text.Json;
using Evolx.Cli.Auth;
using Evolx.Cli.Http;

namespace Evolx.Cli.PowerPlatform;

/// <summary>
/// Power Platform admin REST (BAP) — used for tenant-wide reads like environment list.
/// Auth via the same `az` token broker, scoped to the PowerApps service resource.
/// </summary>
public sealed class BapClient : IDisposable
{
    /// <summary>Resource id for `az account get-access-token` to mint a BAP-bound token.</summary>
    public const string Resource = "https://service.powerapps.com/";

    private const string BaseUrl = "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/";
    private const string ApiVersion = "2020-10-01";

    private readonly string _token;

    private BapClient(string token) { _token = token; }

    public static async Task<BapClient> CreateAsync(CancellationToken ct = default)
    {
        var token = await AzAuth.GetAccessTokenAsync(Resource, ct);
        return new BapClient(token);
    }

    /// <summary>List all environments visible to the signed-in user (admin scope).</summary>
    public Task<JsonElement> ListEnvironmentsAsync(CancellationToken ct = default)
    {
        var qs = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("api-version", ApiVersion),
        });
        return HttpGateway.SendJsonForJsonElementAsync(
            HttpMethod.Get, BaseUrl + "scopes/admin/environments" + qs,
            bearerToken: _token,
            ct: ct);
    }

    public void Dispose() { /* no per-instance resources */ }
}
