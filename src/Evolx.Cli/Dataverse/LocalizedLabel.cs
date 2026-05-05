using System.Text.Json.Serialization;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Writers for the Dataverse <c>Label</c> shape (DisplayName, Description, etc.) used in
/// every metadata POST/PUT body. Centralized so no command hand-rolls the LCID constants
/// or the <c>@odata.type</c> / property layout.
///
/// Reading is in <see cref="DataverseLabels"/>; writing is here.
/// </summary>
internal static class LocalizedLabel
{
    /// <summary>Locale ID for English (United States).</summary>
    public const int LcidEnUs = 1033;
    /// <summary>Locale ID for German (Germany). Used for the DE side of every osis label.</summary>
    public const int LcidDeDe = 1031;

    /// <summary>
    /// Build a Label set with EN (always) and DE (when supplied). Returns null when EN
    /// is null/whitespace — caller is responsible for omitting the field.
    /// </summary>
    public static LocalizedLabelSet? Build(string? en, string? de = null)
    {
        if (string.IsNullOrWhiteSpace(en)) return null;

        var entries = new List<LocalizedLabelEntry>(2)
        {
            new(en!, LcidEnUs),
        };
        if (!string.IsNullOrWhiteSpace(de))
            entries.Add(new(de!, LcidDeDe));

        return new LocalizedLabelSet(entries.ToArray());
    }
}

/// <summary>The wrapping object Dataverse expects for Label-shaped properties.</summary>
internal sealed record LocalizedLabelSet(
    [property: JsonPropertyName("LocalizedLabels")] LocalizedLabelEntry[] LocalizedLabels);

/// <summary>One entry inside a LocalizedLabels array.</summary>
internal sealed record LocalizedLabelEntry(
    [property: JsonPropertyName("Label")] string Label,
    [property: JsonPropertyName("LanguageCode")] int LanguageCode)
{
    [JsonPropertyName("@odata.type")]
    public string ODataType => "Microsoft.Dynamics.CRM.LocalizedLabel";
}
