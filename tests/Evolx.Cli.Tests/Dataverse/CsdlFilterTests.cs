using System.Xml.Linq;
using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class CsdlFilterTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx" Version="4.0">
          <edmx:DataServices>
            <Schema xmlns="http://docs.oasis-open.org/odata/ns/edm" Namespace="Microsoft.Dynamics.CRM">
              <EntityType Name="account" />
              <EntityType Name="evo_site" />
              <EntityType Name="evo_tour" />
              <ComplexType Name="someType" />
              <ComplexType Name="evo_complex" />
              <Action Name="DoThing" />
              <Action Name="evo_doThing" />
              <Function Name="GetThing" />
              <EntityContainer Name="System">
                <EntitySet Name="accounts" EntityType="X" />
              </EntityContainer>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>
        """;

    [Fact]
    public void Prune_keeps_only_entries_with_matching_prefix()
    {
        var doc = XDocument.Parse(Sample);

        CsdlFilter.Prune(doc, "evo");

        var entityNames = doc.Descendants().Where(e => e.Name.LocalName == "EntityType")
            .Select(e => (string?)e.Attribute("Name")).ToList();
        entityNames.Should().BeEquivalentTo(new[] { "evo_site", "evo_tour" });

        var complexNames = doc.Descendants().Where(e => e.Name.LocalName == "ComplexType")
            .Select(e => (string?)e.Attribute("Name")).ToList();
        complexNames.Should().BeEquivalentTo(new[] { "evo_complex" });

        var actionNames = doc.Descendants().Where(e => e.Name.LocalName == "Action")
            .Select(e => (string?)e.Attribute("Name")).ToList();
        actionNames.Should().BeEquivalentTo(new[] { "evo_doThing" });

        // GetThing has no evo_ prefix and no Function survived
        doc.Descendants().Any(e => e.Name.LocalName == "Function").Should().BeFalse();

        // EntityContainer "System" should be removed (doesn't start with evo)
        doc.Descendants().Any(e => e.Name.LocalName == "EntityContainer").Should().BeFalse();
    }

    [Fact]
    public void Prune_preserves_inner_structure_of_kept_nodes()
    {
        var doc = XDocument.Parse(Sample);
        CsdlFilter.Prune(doc, "evo");

        // Schema element itself stays (not in target list)
        doc.Descendants().Where(e => e.Name.LocalName == "Schema").Should().HaveCount(1);
    }

    [Fact]
    public void Prune_with_unmatched_prefix_drops_everything_in_targets()
    {
        var doc = XDocument.Parse(Sample);
        CsdlFilter.Prune(doc, "zzz_");

        doc.Descendants().Any(e => e.Name.LocalName == "EntityType").Should().BeFalse();
        doc.Descendants().Any(e => e.Name.LocalName == "ComplexType").Should().BeFalse();
    }
}
