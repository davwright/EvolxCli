using System.Net;
using System.Net.Http.Headers;
using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Http;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void ShouldRetry_only_on_transient_codes(HttpStatusCode status, bool expected)
    {
        var resp = new HttpResponseMessage(status);
        RetryPolicy.ShouldRetry(resp, attempt: 1, maxAttempts: 3).Should().Be(expected);
    }

    [Fact]
    public void ShouldRetry_returns_false_when_attempts_exhausted()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        RetryPolicy.ShouldRetry(resp, attempt: 3, maxAttempts: 3).Should().BeFalse();
    }

    [Fact]
    public void ComputeDelay_honors_RetryAfter_delta_seconds()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        RetryPolicy.ComputeDelay(resp, attempt: 1).Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void ComputeDelay_honors_RetryAfter_HTTP_date()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var when = DateTimeOffset.UtcNow.AddSeconds(5);
        resp.Headers.RetryAfter = new RetryConditionHeaderValue(when);

        var delay = RetryPolicy.ComputeDelay(resp, attempt: 1);
        // Parsed dates round to whole seconds; allow a generous slack.
        delay.Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ComputeDelay_falls_back_to_exponential_backoff_capped_at_30s()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests); // no RetryAfter set
        RetryPolicy.ComputeDelay(resp, attempt: 1).Should().Be(TimeSpan.FromSeconds(2));
        RetryPolicy.ComputeDelay(resp, attempt: 2).Should().Be(TimeSpan.FromSeconds(4));
        RetryPolicy.ComputeDelay(resp, attempt: 6).Should().Be(TimeSpan.FromSeconds(30)); // capped
        RetryPolicy.ComputeDelay(resp, attempt: 100).Should().Be(TimeSpan.FromSeconds(30)); // still capped
    }
}
