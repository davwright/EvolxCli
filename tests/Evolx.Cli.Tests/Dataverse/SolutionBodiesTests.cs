using System.Text.Json;
using Evolx.Cli.Dataverse;
using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

/// <summary>
/// Wire-shape assertions for the solution-lifecycle request bodies. These exist
/// to catch the camelCase-vs-PascalCase trap (data API expects camelCase, action API
/// expects PascalCase) and the @odata.bind name on publisher.
/// </summary>
public class SolutionBodiesTests
{
    [Fact]
    public void SolutionCreateBody_uses_camelCase_and_preserves_odata_bind_name()
    {
        var body = new SolutionCreateBody
        {
            UniqueName = "EvoTest",
            FriendlyName = "Evo Test",
            Version = "1.2.3.4",
            Description = "desc",
            PublisherIdBind = "/publishers(11111111-2222-3333-4444-555555555555)",
        };

        var json = JsonSerializer.Serialize(body, HttpGateway.JsonOptions);
        using var doc = JsonDocument.Parse(json);

        // camelCase from JsonNamingPolicy.CamelCase
        doc.RootElement.GetProperty("uniqueName").GetString().Should().Be("EvoTest");
        doc.RootElement.GetProperty("friendlyName").GetString().Should().Be("Evo Test");
        doc.RootElement.GetProperty("version").GetString().Should().Be("1.2.3.4");

        // The publisherid binding has an explicit JsonPropertyName so it overrides the policy.
        doc.RootElement.GetProperty("publisherid@odata.bind").GetString()
            .Should().Be("/publishers(11111111-2222-3333-4444-555555555555)");
    }

    [Fact]
    public void ImportSolutionBody_serializes_with_pascalCase_for_action_endpoint()
    {
        var body = new ImportSolutionBody
        {
            CustomizationFile = "AAA=",
            OverwriteUnmanagedCustomizations = true,
            PublishWorkflows = true,
            ImportJobId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        };

        var json = JsonSerializer.Serialize(body, HttpGateway.MetadataJsonOptions);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("CustomizationFile").GetString().Should().Be("AAA=");
        doc.RootElement.GetProperty("OverwriteUnmanagedCustomizations").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("PublishWorkflows").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("ImportJobId").GetGuid()
            .Should().Be(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    }

    [Fact]
    public void ExportSolutionBody_defaults_to_unmanaged_lean_flags()
    {
        var body = new ExportSolutionBody { SolutionName = "EvoTest" };
        var json = JsonSerializer.Serialize(body, HttpGateway.MetadataJsonOptions);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("SolutionName").GetString().Should().Be("EvoTest");
        doc.RootElement.GetProperty("Managed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void RemoveSolutionComponentBody_carries_int_componenttype()
    {
        var body = new RemoveSolutionComponentBody
        {
            ComponentId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ComponentType = 61, // WebResource
            SolutionUniqueName = "EvoTest",
        };
        var json = JsonSerializer.Serialize(body, HttpGateway.MetadataJsonOptions);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("ComponentType").GetInt32().Should().Be(61);
        doc.RootElement.GetProperty("SolutionUniqueName").GetString().Should().Be("EvoTest");
    }
}
