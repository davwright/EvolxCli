using System.ComponentModel;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Solution;

/// <summary>
/// `ev dv solution new` — create a new unmanaged solution bound to an existing publisher.
///
/// Publisher must already exist in the env; we don't auto-create publishers (mostly because
/// publisher prefix collisions are silent and the right value depends on Evolx conventions
/// per-customer). Pass <c>--publisher</c> with the unique name; we resolve to the publisher
/// id and bind via OData reference.
/// </summary>
public sealed class NewSolutionCommand : DvCommandBase<NewSolutionCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandOption("--name <X>")]
        [Description("Solution unique name (no spaces). Required.")]
        public string Name { get; set; } = "";

        [CommandOption("--publisher <X>")]
        [Description("Publisher unique name. Must already exist in the env.")]
        public string Publisher { get; set; } = "";

        [CommandOption("--display <X>")]
        [Description("Friendly (display) name. Default: same as --name.")]
        public string? Display { get; set; }

        [CommandOption("--version <X>")]
        [Description("Initial version (default 1.0.0.0).")]
        public string Version { get; set; } = "1.0.0.0";

        [CommandOption("--description <X>")]
        public string? Description { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
            throw new ArgumentException("--name is required.");
        if (string.IsNullOrWhiteSpace(s.Publisher))
            throw new ArgumentException("--publisher is required.");

        var publisher = await dv.TryGetPublisherAsync(s.Publisher, ct)
            ?? throw new InvalidOperationException(
                $"Publisher '{s.Publisher}' not found. Create it in Power Platform admin first, or pass a different --publisher.");

        var publisherId = DataverseLabels.String(publisher, "publisherid");

        var body = new SolutionCreateBody
        {
            UniqueName = s.Name,
            FriendlyName = s.Display ?? s.Name,
            Version = s.Version,
            Description = s.Description,
            PublisherIdBind = $"/publishers({publisherId})",
        };

        // POST /solutions is a record create (camelCase JSON), not a metadata mutation.
        // Serialize via JsonOptions so PascalCase record names become camelCase on the wire;
        // the `publisherid@odata.bind` JsonPropertyName attribute keeps that one verbatim.
        var json = System.Text.Json.JsonSerializer.Serialize(body, Http.HttpGateway.JsonOptions);

        await SilentSkipGuard.RunAsync(
            description: $"create solution {s.Name}",
            mutate: () => dv.CreateAsync("solutions", json, ct),
            verify: async () => await dv.TryGetSolutionAsync(s.Name, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Created[/] solution [bold]{Markup.Escape(s.Name)}[/]");
        return 0;
    }
}
