using Spectre.Console.Cli;
using System.ComponentModel;

namespace Evolx.Cli.Commands;

/// <summary>Common settings shared by every ADO command. Auto-resolved from env vars if flags omitted.</summary>
public class AdoSettings : CommandSettings
{
    [CommandOption("-o|--organization <ORG>")]
    [Description("Azure DevOps organization (default: $EVOLX_ADO_ORG, fallback 'evolx').")]
    public string? Organization { get; set; }

    [CommandOption("-p|--project <PROJECT>")]
    [Description("Project name (default: $EVOLX_ADO_PROJECT, fallback 'Evolx.Intern.Microsoft').")]
    public string? Project { get; set; }

    public string ResolvedOrganization => Organization
        ?? Environment.GetEnvironmentVariable("EVOLX_ADO_ORG")
        ?? "evolx";

    public string ResolvedProject => Project
        ?? Environment.GetEnvironmentVariable("EVOLX_ADO_PROJECT")
        ?? "Evolx.Intern.Microsoft";
}
