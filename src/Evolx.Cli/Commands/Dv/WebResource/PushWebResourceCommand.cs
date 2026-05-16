using System.ComponentModel;
using System.Text.Json;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.WebResource;

/// <summary>
/// `ev dv webresource push` — create or update a web resource from a local file.
///
/// Strategy: read file bytes, base64-encode, look up the existing webresource by name,
/// and either PATCH (if remote content differs from local) or POST (new resource).
/// Content comparison is direct base64 string equality after fetching the remote bytes,
/// which is cheap on small webresources and avoids the MD5-mismatch quirk where two
/// payloads can MD5-collide but differ (vanishingly unlikely, but no reason to allow it).
/// </summary>
public sealed class PushWebResourceCommand : DvCommandBase<PushWebResourceCommand.Settings>
{
    public sealed class Settings : DvSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Logical name in Dataverse, e.g. 'evo_/scripts/mylib.js'.")]
        public string Name { get; set; } = "";

        [CommandArgument(1, "<FILE>")]
        [Description("Local file path to upload.")]
        public string File { get; set; } = "";

        [CommandOption("--type <X>")]
        [Description("Override the web resource type. By default derived from file extension. Valid: js|html|css|xml|png|jpg|gif|ico|svg|resx|xsl|xap.")]
        public string? Type { get; set; }

        [CommandOption("--display <X>")]
        [Description("Display name. Default: file name.")]
        public string? Display { get; set; }

        [CommandOption("--description <X>")]
        public string? Description { get; set; }

        [CommandOption("--solution <NAME>")]
        [Description("Solution unique name (MSCRM.SolutionUniqueName).")]
        public string? Solution { get; set; }

        [CommandOption("--force")]
        [Description("Upload even when content is byte-identical to the remote.")]
        public bool Force { get; set; }
    }

    protected override async Task<int> RunAsync(DvClient dv, Settings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.File))
            throw new ArgumentException("Both <NAME> and <FILE> are required.");
        if (!System.IO.File.Exists(s.File))
            throw new InvalidOperationException($"File not found: {s.File}");

        var bytes = await System.IO.File.ReadAllBytesAsync(s.File, ct);
        var base64 = Convert.ToBase64String(bytes);
        var wrType = s.Type is null ? WebResourceType.FromPath(s.File) : WebResourceType.FromName(s.Type);
        var display = s.Display ?? Path.GetFileName(s.File);

        var existing = await dv.TryGetWebResourceAsync(s.Name, ct);
        if (existing is not null)
        {
            var remoteB64 = DataverseLabels.String(existing.Value, "content");
            if (!s.Force && string.Equals(remoteB64, base64, StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine($"[dim]Unchanged[/] [bold]{Markup.Escape(s.Name)}[/] — local equals remote ({bytes.Length:n0} bytes). Use --force to re-upload.");
                return 0;
            }

            var id = DataverseLabels.String(existing.Value, "webresourceid");
            var patch = new Dictionary<string, object?>
            {
                ["content"] = base64,
                ["displayname"] = display,
            };
            if (!string.IsNullOrEmpty(s.Description)) patch["description"] = s.Description;
            var patchJson = JsonSerializer.Serialize(patch, Http.HttpGateway.JsonOptions);
            await dv.UpdateAsync("webresourceset", id, patchJson, ct);

            AnsiConsole.MarkupLine($"[green]Updated[/] [bold]{Markup.Escape(s.Name)}[/] ({bytes.Length:n0} bytes).");
            return 0;
        }

        // Create
        var create = new Dictionary<string, object?>
        {
            ["name"] = s.Name,
            ["displayname"] = display,
            ["webresourcetype"] = wrType,
            ["content"] = base64,
        };
        if (!string.IsNullOrEmpty(s.Description)) create["description"] = s.Description;

        var createJson = JsonSerializer.Serialize(create, Http.HttpGateway.JsonOptions);

        await SilentSkipGuard.RunAsync(
            description: $"create webresource {s.Name}",
            mutate: () => dv.CreateAsync("webresourceset", createJson, s.Solution, ct),
            verify: async () => await dv.TryGetWebResourceAsync(s.Name, ct) is not null);

        AnsiConsole.MarkupLine($"[green]Created[/] [bold]{Markup.Escape(s.Name)}[/] ({bytes.Length:n0} bytes).");
        return 0;
    }
}
