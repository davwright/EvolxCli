using System.ComponentModel;
using Evolx.Cli.PowerPlatform;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Canvas;

/// <summary>
/// `ev canvas pack` — wraps `pac canvas pack`.
///
/// Local file operation only: no Dataverse / no `az` token. The one place in `ev`
/// we explicitly delegate to pac, because Microsoft maintains ~5000 lines of
/// format-aware canvas (un)pack logic that we'd otherwise duplicate (and have to
/// keep in sync as the format evolves).
/// </summary>
public sealed class PackCommand : AsyncCommand<PackCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<sources>")]
        [Description("Source directory containing unpacked canvas app YAML/JSON.")]
        public string Sources { get; set; } = "";

        [CommandOption("--out <FILE>")]
        [Description("Output .msapp path. Default: <sources>.msapp")]
        public string? Out { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        await PacTool.EnsureInstalledAsync(ct);

        var sources = Path.GetFullPath(s.Sources);
        if (!Directory.Exists(sources))
            throw new InvalidOperationException($"Source directory not found: {sources}");

        var output = s.Out is null
            ? Path.GetFullPath(sources.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".msapp")
            : Path.GetFullPath(s.Out);

        return await PacTool.RunInteractiveAsync(
            $"canvas pack --sources \"{sources}\" --msapp \"{output}\"", ct);
    }
}
