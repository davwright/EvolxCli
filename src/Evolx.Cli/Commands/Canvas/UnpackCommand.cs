using System.ComponentModel;
using Evolx.Cli.PowerPlatform;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Canvas;

/// <summary>
/// `ev canvas unpack` — wraps `pac canvas unpack`.
///
/// Local file operation only: no Dataverse / no `az` token. See PackCommand for why
/// canvas (un)pack is the one verb that delegates to pac.
/// </summary>
public sealed class UnpackCommand : AsyncCommand<UnpackCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<msapp>")]
        [Description(".msapp file to unpack.")]
        public string Msapp { get; set; } = "";

        [CommandOption("--out <DIR>")]
        [Description("Output directory for unpacked sources. Default: <msapp>_src/")]
        public string? Out { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        await PacTool.EnsureInstalledAsync(ct);

        var msapp = Path.GetFullPath(s.Msapp);
        if (!File.Exists(msapp))
            throw new InvalidOperationException($".msapp file not found: {msapp}");

        var output = s.Out is null
            ? Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(msapp) ?? ".",
                Path.GetFileNameWithoutExtension(msapp) + "_src"))
            : Path.GetFullPath(s.Out);

        return await PacTool.RunInteractiveAsync(
            $"canvas unpack --msapp \"{msapp}\" --sources \"{output}\"", ct);
    }
}
