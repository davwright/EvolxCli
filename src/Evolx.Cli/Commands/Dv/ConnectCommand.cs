using System.ComponentModel;
using Evolx.Cli.Auth;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

public sealed class ConnectCommand : AsyncCommand<ConnectCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[ENV]")]
        [Description("Environment URL or short form (e.g. osis-dev.crm4). Omit with --clear to forget the binding.")]
        public string? Env { get; set; }

        [CommandOption("--clear")]
        [Description("Forget the bound environment (removes ~/.evolx/profile.json).")]
        public bool Clear { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        if (s.Clear)
        {
            DvProfile.Clear();
            AnsiConsole.MarkupLine("[green]Cleared[/] Dataverse profile.");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(s.Env))
        {
            AnsiConsole.MarkupLine("[red]Provide an env URL, or --clear to forget the current binding.[/]");
            return 2;
        }

        var url = EnvUrlResolver.Normalize(s.Env);

        // Probe: try to mint a token. Fails fast if the user can't actually access this env.
        try
        {
            await AzAuth.GetAccessTokenAsync(url, ct);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Could not get a token for {Markup.Escape(url)}.[/]");
            AnsiConsole.WriteLine(ex.Message);
            AnsiConsole.MarkupLine("[dim]Run `az login` if your session is dead, or check the env URL is right.[/]");
            return 1;
        }

        // Confirm the user can actually talk to Dataverse (token might mint but the user
        // could still lack a role on this org).
        try
        {
            using var dv = await DvClient.CreateAsync(url, ct);
            await dv.WhoAmIAsync(ct);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Token works but Dataverse rejected the call.[/]");
            AnsiConsole.WriteLine(ex.Message);
            return 1;
        }

        var profile = new DvProfile { EnvUrl = url };
        profile.Save();

        AnsiConsole.MarkupLine($"[green]Bound[/] to {Markup.Escape(url)}");
        AnsiConsole.MarkupLine("[dim]Try: ev dv whoami | ev dv query evo_sites --top 5[/]");
        return 0;
    }
}
