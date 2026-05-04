using System.Net;
using System.Text;

namespace Evolx.Cli.Http;

/// <summary>
/// Thrown by HttpGateway whenever a request can't be completed successfully (non-2xx
/// after all retries, network failure, deserialization failure). Captures everything
/// a human needs to diagnose: URL, method, status, response body, and key response
/// headers. ToString() formats it as a multi-line error block suitable for stderr.
///
/// Never caught inside the gateway or its callers. Propagates to the top of the
/// process; Spectre dumps it to stderr; exit code is non-zero. Fix and iterate.
/// </summary>
public sealed class HttpFailure : Exception
{
    public string Method { get; }
    public string Url { get; }
    public HttpStatusCode? Status { get; }
    public string? ResponseBody { get; }
    public IReadOnlyDictionary<string, string> ResponseHeaders { get; }
    public int Attempts { get; }

    public HttpFailure(
        string method,
        string url,
        HttpStatusCode? status,
        string? responseBody,
        IReadOnlyDictionary<string, string>? headers,
        int attempts,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        Method = method;
        Url = url;
        Status = status;
        ResponseBody = responseBody;
        ResponseHeaders = headers ?? new Dictionary<string, string>();
        Attempts = attempts;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"HTTP failure: {Message}");
        sb.AppendLine($"  {Method} {Url}");
        if (Status.HasValue) sb.AppendLine($"  Status: {(int)Status.Value} {Status.Value}");
        if (Attempts > 1) sb.AppendLine($"  Attempts: {Attempts}");

        // Surface a few headers that are commonly diagnostic
        foreach (var key in new[] { "x-ms-correlation-request-id", "request-id", "x-vss-e2eid", "Retry-After" })
        {
            if (ResponseHeaders.TryGetValue(key, out var v))
                sb.AppendLine($"  {key}: {v}");
        }

        if (!string.IsNullOrWhiteSpace(ResponseBody))
        {
            sb.AppendLine("  Response body:");
            foreach (var line in ResponseBody.TrimEnd().Split('\n'))
                sb.AppendLine($"    {line.TrimEnd()}");
        }

        if (InnerException != null)
            sb.AppendLine($"  Inner: {InnerException.GetType().Name}: {InnerException.Message}");

        return sb.ToString();
    }
}
