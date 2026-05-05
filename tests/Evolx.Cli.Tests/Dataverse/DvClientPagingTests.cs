using System.Net;
using Evolx.Cli.Dataverse;
using Evolx.Cli.Http;
using Evolx.Cli.Tests.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

[Collection(HttpClientCollection.Name)]
public sealed class DvClientPagingTests
{
    private const string Env = "https://x.crm4.dynamics.com";

    private static FakeHttpHandler TwoPages()
    {
        var handler = new FakeHttpHandler();
        handler.EnqueueStatus(HttpStatusCode.OK,
            $$"""{"value":[{"id":1},{"id":2}],"@odata.nextLink":"{{Env}}/api/data/v9.2/accounts?$skiptoken=p2"}""");
        handler.EnqueueStatus(HttpStatusCode.OK,
            """{"value":[{"id":3}]}""");
        return handler;
    }

    [Fact]
    public async Task QueryPagedAsync_with_followAll_accumulates_every_page()
    {
        var fake = TwoPages();
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var pageCounts = new List<int>();
        var result = await dv.QueryPagedAsync(
            "accounts", filter: null, select: null,
            pageSize: 100, followAll: true,
            onPage: c => pageCounts.Add(c));

        result.Rows.Should().HaveCount(3);
        result.HasMore.Should().BeFalse();
        pageCounts.Should().Equal(2, 3); // running total reported per page
        fake.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryPagedAsync_without_followAll_returns_only_first_page_with_HasMore_true()
    {
        var fake = TwoPages();
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await dv.QueryPagedAsync(
            "accounts", filter: null, select: null,
            pageSize: 100, followAll: false);

        result.Rows.Should().HaveCount(2);
        result.HasMore.Should().BeTrue();
        fake.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task QueryPagedAsync_sends_maxpagesize_prefer_header()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.OK, """{"value":[]}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        await dv.QueryPagedAsync("accounts", null, null, pageSize: 250, followAll: false);

        fake.Requests[0].Headers.GetValues("Prefer").Should().ContainSingle()
            .Which.Should().Be("odata.maxpagesize=250");
    }

    [Fact]
    public async Task UpdateAsync_sends_PATCH_with_utf8_json_body()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NoContent);
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        await dv.UpdateAsync("evo_tours", "00000000-0000-0000-0000-000000000001",
            """{"name":"Über"}""");

        var req = fake.Requests[0];
        req.Method.Should().Be(HttpMethod.Patch);
        req.RequestUri!.ToString().Should().EndWith("evo_tours(00000000-0000-0000-0000-000000000001)");
        var body = await req.Content!.ReadAsStringAsync();
        body.Should().Be("""{"name":"Über"}"""); // round-trips UTF-8 multibyte chars cleanly
        req.Content.Headers.ContentType!.CharSet.Should().Be("utf-8");
    }

    [Fact]
    public async Task UpdateAsync_throws_on_malformed_json_before_round_trip()
    {
        var fake = new FakeHttpHandler();
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        Func<Task> act = () => dv.UpdateAsync("t", "id", "{not json");

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
        fake.Requests.Should().BeEmpty();
    }
}
