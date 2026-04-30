using System.ComponentModel;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv;

/// <summary>Common settings shared by every `ev dv ...` command.</summary>
public class DvSettings : CommandSettings
{
    [CommandOption("--env <URL>")]
    [Description("Dataverse environment URL (default: bound profile from `ev dv connect`).")]
    public string? EnvUrl { get; set; }
}
