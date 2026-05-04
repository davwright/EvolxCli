using System.Net;

namespace Evolx.Cli.Http;

/// <summary>
/// Decides whether to retry an HTTP response and how long to wait. Honors Retry-After.
/// Bounded — caller passes max attempts. No silent infinite retry; if we exhaust
/// attempts we throw HttpFailure.
///
/// Retry only on transient failures:
///   429 Too Many Requests   — service-protection throttle (Dataverse) or Azure DevOps
///   503 Service Unavailable — transient backend
///   408 Request Timeout
///   504 Gateway Timeout
/// Everything else is fail-fast. 4xx other than 429/408 means our request is wrong;
/// retrying just hides the bug.
/// </summary>
public static class RetryPolicy
{
    public const int DefaultMaxAttempts = 3;

    public static bool ShouldRetry(HttpResponseMessage resp, int attempt, int maxAttempts)
    {
        if (attempt >= maxAttempts) return false;
        return resp.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => true,
            HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.GatewayTimeout => true,
            _ => false,
        };
    }

    /// <summary>
    /// How long to wait before the next attempt. Uses Retry-After if the server set
    /// it (most reliable); otherwise exponential backoff capped at 30s.
    /// </summary>
    public static TimeSpan ComputeDelay(HttpResponseMessage resp, int attempt)
    {
        // Retry-After: integer seconds OR HTTP-date. .NET parses both via the typed accessor.
        if (resp.Headers.RetryAfter != null)
        {
            if (resp.Headers.RetryAfter.Delta is { } delta) return delta;
            if (resp.Headers.RetryAfter.Date is { } when)
            {
                var diff = when - DateTimeOffset.UtcNow;
                if (diff > TimeSpan.Zero) return diff;
            }
        }

        // Fallback: 2^attempt seconds, capped at 30s
        var seconds = Math.Min(30, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds);
    }
}
