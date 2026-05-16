using System.Text.Json;
using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

/// <summary>
/// Covers the silent-skip detection on solution imports: the central acceptance
/// criterion of the Cluster F issue is that we never report success when the
/// importjob shows zero components processed.
/// </summary>
public class ImportJobResultTests
{
    [Fact]
    public void Counts_success_results_in_data_xml()
    {
        var xml = """
            <importexportxml>
              <result result="success" componentName="evo_demo" />
              <result result="success" componentName="evo_other" />
              <result result="failure" componentName="evo_bad" errorcode="0x80048437" />
            </importexportxml>
            """;
        ImportJobResult.CountComponentsProcessed(xml).Should().Be(2);
    }

    [Fact]
    public void Empty_xml_is_zero()
    {
        ImportJobResult.CountComponentsProcessed("").Should().Be(0);
        ImportJobResult.CountComponentsProcessed("   ").Should().Be(0);
    }

    [Fact]
    public void Malformed_xml_is_zero_not_throw()
    {
        ImportJobResult.CountComponentsProcessed("<not-xml").Should().Be(0);
    }

    [Fact]
    public void From_json_extracts_progress_and_completion()
    {
        var json = """
            {
                "importjobid": "11111111-2222-3333-4444-555555555555",
                "progress": 100,
                "completedon": "2026-05-13T10:15:00Z",
                "startedon":   "2026-05-13T10:14:50Z",
                "solutionname": "EvoTest",
                "data": "<importexportxml><result result='success' componentName='x'/></importexportxml>"
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var r = ImportJobResult.From(doc.RootElement);

        r.ImportJobId.Should().Be(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        r.Progress.Should().Be(100d);
        r.CompletedOn.Should().NotBeNull();
        r.SolutionName.Should().Be("EvoTest");
        r.ComponentsProcessed.Should().Be(1);
        r.IsComplete.Should().BeTrue();
        r.IsSilentNoOp.Should().BeFalse();
    }

    [Fact]
    public void Completed_but_zero_components_is_silent_noop()
    {
        var json = """
            {
                "importjobid": "11111111-2222-3333-4444-555555555555",
                "progress": 100,
                "completedon": "2026-05-13T10:15:00Z",
                "solutionname": "EvoTest",
                "data": "<importexportxml></importexportxml>"
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var r = ImportJobResult.From(doc.RootElement);

        r.IsComplete.Should().BeTrue();
        r.IsSilentNoOp.Should().BeTrue();
    }

    [Fact]
    public void Not_yet_complete_is_neither_complete_nor_noop()
    {
        var json = """
            {
                "importjobid": "11111111-2222-3333-4444-555555555555",
                "progress": 42.5,
                "solutionname": "EvoTest",
                "data": ""
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var r = ImportJobResult.From(doc.RootElement);

        r.Progress.Should().Be(42.5d);
        r.IsComplete.Should().BeFalse();
        r.IsSilentNoOp.Should().BeFalse();
    }
}
