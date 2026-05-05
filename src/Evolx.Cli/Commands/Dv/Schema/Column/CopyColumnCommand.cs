using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evolx.Cli.Commands.Dv;
using Evolx.Cli.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Evolx.Cli.Commands.Dv.Schema.Column;

/// <summary>
/// Cross-environment column copy: read each source row's value and PATCH the same column on
/// the corresponding destination row (matched by id). Used to migrate data after schema diff.
/// Does NOT create the destination column — caller must run <c>ev dv schema column new</c>
/// first if needed.
/// </summary>
public sealed class CopyColumnCommand : AsyncCommand<CopyColumnCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--from <REF>")]
        [Description("Source reference: <env>:<table>.<column>, e.g. osis-prod.crm4:account.evo_foo.")]
        public string From { get; set; } = "";

        [CommandOption("--to <REF>")]
        [Description("Destination reference: <table>.<column> (env defaults to the bound profile).")]
        public string To { get; set; } = "";

        [CommandOption("--filter <ODATA>")]
        [Description("Optional OData $filter to limit which source rows are copied.")]
        public string? Filter { get; set; }

        [CommandOption("--overwrite")]
        [Description("Replace dest values that are already set. Default: skip.")]
        public bool Overwrite { get; set; }

        [CommandOption("--page-size <N>")]
        public int PageSize { get; set; } = 500;

        [CommandOption("--dest-env <URL>")]
        [Description("Override destination env (default: bound profile from `ev dv connect`).")]
        public string? DestEnv { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings s, CancellationToken ct)
    {
        var src = ParseReference(s.From, requireEnv: true);
        var dst = ParseReference(s.To, requireEnv: false);

        var srcEnvUrl = EnvUrlResolver.Normalize(src.Env!);
        var dstEnvUrl = DvProfile.Resolve(s.DestEnv ?? dst.Env);

        using var srcDv = await DvClient.CreateAsync(srcEnvUrl, ct);
        using var dstDv = await DvClient.CreateAsync(dstEnvUrl, ct);

        // Look up the source entity-set name (we need the plural form for OData GET).
        if (await srcDv.TryGetEntityDefinitionAsync(src.Table, ct) is not { } srcDef)
            throw new InvalidOperationException($"Source table '{src.Table}' not found.");
        var srcEntitySet = DataverseLabels.String(srcDef, "EntitySetName");
        var srcPrimaryId = $"{src.Table}id";

        if (await dstDv.TryGetEntityDefinitionAsync(dst.Table, ct) is not { } dstDef)
            throw new InvalidOperationException($"Destination table '{dst.Table}' not found.");
        var dstEntitySet = DataverseLabels.String(dstDef, "EntitySetName");

        // Read source: id + value column.
        var paged = await srcDv.QueryPagedAsync(
            srcEntitySet,
            filter: s.Filter,
            select: $"{srcPrimaryId},{src.Column}",
            pageSize: s.PageSize,
            followAll: true,
            onPage: count => AnsiConsole.MarkupLine($"[dim]read {count} source row(s)…[/]"),
            ct: ct);

        AnsiConsole.MarkupLine($"[dim]Source: {paged.Rows.Count} row(s) total.[/]");

        var written = 0;
        var skipped = 0;
        await AnsiConsole.Progress().StartAsync(async pctx =>
        {
            var task = pctx.AddTask("[green]Copying[/]", maxValue: paged.Rows.Count);
            foreach (var row in paged.Rows)
            {
                if (ct.IsCancellationRequested) break;
                task.Increment(1);

                if (!row.TryGetProperty(srcPrimaryId, out var idEl) || idEl.ValueKind != JsonValueKind.String)
                {
                    skipped++; continue;
                }
                var id = idEl.GetString()!;
                if (!row.TryGetProperty(src.Column, out var valueEl) || valueEl.ValueKind == JsonValueKind.Null)
                {
                    skipped++; continue;
                }

                if (!s.Overwrite)
                {
                    // Skip rows where the destination already has a non-null value.
                    var current = await dstDv.QueryAsync(dstEntitySet,
                        filter: $"{srcPrimaryId} eq {id}",
                        select: dst.Column,
                        top: 1,
                        ct: ct);
                    var rows = current.GetProperty("value").EnumerateArray().ToList();
                    if (rows.Count > 0
                        && rows[0].TryGetProperty(dst.Column, out var existing)
                        && existing.ValueKind != JsonValueKind.Null)
                    {
                        skipped++; continue;
                    }
                }

                // Build the PATCH body via System.Text.Json.Nodes (single-field object) — no string concat.
                var body = new JsonObject();
                body[dst.Column] = JsonNode.Parse(valueEl.GetRawText());
                await dstDv.UpdateAsync(dstEntitySet, id, body.ToJsonString(), ct);
                written++;
            }
        });

        AnsiConsole.MarkupLine($"[green]Done.[/] Wrote {written}; skipped {skipped}.");
        return 0;
    }

    private static (string? Env, string Table, string Column) ParseReference(string text, bool requireEnv)
    {
        var s = text?.Trim() ?? "";
        if (s.Length == 0)
            throw new ArgumentException($"Empty reference.");
        string? env = null;
        var dot = s;
        var colon = s.IndexOf(':');
        if (colon > 0)
        {
            env = s[..colon];
            dot = s[(colon + 1)..];
        }
        else if (requireEnv)
        {
            throw new ArgumentException($"--from must include an environment, e.g. 'osis-prod.crm4:account.evo_foo'.");
        }

        var dotIdx = dot.IndexOf('.');
        if (dotIdx < 0)
            throw new ArgumentException($"Reference '{text}' is missing the .column suffix.");

        return (env, dot[..dotIdx], dot[(dotIdx + 1)..]);
    }
}

