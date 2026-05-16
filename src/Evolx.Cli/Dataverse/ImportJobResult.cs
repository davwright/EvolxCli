using System.Text.Json;
using System.Xml.Linq;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Structured view of an <c>importjobs</c> row. Source of truth for whether an
/// ImportSolutionAsync actually applied changes — see
/// <see cref="ComponentsProcessed"/>. The PowerShell module historically logged
/// "Imported successfully" off the HTTP 200, but a 200 means "the import job ran",
/// not "the import job changed anything". This wrapper exposes the data the
/// silent-skip guard needs to distinguish those cases.
/// </summary>
public sealed record ImportJobResult
{
    public Guid ImportJobId { get; init; }
    public double Progress { get; init; }
    public DateTimeOffset? CompletedOn { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public string SolutionName { get; init; } = "";

    /// <summary>
    /// Number of solution components Dataverse reports as processed. Parsed from the
    /// <c>data</c> XML payload. Zero means the import was a no-op (the very case the
    /// silent-skip guard exists to catch).
    /// </summary>
    public int ComponentsProcessed { get; init; }

    /// <summary>True when CompletedOn is set and Progress is at 100.</summary>
    public bool IsComplete => CompletedOn is not null && Progress >= 100d;

    /// <summary>True when the job ran to completion but processed zero components.</summary>
    public bool IsSilentNoOp => IsComplete && ComponentsProcessed == 0;

    /// <summary>Raw <c>data</c> XML, available for callers that want to inspect deeper.</summary>
    public string DataXml { get; init; } = "";

    /// <summary>Build from the JsonElement returned by <c>GetImportJobAsync</c>.</summary>
    public static ImportJobResult From(JsonElement row)
    {
        var dataXml = DataverseLabels.String(row, "data");
        return new ImportJobResult
        {
            ImportJobId = TryGetGuid(row, "importjobid") ?? Guid.Empty,
            Progress = TryGetDouble(row, "progress") ?? 0d,
            CompletedOn = TryGetDateTime(row, "completedon"),
            StartedOn = TryGetDateTime(row, "startedon"),
            SolutionName = DataverseLabels.String(row, "solutionname"),
            ComponentsProcessed = CountComponentsProcessed(dataXml),
            DataXml = dataXml,
        };
    }

    /// <summary>
    /// Count solution components in the importjob's <c>data</c> XML. Dataverse emits
    /// a <c>&lt;result result="success|failure" .../&gt;</c> node per component;
    /// counting <c>success</c> nodes gives the number of components actually applied
    /// (a no-op import has zero, even on HTTP 200 + 100% progress).
    /// </summary>
    internal static int CountComponentsProcessed(string dataXml)
    {
        if (string.IsNullOrWhiteSpace(dataXml)) return 0;
        try
        {
            var doc = XDocument.Parse(dataXml);
            return doc.Descendants("result")
                .Count(r => string.Equals(
                    (string?)r.Attribute("result"), "success",
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return 0;
        }
    }

    private static Guid? TryGetGuid(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        if (!row.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return null;
        return Guid.TryParse(p.GetString(), out var g) ? g : null;
    }

    private static double? TryGetDouble(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        if (!row.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
        if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var d2)) return d2;
        return null;
    }

    private static DateTimeOffset? TryGetDateTime(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        if (!row.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return null;
        return DateTimeOffset.TryParse(p.GetString(), out var dt) ? dt : null;
    }
}
