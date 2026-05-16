using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Plugin;

/// <summary>
/// `ev dv plugin list` — list plugin assemblies (and optionally their types + steps).
/// </summary>
public sealed class ListPluginCommand : DvCommandBase<ListPluginCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--assembly <X>")]
        [Description("Filter assemblies whose name contains this substring.")]
        public string? Assembly { get; set; }

        [CommandOption("--types")]
        [Description("Also list plugin types under each assembly.")]
        public bool Types { get; set; }

        [CommandOption("--steps")]
        [Description("Also list steps under each type. Implies --types.")]
        public bool Steps { get; set; }

        [CommandOption("--json")]
        public bool Json { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        var assemblies = await dv.ListPluginAssembliesAsync(s.Assembly, ct);

        if (s.Json) { JsonTableRenderer.RenderJson(assemblies); return 0; }

        if (!assemblies.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            AnsiConsole.MarkupLine("[yellow]No `value` array.[/]");
            return 1;
        }

        var rows = arr.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim](no plugin assemblies matched)[/]");
            return 0;
        }

        var includeTypes = s.Types || s.Steps;
        foreach (var asm in rows)
        {
            var asmName = DataverseLabels.String(asm, "name");
            var asmVer = DataverseLabels.String(asm, "version");
            var asmId = DataverseLabels.String(asm, "pluginassemblyid");
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(asmName)}[/] [dim]{Markup.Escape(asmVer)}  {asmId}[/]");

            if (!includeTypes) continue;
            if (!Guid.TryParse(asmId, out var aId)) continue;

            var types = await dv.ListPluginTypesAsync(aId, ct);
            if (!types.TryGetProperty("value", out var tArr)) continue;
            foreach (var t in tArr.EnumerateArray())
            {
                var typeName = DataverseLabels.String(t, "typename");
                var typeId = DataverseLabels.String(t, "plugintypeid");
                AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(typeName)}[/]");

                if (!s.Steps) continue;
                if (!Guid.TryParse(typeId, out var ptid)) continue;
                var steps = await dv.ListPluginStepsAsync(ptid, ct);
                if (!steps.TryGetProperty("value", out var sArr)) continue;
                foreach (var step in sArr.EnumerateArray())
                {
                    AnsiConsole.MarkupLine(
                        $"    [dim]stage={step.GetProperty("stage").GetInt32()}[/] " +
                        $"[dim]mode={step.GetProperty("mode").GetInt32()}[/] " +
                        $"{Markup.Escape(DataverseLabels.String(step, "name"))}");
                }
            }
        }
        AnsiConsole.MarkupLine($"[dim]{rows.Count} assembly(ies)[/]");
        return 0;
    }
}
