using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class WhoamiCommand : AsyncCommand<DvSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, DvSettings s, CancellationToken ct)
    {
        string envUrl;
        try { envUrl = DvProfile.Resolve(s.EnvUrl); }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }

        using var dv = await DvClient.CreateAsync(envUrl, ct);
        var who = await dv.WhoAmIAsync(ct);

        var table = new Table().Border(TableBorder.Minimal).AddColumns("Field", "Value");
        table.AddRow("Env", Markup.Escape(envUrl));
        if (who.TryGetProperty("UserId", out var u)) table.AddRow("UserId", u.GetString() ?? "");
        if (who.TryGetProperty("BusinessUnitId", out var b)) table.AddRow("BusinessUnitId", b.GetString() ?? "");
        if (who.TryGetProperty("OrganizationId", out var o)) table.AddRow("OrganizationId", o.GetString() ?? "");
        AnsiConsole.Write(table);
        return 0;
    }
}
