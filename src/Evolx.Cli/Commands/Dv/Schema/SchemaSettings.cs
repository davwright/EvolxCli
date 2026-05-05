using System.ComponentModel;
using Evolx.Cli.Commands.Dv;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema;

/// <summary>Shared options for every <c>ev dv schema …</c> verb.</summary>
public class SchemaSettings : DvSettings
{
    [CommandOption("--solution <NAME>")]
    [Description("Solution unique name to scope the change to (sets MSCRM.SolutionUniqueName).")]
    public string? Solution { get; set; }

    [CommandOption("--publish")]
    [Description("Publish the affected entity / option set after the mutation lands.")]
    public bool Publish { get; set; }
}

/// <summary>Settings for destructive verbs — adds the <c>--yes</c> confirmation gate.</summary>
public class SchemaRemoveSettings : SchemaSettings
{
    [CommandOption("--yes")]
    [Description("Confirm the destructive action. Required for any remove verb.")]
    public bool Yes { get; set; }
}
