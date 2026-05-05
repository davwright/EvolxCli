using System.Text.Json;
using Evolx.Cli.Dataverse;
using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class LocalizedLabelTests
{
    [Fact]
    public void Build_with_null_or_whitespace_en_returns_null()
    {
        LocalizedLabel.Build(null).Should().BeNull();
        LocalizedLabel.Build("").Should().BeNull();
        LocalizedLabel.Build("   ").Should().BeNull();
    }

    [Fact]
    public void Build_with_only_en_returns_single_entry_with_lcid_1033()
    {
        var label = LocalizedLabel.Build("Account");
        label.Should().NotBeNull();
        label!.LocalizedLabels.Should().HaveCount(1);
        label.LocalizedLabels[0].Label.Should().Be("Account");
        label.LocalizedLabels[0].LanguageCode.Should().Be(1033);
    }

    [Fact]
    public void Build_with_en_and_de_returns_two_entries_with_correct_lcids()
    {
        var label = LocalizedLabel.Build("Account", "Konto");
        label!.LocalizedLabels.Should().HaveCount(2);
        label.LocalizedLabels[0].LanguageCode.Should().Be(1033);
        label.LocalizedLabels[1].Label.Should().Be("Konto");
        label.LocalizedLabels[1].LanguageCode.Should().Be(1031);
    }

    [Fact]
    public void Build_drops_whitespace_de()
    {
        var label = LocalizedLabel.Build("Account", "  ");
        label!.LocalizedLabels.Should().HaveCount(1);
    }

    [Fact]
    public void Serializes_with_required_odata_type_on_each_entry()
    {
        var label = LocalizedLabel.Build("Account", "Konto");
        var json = JsonSerializer.Serialize(label, HttpGateway.MetadataJsonOptions);
        json.Should().Contain("\"@odata.type\":\"Microsoft.Dynamics.CRM.LocalizedLabel\"");
        json.Should().Contain("\"Label\":\"Account\"")
            .And.Contain("\"Label\":\"Konto\"")
            .And.Contain("\"LanguageCode\":1033")
            .And.Contain("\"LanguageCode\":1031");
    }
}
