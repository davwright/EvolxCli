using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Solution;

/// <summary>
/// `ev dv solution import` — POST ImportSolutionAsync with the .zip base64'd in,
/// then poll the import job until completion. Failure surfacing is the point:
/// we report number of components actually applied, not just "HTTP 200 success".
/// </summary>
public sealed class ImportSolutionCommand : DvCommandBase<ImportSolutionCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("Path to the solution .zip to import.")]
        public string File { get; set; } = "";

        [CommandOption("--publish-changes")]
        [Description("Publish workflows / customizations after import.")]
        public bool PublishChanges { get; set; }

        [CommandOption("--overwrite-unmanaged")]
        [Description("Overwrite any unmanaged customizations conflicting with the import.")]
        public bool OverwriteUnmanaged { get; set; }

        [CommandOption("--convert-to-managed")]
        [Description("Convert components to managed during import.")]
        public bool ConvertToManaged { get; set; }

        [CommandOption("--no-wait")]
        [Description("Submit the import and return immediately with the import job id.")]
        public bool NoWait { get; set; }

        [CommandOption("--timeout <SECONDS>")]
        [Description("Polling timeout in seconds. Default 1800 (30 min).")]
        public int TimeoutSeconds { get; set; } = 1800;
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (!System.IO.File.Exists(s.File))
            throw new InvalidOperationException($"Solution file not found: {s.File}");

        var bytes = await System.IO.File.ReadAllBytesAsync(s.File, ct);
        var importJobId = Guid.NewGuid();

        var body = new ImportSolutionBody
        {
            CustomizationFile = Convert.ToBase64String(bytes),
            OverwriteUnmanagedCustomizations = s.OverwriteUnmanaged,
            PublishWorkflows = s.PublishChanges,
            ImportJobId = importJobId,
            ConvertToManaged = s.ConvertToManaged,
        };

        AnsiConsole.MarkupLine($"[dim]Importing[/] [bold]{Markup.Escape(Path.GetFileName(s.File))}[/] ({bytes.Length:n0} bytes)...");
        AnsiConsole.MarkupLine($"[dim]ImportJobId:[/] {importJobId}");

        await dv.InvokeActionAsync("ImportSolutionAsync", body, ct: ct);

        if (s.NoWait)
        {
            AnsiConsole.MarkupLine("[green]Submitted[/]. Poll with: [bold]ev dv solution import-status " + importJobId + "[/]");
            return 0;
        }

        var result = await PollImportJobAsync(dv, importJobId, s.TimeoutSeconds, ct);
        return RenderImportResult(result);
    }

    /// <summary>
    /// Poll <c>importjobs(id)</c> at 5s intervals until complete or timeout. Prints a
    /// progress line each tick so long imports don't look stalled.
    /// </summary>
    internal static async Task<ImportJobResult> PollImportJobAsync(
        DvClient dv, Guid importJobId, int timeoutSeconds, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        var lastProgress = -1d;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var row = await dv.TryGetImportJobAsync(importJobId, ct);
            if (row is null)
            {
                // Importjob row materializes slightly after the async submission; keep polling.
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            var result = ImportJobResult.From(row.Value);
            if (result.Progress != lastProgress)
            {
                AnsiConsole.MarkupLine($"[dim]progress[/] {result.Progress:0.0}%");
                lastProgress = result.Progress;
            }
            if (result.IsComplete) return result;

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        throw new TimeoutException(
            $"Import job {importJobId} did not complete within {timeoutSeconds}s. " +
            $"Check with: ev dv solution import-status {importJobId}");
    }

    /// <summary>
    /// Render the final import job state. Exit code maps from outcome:
    ///  0 — completed and components applied
    ///  3 — completed but zero components processed (silent no-op)
    /// </summary>
    internal static int RenderImportResult(ImportJobResult r)
    {
        if (r.IsSilentNoOp)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Import completed but 0 components were processed.[/] " +
                $"This usually means the solution was already at this version, or the file was empty.");
            AnsiConsole.MarkupLine($"[dim]ImportJobId:[/] {r.ImportJobId}");
            return 3;
        }
        AnsiConsole.MarkupLine(
            $"[green]Imported[/] [bold]{Markup.Escape(r.SolutionName)}[/] — " +
            $"{r.ComponentsProcessed} component(s) processed.");
        return 0;
    }
}
