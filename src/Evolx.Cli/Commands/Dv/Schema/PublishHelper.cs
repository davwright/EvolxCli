using Evolx.Cli.Dataverse;

namespace Evolx.Cli.Commands.Dv.Schema;

/// <summary>
/// Convenience publishers used by mutation verbs when --publish is set. Centralized
/// so every "publish what you just changed" call site uses the same scope-narrowing
/// rules and the same XML envelope.
/// </summary>
internal static class PublishHelper
{
    public static Task PublishEntityAsync(DvClient dv, string logicalName, CancellationToken ct)
    {
        var xml = PublishXml.Build(
            entityLogicalNames: new[] { logicalName },
            webResourceIds: Array.Empty<string>(),
            optionSetNames: Array.Empty<string>());
        return dv.InvokeActionAsync("PublishXml", new PublishXmlBody(xml), ct: ct);
    }

    public static Task PublishOptionSetAsync(DvClient dv, string optionSetName, CancellationToken ct)
    {
        var xml = PublishXml.Build(
            entityLogicalNames: Array.Empty<string>(),
            webResourceIds: Array.Empty<string>(),
            optionSetNames: new[] { optionSetName });
        return dv.InvokeActionAsync("PublishXml", new PublishXmlBody(xml), ct: ct);
    }
}
