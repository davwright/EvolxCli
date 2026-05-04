using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Evolx.Cli.Http;

/// <summary>
/// The single HTTP entry point. Every request from every component goes through here.
/// Handles body encoding, content-type, response decoding, retry on 429/503,
/// deprecation-header surfacing, and structured failure reporting.
///
/// Fails fast: non-2xx after retries throws HttpFailure. Never silently swallows.
/// Caller never writes try/catch around HTTP — exceptions propagate to Spectre,
/// which prints them and exits non-zero. Fix and iterate.
/// </summary>
public static class HttpGateway
{
    /// <summary>One HttpClient per process — modern best practice (DNS rotation aside).</summary>
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(100),  // Same as default; overridable per-request via CT
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // -------------------------------------------------------------- High-level API

    /// <summary>JSON in, JSON out (deserialized to T).</summary>
    public static async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string url,
        object? body = null,
        IDictionary<string, string>? headers = null,
        string? bearerToken = null,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var content = body == null
            ? null
            : JsonContent.Create(body, options: JsonOptions);

        if (content != null && !string.IsNullOrEmpty(contentType))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        var resp = await SendCoreAsync(method, url, content, headers, bearerToken, ct);
        return await DeserializeBodyAsync<T>(resp, method, url, ct);
    }

    /// <summary>JSON in, response is JsonElement (caller picks fields manually).</summary>
    public static async Task<JsonElement> SendJsonForJsonElementAsync(
        HttpMethod method,
        string url,
        object? body = null,
        IDictionary<string, string>? headers = null,
        string? bearerToken = null,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var content = body == null
            ? null
            : JsonContent.Create(body, options: JsonOptions);

        if (content != null && !string.IsNullOrEmpty(contentType))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        var resp = await SendCoreAsync(method, url, content, headers, bearerToken, ct);
        return await ReadJsonElementAsync(resp, method, url, ct);
    }

    /// <summary>Raw string body in, deserialized T out. For pre-serialized JSON or non-object bodies.</summary>
    public static async Task<T> SendStringAsync<T>(
        HttpMethod method,
        string url,
        string body,
        string contentType,
        IDictionary<string, string>? headers = null,
        string? bearerToken = null,
        CancellationToken ct = default)
    {
        var content = new StringContent(body, Encoding.UTF8, contentType);
        var resp = await SendCoreAsync(method, url, content, headers, bearerToken, ct);
        return await DeserializeBodyAsync<T>(resp, method, url, ct);
    }

    /// <summary>Binary body in (raw bytes + content type), bytes out. For msapp/zip/blob transfer.</summary>
    public static async Task<byte[]> SendBytesAsync(
        HttpMethod method,
        string url,
        byte[]? body = null,
        string? contentType = null,
        IDictionary<string, string>? headers = null,
        string? bearerToken = null,
        CancellationToken ct = default)
    {
        HttpContent? content = null;
        if (body != null)
        {
            content = new ByteArrayContent(body);
            if (!string.IsNullOrEmpty(contentType))
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        var resp = await SendCoreAsync(method, url, content, headers, bearerToken, ct);
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Send with no body, expect no response body. For DELETE etc.</summary>
    public static async Task SendNoContentAsync(
        HttpMethod method,
        string url,
        IDictionary<string, string>? headers = null,
        string? bearerToken = null,
        CancellationToken ct = default)
    {
        var resp = await SendCoreAsync(method, url, content: null, headers, bearerToken, ct);
        // Drain so the connection is reusable
        await resp.Content.ReadAsByteArrayAsync(ct);
    }

    // -------------------------------------------------------------- Body parsing

    /// <summary>
    /// Reads the response body as a string (network streams don't support .Length, so we
    /// can't shortcut on size — buffer once, parse from buffer). Returns default(T) only
    /// when the body is genuinely empty (204 No Content / empty 200).
    /// </summary>
    private static async Task<T> DeserializeBodyAsync<T>(
        HttpResponseMessage resp, HttpMethod method, string url, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrEmpty(body)) return default!;

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
        }
        catch (JsonException ex)
        {
            throw new HttpFailure(method.Method, url, resp.StatusCode, body,
                CaptureHeaders(resp), 1,
                $"Response body was not valid JSON for type {typeof(T).Name}.", ex);
        }
    }

    private static async Task<JsonElement> ReadJsonElementAsync(
        HttpResponseMessage resp, HttpMethod method, string url, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrEmpty(body)) return default;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new HttpFailure(method.Method, url, resp.StatusCode, body,
                CaptureHeaders(resp), 1, "Response body was not valid JSON.", ex);
        }
    }

    // -------------------------------------------------------------- Internals

    /// <summary>
    /// The one place where every request actually gets sent. Handles auth header,
    /// retry on 429/503, deprecation header surfacing, and converting non-2xx into
    /// HttpFailure. Returns the raw response so the typed wrappers above can
    /// deserialize the body in whatever shape they want.
    /// </summary>
    private static async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string url,
        HttpContent? content,
        IDictionary<string, string>? headers,
        string? bearerToken,
        CancellationToken ct)
    {
        const int maxAttempts = RetryPolicy.DefaultMaxAttempts;
        HttpResponseMessage? resp = null;
        Exception? lastInner = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // HttpRequestMessage (and the content stream) can't be reused after Send,
            // so we rebuild per attempt.
            using var req = new HttpRequestMessage(method, url);
            if (content != null) req.Content = await CloneContentAsync(content, ct);
            if (!string.IsNullOrEmpty(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            if (headers != null)
            {
                foreach (var (k, v) in headers)
                {
                    if (k.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && req.Content != null)
                    {
                        req.Content.Headers.TryAddWithoutValidation(k, v);
                    }
                    else
                    {
                        req.Headers.TryAddWithoutValidation(k, v);
                    }
                }
            }

            try
            {
                resp = await SharedClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex)
            {
                // Network-level failures: only retry the first time, otherwise bail
                lastInner = ex;
                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                    continue;
                }
                throw new HttpFailure(method.Method, url, status: null, responseBody: null,
                    headers: null, attempts: attempt,
                    message: $"Network failure: {ex.Message}", inner: ex);
            }

            // Surface deprecation signals for ALL responses (success or failure)
            DeprecationDetector.Inspect(resp);

            if (resp.IsSuccessStatusCode) return resp;

            if (RetryPolicy.ShouldRetry(resp, attempt, maxAttempts))
            {
                var delay = RetryPolicy.ComputeDelay(resp, attempt);
                resp.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }

            // Non-retryable failure: capture everything and throw
            var body = await ReadBodyAsync(resp, ct);
            var hdrs = CaptureHeaders(resp);
            var status = resp.StatusCode;
            resp.Dispose();
            throw new HttpFailure(method.Method, url, status, body, hdrs, attempt,
                $"{(int)status} {status}");
        }

        // We exhausted retries on a retryable status (e.g. always 429). Throw with the last response.
        if (resp != null)
        {
            var body = await ReadBodyAsync(resp, ct);
            var hdrs = CaptureHeaders(resp);
            var status = resp.StatusCode;
            resp.Dispose();
            throw new HttpFailure(method.Method, url, status, body, hdrs, maxAttempts,
                $"Exhausted {maxAttempts} retries; last response was {(int)status} {status}.");
        }

        throw new HttpFailure(method.Method, url, null, null, null, maxAttempts,
            "Exhausted retries with no response captured.", lastInner);
    }

    /// <summary>
    /// Re-read content into a buffer so it can be replayed on retry. For tiny request bodies
    /// (typical for our REST calls) this is fine; if we ever ship binary uploads through
    /// the gateway we may want a streaming-aware variant.
    /// </summary>
    private static async Task<HttpContent> CloneContentAsync(HttpContent original, CancellationToken ct)
    {
        var bytes = await original.ReadAsByteArrayAsync(ct);
        var clone = new ByteArrayContent(bytes);
        foreach (var h in original.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return clone;
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return null; }
    }

    private static Dictionary<string, string> CaptureHeaders(HttpResponseMessage resp)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in resp.Headers) dict[h.Key] = string.Join(", ", h.Value);
        if (resp.Content != null)
        {
            foreach (var h in resp.Content.Headers) dict[h.Key] = string.Join(", ", h.Value);
        }
        return dict;
    }
}
