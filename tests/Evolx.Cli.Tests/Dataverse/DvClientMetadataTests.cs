using System.Net;
using Evolx.Cli.Dataverse;
using Evolx.Cli.Http;
using Evolx.Cli.Tests.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

[Collection(HttpClientCollection.Name)]
public sealed class DvClientMetadataTests
{
    private const string Env = "https://x.crm4.dynamics.com";

    [Fact]
    public async Task PostMetadataAsync_sends_solution_header_when_provided()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NoContent);
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        await dv.PostMetadataAsync("EntityDefinitions", new { SchemaName = "evo_x" }, solutionUniqueName: "ev_test_delete");

        var req = fake.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.ToString().Should().EndWith("/api/data/v9.2/EntityDefinitions");
        req.Headers.GetValues("MSCRM.SolutionUniqueName").Should().ContainSingle().Which.Should().Be("ev_test_delete");
        // No MergeLabels on POST — that's a PUT-only header.
        req.Headers.Contains("MSCRM.MergeLabels").Should().BeFalse();
    }

    [Fact]
    public async Task PutMetadataAsync_sends_MergeLabels_true_and_solution_header()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NoContent);
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        await dv.PutMetadataAsync(
            "EntityDefinitions(LogicalName='evo_x')",
            new { HasChanged = true },
            solutionUniqueName: "ev_test_delete");

        var req = fake.Requests[0];
        req.Method.Should().Be(HttpMethod.Put);
        req.Headers.GetValues("MSCRM.MergeLabels").Should().ContainSingle().Which.Should().Be("true");
        req.Headers.GetValues("MSCRM.SolutionUniqueName").Should().ContainSingle().Which.Should().Be("ev_test_delete");
    }

    [Fact]
    public async Task TryGetEntityDefinitionAsync_returns_null_on_404()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.NotFound, """{"error":{"message":"Not found"}}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await dv.TryGetEntityDefinitionAsync("evo_missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryGetEntityDefinitionAsync_returns_value_on_200()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.OK, """{"LogicalName":"evo_x","SchemaName":"evo_x"}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await dv.TryGetEntityDefinitionAsync("evo_x");

        result.Should().NotBeNull();
        result!.Value.GetProperty("SchemaName").GetString().Should().Be("evo_x");
    }

    [Fact]
    public async Task TryGetAttributeAsync_propagates_500_as_HttpFailure()
    {
        var fake = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.InternalServerError, """{"error":{"message":"boom"}}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        Func<Task> act = () => dv.TryGetAttributeAsync("evo_x", "evo_y");

        await act.Should().ThrowAsync<HttpFailure>();
    }

    [Fact]
    public async Task DeleteMetadataAsync_sends_DELETE_no_body()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NoContent);
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        await dv.DeleteMetadataAsync("EntityDefinitions(00000000-0000-0000-0000-000000000001)");

        var req = fake.Requests[0];
        req.Method.Should().Be(HttpMethod.Delete);
        req.Content.Should().BeNull();
    }

    [Fact]
    public async Task InvokeActionAsync_posts_to_action_path_with_body()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK, """{"AttributeId":"abc"}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await dv.InvokeActionAsync("PublishXml", new { ParameterXml = "<importexportxml/>" });

        var req = fake.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.ToString().Should().EndWith("/api/data/v9.2/PublishXml");
        var body = await req.Content!.ReadAsStringAsync();
        body.Should().Contain("ParameterXml");
        result.GetProperty("AttributeId").GetString().Should().Be("abc");
    }
}
