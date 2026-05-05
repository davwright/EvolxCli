using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class UpdateCommand : DvCommandBase<UpdateCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<TABLE>")]
        [Description("Entity set name, e.g. evo_tours.")]
        public string Table { get; set; } = "";

        [CommandArgument(1, "<ID>")]
        [Description("Primary key GUID of the row to update (no braces).")]
        public string Id { get; set; } = "";

        [CommandOption("--json <BODY>")]
        [Description("JSON body. Literal JSON, or @path/to/file.json to read from disk.")]
        public string Json { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Json))
        {
            AnsiConsole.MarkupLine("[red]--json <body> is required.[/]");
            return 2;
        }

        var body = s.Json.StartsWith('@')
            ? await File.ReadAllTextAsync(s.Json[1..], ct)
            : s.Json;

        await dv.UpdateAsync(s.Table, s.Id, body, ct);
        AnsiConsole.MarkupLine($"[green]Updated[/] {Markup.Escape(s.Table)}({s.Id})");
        return 0;
    }
}
