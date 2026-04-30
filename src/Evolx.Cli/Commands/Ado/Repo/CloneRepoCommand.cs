using System.ComponentModel;
using System.Diagnostics;
using Evolx.Cli.Ado;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Ado.Repo;

/// <summary>Look up an ADO repo by name and shell out to `git clone` with its remote URL.</summary>
public sealed class CloneRepoCommand : AsyncCommand<CloneRepoCommand.Settings>
{
    public sealed class Settings : AdoSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Repo name (or substring, must match exactly one).")]
        public string Name { get; set; } = "";

        [CommandOption("--into <DIR>")]
        [Description("Target directory (default: current working dir, repo cloned into a subfolder by name).")]
        public string? Into { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        using var ado = await AdoClient.CreateAsync(s.ResolvedOrganization, s.ResolvedProject, ct);
        var repos = await ado.ListRepositoriesAsync(ct);
        var matches = repos.Where(r => r.Name.Contains(s.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]No repo matching '{s.Name}'.[/]");
            return 1;
        }
        if (matches.Count > 1)
        {
            AnsiConsole.MarkupLine($"[yellow]Multiple matches — be more specific:[/]");
            foreach (var m in matches) AnsiConsole.MarkupLine($"  {Markup.Escape(m.Name)}");
            return 1;
        }

        var repo = matches[0];
        var url = repo.RemoteUrl ?? repo.WebUrl ?? throw new InvalidOperationException("Repo has no clone URL.");
        var into = s.Into ?? Directory.GetCurrentDirectory();

        AnsiConsole.MarkupLine($"[cyan]git clone[/] {Markup.Escape(url)} (into {Markup.Escape(into)})");
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            ArgumentList = { "clone", url },
            WorkingDirectory = into,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start `git`. Is git on PATH?");
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
