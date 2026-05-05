using System.Text.Json;
using Evolx.Cli.PowerPlatform;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Live;

[Trait("Category", "Live")]
public class PpLiveTests
{
    [Fact]
    public async Task Envs_returns_at_least_one_environment_with_a_url()
    {
        using var bap = await BapClient.CreateAsync();
        var result = await bap.ListEnvironmentsAsync();

        result.TryGetProperty("value", out var value).Should().BeTrue();
        value.ValueKind.Should().Be(JsonValueKind.Array);
        var envs = value.EnumerateArray().ToList();
        envs.Should().NotBeEmpty("the signed-in tenant has at least one environment");

        // At least one environment should have a Dataverse instanceUrl in linkedEnvironmentMetadata.
        envs.Any(HasInstanceUrl).Should().BeTrue(
            "at least one Power Platform environment is linked to a Dataverse instance");
    }

    private static bool HasInstanceUrl(JsonElement env)
    {
        if (!env.TryGetProperty("properties", out var p)) return false;
        if (!p.TryGetProperty("linkedEnvironmentMetadata", out var lem)) return false;
        if (lem.ValueKind != JsonValueKind.Object) return false;
        return lem.TryGetProperty("instanceUrl", out var iu)
            && iu.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(iu.GetString());
    }
}
