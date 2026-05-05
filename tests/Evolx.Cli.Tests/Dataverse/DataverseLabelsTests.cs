using System.Text.Json;
using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class DataverseLabelsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void String_returns_value_for_string_property()
    {
        var el = Parse("""{"name":"Account"}""");
        DataverseLabels.String(el, "name").Should().Be("Account");
    }

    [Fact]
    public void String_returns_empty_when_missing_or_wrong_kind()
    {
        var el = Parse("""{"name":null,"count":5}""");
        DataverseLabels.String(el, "name").Should().BeEmpty();
        DataverseLabels.String(el, "count").Should().BeEmpty();
        DataverseLabels.String(el, "absent").Should().BeEmpty();
    }

    [Fact]
    public void Bool_reads_true_only_when_truly_true()
    {
        var el = Parse("""{"a":true,"b":false,"c":"true"}""");
        DataverseLabels.Bool(el, "a").Should().BeTrue();
        DataverseLabels.Bool(el, "b").Should().BeFalse();
        DataverseLabels.Bool(el, "c").Should().BeFalse(); // string, not bool
        DataverseLabels.Bool(el, "absent").Should().BeFalse();
    }

    [Fact]
    public void LocalizedLabel_reads_UserLocalizedLabel_Label()
    {
        var el = Parse("""{"DisplayName":{"UserLocalizedLabel":{"Label":"Konto","LanguageCode":1031}}}""");
        DataverseLabels.LocalizedLabel(el, "DisplayName").Should().Be("Konto");
    }

    [Fact]
    public void LocalizedLabel_returns_empty_when_chain_breaks()
    {
        var noLabel = Parse("""{"DisplayName":{"UserLocalizedLabel":null}}""");
        var noULL = Parse("""{"DisplayName":{}}""");
        var absent = Parse("""{}""");
        DataverseLabels.LocalizedLabel(noLabel, "DisplayName").Should().BeEmpty();
        DataverseLabels.LocalizedLabel(noULL, "DisplayName").Should().BeEmpty();
        DataverseLabels.LocalizedLabel(absent, "DisplayName").Should().BeEmpty();
    }

    [Fact]
    public void EnumValue_reads_nested_Value_string()
    {
        var el = Parse("""{"RequiredLevel":{"Value":"ApplicationRequired","CanBeChanged":false}}""");
        DataverseLabels.EnumValue(el, "RequiredLevel").Should().Be("ApplicationRequired");
    }
}
