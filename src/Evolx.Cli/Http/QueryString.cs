namespace Evolx.Cli.Http;

/// <summary>
/// Builds URL query strings with proper percent-encoding via <see cref="System.Uri.EscapeDataString"/>.
/// Used by every REST client (Dataverse OData, BAP, ADO) — no inline string concat anywhere.
/// </summary>
internal static class QueryString
{
    /// <summary>
    /// Format a parameter list as `?k1=v1&amp;k2=v2`. Values are percent-encoded;
    /// keys are passed through (REST APIs use URL-safe key characters by convention).
    /// Empty/null values drop the parameter entirely.
    /// </summary>
    public static string Build(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        var parts = parameters
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}")
            .ToList();
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }
}
