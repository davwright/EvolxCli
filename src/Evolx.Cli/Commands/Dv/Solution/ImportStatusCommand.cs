using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Solution;

/// <summary>
/// `ev dv solution import-status` — show an import job's progress / completion.
/// With <c>--watch</c> behaves like the polling phase of <see cref="ImportSolutionCommand"/>.
/// </summary>
public sealed class ImportStatusCommand : DvCommandBase<ImportStatusCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<JOBID>")]
        [Description("Import job id (GUID).")]
        public string JobId { get; set; } = "";

        [CommandOption("--watch")]
        [Description("Poll until the job completes.")]
        public bool Watch { get; set; }

        [CommandOption("--timeout <SECONDS>")]
        [Description("Polling timeout when --watch. Default 1800.")]
        public int TimeoutSeconds { get; set; } = 1800;
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (!Guid.TryParse(s.JobId, out var jobId))
            throw new ArgumentException($"'{s.JobId}' is not a valid GUID.");

        if (s.Watch)
        {
            var result = await ImportSolutionCommand.PollImportJobAsync(dv, jobId, s.TimeoutSeconds, ct);
            return ImportSolutionCommand.RenderImportResult(result);
        }

        var row = await dv.TryGetImportJobAsync(jobId, ct);
        if (row is null)
        {
            AnsiConsole.MarkupLine($"[yellow]Import job {jobId} not found.[/]");
            return 1;
        }

        var snapshot = ImportJobResult.From(row.Value);
        AnsiConsole.MarkupLine($"[bold]Import job[/] {snapshot.ImportJobId}");
        AnsiConsole.MarkupLine($"  Solution    : {Markup.Escape(snapshot.SolutionName)}");
        AnsiConsole.MarkupLine($"  Progress    : {snapshot.Progress:0.0}%");
        AnsiConsole.MarkupLine($"  Started     : {snapshot.StartedOn?.ToString("u") ?? "(not started)"}");
        AnsiConsole.MarkupLine($"  Completed   : {snapshot.CompletedOn?.ToString("u") ?? "(not completed)"}");
        AnsiConsole.MarkupLine($"  Components  : {snapshot.ComponentsProcessed}");
        if (snapshot.IsComplete) return snapshot.IsSilentNoOp ? 3 : 0;
        return 0;
    }
}
