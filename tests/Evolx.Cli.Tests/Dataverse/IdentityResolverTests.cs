using System.Net;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Evolx.Cli.Http;
using Evolx.Cli.Tests.Http;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

[Collection(HttpClientCollection.Name)]
public sealed class IdentityResolverTests
{
    private const string Env = "https://x.crm4.dynamics.com";

    private static IDisposable Console(out TestConsole console)
    {
        console = new TestConsole();
        var prev = AnsiConsole.Console;
        AnsiConsole.Console = console;
        return new ConsoleRestorer(prev);
    }

    private sealed class ConsoleRestorer(IAnsiConsole prev) : IDisposable
    {
        public void Dispose() => AnsiConsole.Console = prev;
    }

    [Fact]
    public async Task ResolveRoleAsync_returns_single_match_from_array()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK,
            """{"value":[{"roleid":"00000000-0000-0000-0000-000000000001","name":"System Administrator"}]}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await IdentityResolver.ResolveRoleAsync(dv, "System Admin", default);

        result.Should().NotBeNull();
        result!.Value.Id.Should().Be("00000000-0000-0000-0000-000000000001");
        result.Value.Label.Should().Be("System Administrator");
    }

    [Fact]
    public async Task ResolveRoleAsync_returns_null_and_logs_on_zero_matches()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK, """{"value":[]}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);
        using var __ = Console(out var console);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await IdentityResolver.ResolveRoleAsync(dv, "Nonexistent", default);

        result.Should().BeNull();
        console.Output.Should().Contain("No role matched").And.Contain("Nonexistent");
    }

    [Fact]
    public async Task ResolveRoleAsync_returns_null_and_lists_candidates_on_multiple_matches()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK,
            """{"value":[{"roleid":"00000000-0000-0000-0000-000000000001","name":"Sales User"},{"roleid":"00000000-0000-0000-0000-000000000002","name":"Sales Manager"}]}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);
        using var __ = Console(out var console);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await IdentityResolver.ResolveRoleAsync(dv, "Sales", default);

        result.Should().BeNull();
        console.Output.Should().Contain("2 roles matched")
            .And.Contain("Sales User")
            .And.Contain("Sales Manager");
    }

    [Fact]
    public async Task ResolveRoleAsync_handles_single_object_response_for_GUID_input()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK,
            """{"roleid":"00000000-0000-0000-0000-00000000aaaa","name":"By Id"}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await IdentityResolver.ResolveRoleAsync(dv, "00000000-0000-0000-0000-00000000aaaa", default);

        result.Should().NotBeNull();
        result!.Value.Id.Should().Be("00000000-0000-0000-0000-00000000aaaa");
        result.Value.Label.Should().Be("By Id");
    }

    [Fact]
    public async Task ResolveUserAsync_uses_contains_email_filter_when_input_contains_at()
    {
        var fake = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.OK,
            """{"value":[{"systemuserid":"u-1","fullname":"Dave"}]}""");
        using var client = new HttpClient(fake);
        using var _ = HttpGateway.OverrideHttpClientForTesting(client);

        var dv = DvClient.ForTesting(Env, "token");
        var result = await IdentityResolver.ResolveUserAsync(dv, "dave@example.com", default);

        result.Should().NotBeNull();
        // We use `contains(internalemailaddress,...)` because Dataverse `eq` is case-sensitive
        // and `tolower()` isn't implemented. Verify the filter took that shape.
        var sentUrl = fake.Requests[0].RequestUri!.ToString();
        sentUrl.Should().Contain("contains")
            .And.Contain("internalemailaddress")
            .And.Contain("%27dave%40example.com%27");
    }
}
