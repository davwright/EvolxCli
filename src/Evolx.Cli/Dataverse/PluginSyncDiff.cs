using System.Text.Json;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Diff between a desired <see cref="PluginManifest"/> and the current
/// registration state in Dataverse. Pure function — takes the JSON snapshot
/// of <c>sdkmessageprocessingsteps</c> and produces lists of (create, update,
/// delete) operations to apply.
/// </summary>
public static class PluginSyncDiff
{
    /// <summary>
    /// One step's worth of desired-state. Carries enough context for the caller
    /// to POST/PATCH; the sync engine fills in lookup ids (sdkmessageid,
    /// sdkmessagefilterid, plugintypeid) at apply time.
    /// </summary>
    public sealed record StepCreate(PluginManifestStep Step, string TypeName);

    public sealed record StepUpdate(string StepId, PluginManifestStep Step);

    public sealed record StepDelete(string StepId, string Name);

    public sealed record Result(
        IReadOnlyList<StepCreate> Creates,
        IReadOnlyList<StepUpdate> Updates,
        IReadOnlyList<StepDelete> Deletes);

    /// <summary>
    /// Diff manifest desired vs current state. Existing steps keyed by name are
    /// considered the same step (steps without an explicit StepName get the
    /// PowerShell-cmdlet-compatible default name when emitted to the manifest;
    /// see <see cref="PluginManifestStep.StepName"/>).
    /// </summary>
    public static Result Compute(PluginManifest desired, JsonElement currentStepsResponse)
    {
        var current = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (currentStepsResponse.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in arr.EnumerateArray())
            {
                current[DataverseLabels.String(step, "name")] = step.Clone();
            }
        }

        var creates = new List<StepCreate>();
        var updates = new List<StepUpdate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in desired.Types)
        {
            foreach (var step in type.Steps)
            {
                seen.Add(step.StepName);
                if (!current.TryGetValue(step.StepName, out var existing))
                {
                    creates.Add(new StepCreate(step, type.TypeName));
                    continue;
                }

                if (RequiresUpdate(step, existing))
                {
                    var id = DataverseLabels.String(existing, "sdkmessageprocessingstepid");
                    updates.Add(new StepUpdate(id, step));
                }
            }
        }

        var deletes = new List<StepDelete>();
        foreach (var (name, row) in current)
        {
            if (seen.Contains(name)) continue;
            deletes.Add(new StepDelete(
                DataverseLabels.String(row, "sdkmessageprocessingstepid"),
                name));
        }

        return new Result(creates, updates, deletes);
    }

    private static bool RequiresUpdate(PluginManifestStep desired, JsonElement existing)
    {
        if (desired.Stage != GetInt(existing, "stage")) return true;
        if (desired.Mode != GetInt(existing, "mode")) return true;
        if (desired.Rank != GetInt(existing, "rank", 1)) return true;
        if (desired.SupportedDeployment != GetInt(existing, "supporteddeployment")) return true;

        var existingFilter = DataverseLabels.String(existing, "filteringattributes");
        if (!string.Equals(desired.FilteredAttributes ?? "", existingFilter ?? "", StringComparison.Ordinal))
            return true;

        var existingConfig = DataverseLabels.String(existing, "configuration");
        if (!string.Equals(desired.Configuration ?? "", existingConfig ?? "", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static int GetInt(JsonElement el, string prop, int @default = 0)
    {
        if (el.ValueKind != JsonValueKind.Object) return @default;
        if (!el.TryGetProperty(prop, out var v)) return @default;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : @default;
    }
}
