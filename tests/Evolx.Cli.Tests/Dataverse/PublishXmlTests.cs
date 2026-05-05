using System.Xml.Linq;
using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class PublishXmlTests
{
    [Fact]
    public void PublishAll_produces_empty_envelope()
    {
        var xml = PublishXml.PublishAll();
        var doc = XDocument.Parse(xml);
        doc.Root!.Name.LocalName.Should().Be("importexportxml");
        doc.Root.Elements().Should().BeEmpty();
    }

    [Fact]
    public void Build_targeted_emits_one_entity_section_with_one_child()
    {
        var xml = PublishXml.Build(
            entityLogicalNames: new[] { "evo_demo" },
            webResourceIds: Array.Empty<string>(),
            optionSetNames: Array.Empty<string>());

        var doc = XDocument.Parse(xml);
        var entities = doc.Root!.Element("entities");
        entities.Should().NotBeNull();
        entities!.Elements("entity").Select(e => e.Value).Should().Equal(new[] { "evo_demo" });
        doc.Root.Element("optionsets").Should().BeNull("empty section is omitted");
    }

    [Fact]
    public void Build_emits_multiple_sections_when_multiple_kinds_specified()
    {
        var xml = PublishXml.Build(
            entityLogicalNames: new[] { "account", "contact" },
            webResourceIds: Array.Empty<string>(),
            optionSetNames: new[] { "evo_status" });

        var doc = XDocument.Parse(xml);
        doc.Root!.Element("entities")!.Elements("entity").Should().HaveCount(2);
        doc.Root.Element("optionsets")!.Elements("optionset").Select(e => e.Value)
            .Should().Equal(new[] { "evo_status" });
    }

    [Fact]
    public void Build_escapes_xml_unsafe_chars_via_XElement_value()
    {
        // We never concat strings — XElement handles escaping. Sanity check that <,> in a
        // hypothetical name wouldn't blow up the serialization (they shouldn't appear in
        // real Dataverse names, but the encoder must be robust either way).
        var xml = PublishXml.Build(
            entityLogicalNames: new[] { "weird<&>name" },
            webResourceIds: Array.Empty<string>(),
            optionSetNames: Array.Empty<string>());
        var doc = XDocument.Parse(xml);
        doc.Root!.Element("entities")!.Element("entity")!.Value.Should().Be("weird<&>name");
    }
}
