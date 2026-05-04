using System.Net;
using System.Net.Http.Headers;
using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Http;

public sealed class HttpGatewayTests
{
    private record Probe(int Id, string Name);

    [Fact]
    public async Task Get_200_returns_deserialized_T()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.OK, """{"id":1,"name":"foo"}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var result = await HttpGateway.SendJsonAsync<Probe>(HttpMethod.Get, "https://x/y");

        result.Should().Be(new Probe(1, "foo"));
        fake.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task NoContent_returns_default()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NoContent);
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var result = await HttpGateway.SendJsonAsync<Probe?>(HttpMethod.Get, "https://x/y");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Retries_on_429_with_RetryAfter_then_succeeds()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.TooManyRequests, mutate: r =>
                r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)))
            .EnqueueStatus(HttpStatusCode.OK, """{"id":2,"name":"after-retry"}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var result = await HttpGateway.SendJsonAsync<Probe>(HttpMethod.Get, "https://x/y");

        result.Should().Be(new Probe(2, "after-retry"));
        fake.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Throws_HttpFailure_after_3_retries_on_persistent_429()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.TooManyRequests, """{"throttled":true}""",
                mutate: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)))
            .EnqueueStatus(HttpStatusCode.TooManyRequests, """{"throttled":true}""",
                mutate: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)))
            .EnqueueStatus(HttpStatusCode.TooManyRequests, """{"throttled":true}""",
                mutate: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)));
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        Func<Task> act = () => HttpGateway.SendJsonAsync<Probe>(HttpMethod.Get, "https://x/y");

        var ex = (await act.Should().ThrowAsync<HttpFailure>()).Which;
        ex.Status.Should().Be(HttpStatusCode.TooManyRequests);
        ex.Attempts.Should().Be(3);
        ex.ResponseBody.Should().Contain("throttled");
        fake.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Non_retryable_status_throws_with_full_diagnostic_context()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.NotFound, """{"error":{"code":"0x80060888","message":"Could not find table 'foo'."}}""",
                mutate: r => r.Headers.TryAddWithoutValidation("x-ms-correlation-request-id", "abc-123"));
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        Func<Task> act = () => HttpGateway.SendJsonAsync<Probe>(HttpMethod.Get, "https://x/missing");

        var ex = (await act.Should().ThrowAsync<HttpFailure>()).Which;
        ex.Status.Should().Be(HttpStatusCode.NotFound);
        ex.Method.Should().Be("GET");
        ex.Url.Should().Be("https://x/missing");
        ex.ResponseBody.Should().Contain("0x80060888");
        ex.ResponseHeaders.Should().ContainKey("x-ms-correlation-request-id");
        ex.Attempts.Should().Be(1); // not retried

        // ToString() must format multi-line for stderr
        var formatted = ex.ToString();
        formatted.Should().Contain("HTTP failure");
        formatted.Should().Contain("404");
        formatted.Should().Contain("https://x/missing");
        formatted.Should().Contain("0x80060888");
    }

    [Fact]
    public async Task BadJson_in_response_throws_HttpFailure_not_silent()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.OK, "this is not json at all");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        Func<Task> act = () => HttpGateway.SendJsonAsync<Probe>(HttpMethod.Get, "https://x/y");

        var ex = (await act.Should().ThrowAsync<HttpFailure>()).Which;
        ex.Message.Should().Contain("not valid JSON");
        ex.ResponseBody.Should().Contain("not json");
    }

    [Fact]
    public async Task Network_failure_propagates_as_HttpFailure_with_inner()
    {
        var fake = new FakeHttpHandler();
        // Three queued exceptions because gateway retries a network failure once with backoff
        fake.EnqueueException(new HttpRequestException("DNS failure"));
        fake.EnqueueException(new HttpRequestException("DNS failure"));
        fake.EnqueueException(new HttpRequestException("DNS failure"));
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        Func<Task> act = () => HttpGateway.SendJsonAsync<Probe>(HttpMethod.Get, "https://x/y");

        var ex = (await act.Should().ThrowAsync<HttpFailure>()).Which;
        ex.Status.Should().BeNull(); // no response received
        ex.Message.Should().Contain("Network failure");
        ex.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Bearer_token_is_attached_when_provided()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        await HttpGateway.SendJsonAsync<object>(HttpMethod.Get, "https://x/y", bearerToken: "abc.def.ghi");

        fake.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        fake.Requests[0].Headers.Authorization.Parameter.Should().Be("abc.def.ghi");
    }

    [Fact]
    public async Task Custom_headers_round_trip()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var headers = new Dictionary<string, string>
        {
            ["OData-MaxVersion"] = "4.0",
            ["Prefer"] = "return=representation",
        };

        await HttpGateway.SendJsonAsync<object>(HttpMethod.Get, "https://x/y", headers: headers);

        fake.Requests[0].Headers.GetValues("OData-MaxVersion").Should().ContainSingle("4.0");
        fake.Requests[0].Headers.GetValues("Prefer").Should().ContainSingle("return=representation");
    }

    [Fact]
    public async Task SendNoContent_for_DELETE_drains_response()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NoContent);
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        await HttpGateway.SendNoContentAsync(HttpMethod.Delete, "https://x/y/123");

        fake.Requests[0].Method.Should().Be(HttpMethod.Delete);
    }
}
