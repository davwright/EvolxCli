using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

/// <summary>
/// Shared base for every `ev dv ...` command. Resolves the env URL from --env or the
/// bound profile, creates a <see cref="DvClient"/>, and forwards control to a typed
/// <see cref="RunAsync"/>. No try/catch around the body — HttpFailure flows up to
/// Program.cs which renders it.
/// </summary>
/// <typeparam name="TSettings">Settings type for the command (must derive from DvSettings).</typeparam>
public abstract class DvCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : DvSettings
{
    protected override async Task<int> ExecuteAsync(CommandContext context, TSettings s, CancellationToken ct)
    {
        string envUrl;
        try { envUrl = DvProfile.Resolve(s.EnvUrl); }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
            return 2;
        }

        using var dv = await DvClient.CreateAsync(envUrl, ct);
        return await RunAsync(dv, s, ct);
    }

    /// <summary>Implement the actual verb. The DvClient is already authenticated and bound.</summary>
    protected abstract Task<int> RunAsync(DvClient dv, TSettings s, CancellationToken ct);
}
