using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Solution;

/// <summary>
/// `ev dv solution remove-component` — POST RemoveSolutionComponent.
///
/// Component types are Dataverse's <c>componenttype</c> enum (1 = Entity, 2 = Attribute,
/// 9 = OptionSet, 61 = WebResource, 70 = FieldSecurityProfile, etc.). Reference:
/// https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/solutioncomponent
/// </summary>
public sealed class RemoveComponentCommand : DvCommandBase<RemoveComponentCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<SOLUTION>")]
        [Description("Solution unique name.")]
        public string Solution { get; set; } = "";

        [CommandOption("--component-type <N>")]
        [Description("Component type (enum int).")]
        public int ComponentType { get; set; }

        [CommandOption("--object-id <GUID>")]
        [Description("Object id (MetadataId / SolutionComponent.ObjectId).")]
        public string ObjectId { get; set; } = "";
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Solution))
            throw new ArgumentException("Solution name is required.");
        if (!Guid.TryParse(s.ObjectId, out var objectId))
            throw new ArgumentException("--object-id must be a GUID.");

        var body = new RemoveSolutionComponentBody
        {
            ComponentId = objectId,
            ComponentType = s.ComponentType,
            SolutionUniqueName = s.Solution,
        };

        await dv.InvokeActionAsync("RemoveSolutionComponent", body, ct: ct);

        AnsiConsole.MarkupLine(
            $"[green]Removed[/] componenttype={s.ComponentType} id={Markup.Escape(objectId.ToString())} from [bold]{Markup.Escape(s.Solution)}[/].");
        return 0;
    }
}
