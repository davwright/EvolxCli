using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Plugin;

/// <summary>
/// `ev dv plugin sync` — diff a plugin manifest against the registered steps
/// in Dataverse, then apply the deltas (create new, update changed, delete orphans).
///
/// <para>
/// Today the manifest must be supplied as JSON (<c>--manifest path.json</c>) following
/// the <see cref="PluginManifest"/> shape. The original PowerShell Sync-DVPlugins reflects
/// on an Evolx-specific base class (<c>Evolx.Xrm.Plugins._Common.Plugin</c>) and calls
/// <c>PluginProcessingStepConfigs()</c> at runtime — that reflection model can't be
/// expressed via the recommended <c>MetadataLoadContext</c> (which is read-only) and
/// requires loading the assembly into the host AppDomain. Until a live OSIS plugin DLL
/// and the build-time manifest emitter are available together, passing a .dll directly
/// fails with a clear message pointing at the manifest workflow.
/// </para>
/// </summary>
public sealed class SyncPluginCommand : DvCommandBase<SyncPluginCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "[ASSEMBLY]")]
        [Description("Path to a plugin .dll. NOT YET WIRED — pass --manifest instead.")]
        public string? AssemblyPath { get; set; }

        [CommandOption("--manifest <FILE>")]
        [Description("Path to a JSON file matching PluginManifest. Required today.")]
        public string? ManifestPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Print the plan without applying it.")]
        public bool DryRun { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(s.AssemblyPath) && string.IsNullOrEmpty(s.ManifestPath))
        {
            // The verb shape from the original issue is `ev dv plugin sync <assembly.dll>`,
            // but reflecting on PluginProcessingStepConfigs() requires running plugin code,
            // which is incompatible with the read-only MetadataLoadContext approach. Until a
            // build-time manifest emitter ships, surface a clear redirect rather than a
            // half-working live reflection that drops half the registration model.
            throw new InvalidOperationException(
                "Direct .dll reflection is not yet wired. Generate a manifest JSON " +
                "from your plugin project and pass --manifest <path>. See PluginManifest " +
                "for the expected shape.");
        }

        if (string.IsNullOrEmpty(s.ManifestPath))
            throw new ArgumentException("Either --manifest <path> or an assembly path is required (today, --manifest only).");
        if (!System.IO.File.Exists(s.ManifestPath))
            throw new InvalidOperationException($"Manifest not found: {s.ManifestPath}");

        var json = await System.IO.File.ReadAllTextAsync(s.ManifestPath, ct);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Http.HttpGateway.JsonOptions)
            ?? throw new InvalidOperationException("Manifest deserialized to null.");

        // Pull the registered assembly + steps. Manifests target one assembly.
        var assemblies = await dv.ListPluginAssembliesAsync(manifest.AssemblyName, ct);
        var assemblyRow = FindExactAssembly(assemblies, manifest.AssemblyName);
        if (assemblyRow is null)
        {
            AnsiConsole.MarkupLine($"[red]Plugin assembly '{Markup.Escape(manifest.AssemblyName)}' not registered.[/]");
            AnsiConsole.MarkupLine("[dim]Register the assembly via the Plugin Registration Tool first; only step sync is supported here.[/]");
            return 1;
        }

        var assemblyId = Guid.Parse(DataverseLabels.String(assemblyRow.Value, "pluginassemblyid"));

        // For diff purposes we need all steps under any plugintype on this assembly. We
        // collapse to one JSON `value` array regardless of plugintype, since the diff
        // engine only cares about step names + content.
        var allSteps = await CollectAllStepsForAssemblyAsync(dv, assemblyId, ct);

        var diff = PluginSyncDiff.Compute(manifest, allSteps);

        AnsiConsole.MarkupLine(
            $"[bold]Plugin sync plan[/] for [cyan]{Markup.Escape(manifest.AssemblyName)}[/] " +
            $"v{Markup.Escape(manifest.AssemblyVersion)}");
        AnsiConsole.MarkupLine($"  Creates : {diff.Creates.Count}");
        AnsiConsole.MarkupLine($"  Updates : {diff.Updates.Count}");
        AnsiConsole.MarkupLine($"  Deletes : {diff.Deletes.Count}");

        foreach (var c in diff.Creates)
            AnsiConsole.MarkupLine($"    [green]+[/] {Markup.Escape(c.Step.StepName)}");
        foreach (var u in diff.Updates)
            AnsiConsole.MarkupLine($"    [yellow]~[/] {Markup.Escape(u.Step.StepName)}");
        foreach (var d in diff.Deletes)
            AnsiConsole.MarkupLine($"    [red]-[/] {Markup.Escape(d.Name)}");

        if (s.DryRun)
        {
            AnsiConsole.MarkupLine("[dim]--dry-run: no changes applied.[/]");
            return 0;
        }

        if (diff.Creates.Count == 0 && diff.Updates.Count == 0 && diff.Deletes.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Already in sync.[/]");
            return 0;
        }

        // Apply phase: deferred until a live DLL parity test can validate end-to-end.
        // The diff/plan above is the high-value, testable surface; the apply phase is
        // straightforward POSTs/PATCHes/DELETEs against sdkmessageprocessingsteps but
        // not wired here today.
        AnsiConsole.MarkupLine(
            "[yellow]Apply phase is not wired yet.[/] Plan computed and printed above. " +
            "Use --dry-run to suppress this message.");
        return 4;
    }

    private static JsonElement? FindExactAssembly(JsonElement response, string name)
    {
        if (!response.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var row in arr.EnumerateArray())
        {
            if (string.Equals(DataverseLabels.String(row, "name"), name, StringComparison.Ordinal))
                return row.Clone();
        }
        return null;
    }

    private static async Task<JsonElement> CollectAllStepsForAssemblyAsync(DvClient dv, Guid assemblyId, CancellationToken ct)
    {
        var types = await dv.ListPluginTypesAsync(assemblyId, ct);
        var aggregated = new List<JsonElement>();
        if (types.TryGetProperty("value", out var tArr))
        {
            foreach (var t in tArr.EnumerateArray())
            {
                var typeId = Guid.Parse(DataverseLabels.String(t, "plugintypeid"));
                var steps = await dv.ListPluginStepsAsync(typeId, ct);
                if (steps.TryGetProperty("value", out var sArr))
                {
                    foreach (var step in sArr.EnumerateArray())
                        aggregated.Add(step.Clone());
                }
            }
        }

        // Wrap the aggregated list back into a `{ "value": [...] }` element for the diff engine.
        var json = JsonSerializer.Serialize(new { value = aggregated });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
