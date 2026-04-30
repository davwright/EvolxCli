using System.Collections.Concurrent;

namespace Evolx.Cli.Http;

/// <summary>
/// Surfaces Microsoft's API deprecation signals (RFC 9745 Deprecation, RFC 8594 Sunset,
/// and RFC 7234 Warning headers) to the user via a single yellow stderr line per
/// (method, path) per ev process.
///
/// Costs microseconds per response when nothing is deprecated. Spams nothing in steady
/// state. When a response IS deprecated, the user sees one line, can act on it, and
/// won't see it again in the same session.
/// </summary>
public static class DeprecationDetector
{
    // Dedup key: "GET /api/data/v9.2/evo_foo". Normalized so query strings don't matter.
    private static readonly ConcurrentDictionary<string, byte> WarnedKeys = new();

    public static void Inspect(HttpResponseMessage resp)
    {
        var key = MakeKey(resp);
        if (string.IsNullOrEmpty(key)) return;

        var lines = new List<string>();

        // RFC 9745 Deprecation: either a date or "true"
        if (resp.Headers.TryGetValues("Deprecation", out var deps))
        {
            var dep = deps.FirstOrDefault();
            if (!string.IsNullOrEmpty(dep))
            {
                var msg = $"[ev] DEPRECATED: {key}";
                if (dep != "true") msg += $" (since {dep})";

                if (resp.Headers.TryGetValues("Sunset", out var sunsets))
                {
                    var sunset = sunsets.FirstOrDefault();
                    if (!string.IsNullOrEmpty(sunset)) msg += $", removed by {sunset}";
                }
                lines.Add(msg);

                // Link header may carry the successor reference
                if (resp.Headers.TryGetValues("Link", out var links))
                {
                    var successor = links.FirstOrDefault(l => l.Contains("successor", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(successor)) lines.Add($"  See: {successor}");
                }
            }
        }

        // RFC 7234 Warning header (older style; still used by some Dataverse responses).
        // Code 299 = "Miscellaneous Persistent Warning" — this is what deprecation typically uses.
        foreach (var w in resp.Headers.Warning)
        {
            if (w.Code == 299)
            {
                lines.Add($"[ev] WARNING: {w.Text}");
            }
        }

        if (lines.Count == 0) return;

        // Only print if we haven't warned about this exact endpoint already this run.
        if (!WarnedKeys.TryAdd(key, 0)) return;

        foreach (var line in lines) Console.Error.WriteLine(line);
    }

    private static string MakeKey(HttpResponseMessage resp)
    {
        var req = resp.RequestMessage;
        if (req?.RequestUri == null) return "";
        // Use absolute path (no query string) so /foo?bar=1 and /foo?bar=2 dedupe to the same warning.
        return $"{req.Method.Method} {req.RequestUri.AbsolutePath}";
    }
}
