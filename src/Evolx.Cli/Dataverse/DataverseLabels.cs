using System.Text.Json;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Readers for the recurring shapes Dataverse metadata returns: simple strings,
/// LocalizedLabel objects, and required-level enums. Centralized so every read
/// command parses these the same way.
/// </summary>
internal static class DataverseLabels
{
    /// <summary>Read a property as a string, or "" if missing/wrong-kind.</summary>
    public static string String(JsonElement row, string name) =>
        row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";

    /// <summary>Read a property as bool, default false.</summary>
    public static bool Bool(JsonElement row, string name) =>
        row.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Read a Label-shaped property: <c>row.{name}.UserLocalizedLabel.Label</c>.
    /// Returns "" when any link in the chain is missing or null. Used for DisplayName,
    /// DisplayCollectionName, Description, and option Labels.
    /// </summary>
    public static string LocalizedLabel(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var node)) return "";
        if (!node.TryGetProperty("UserLocalizedLabel", out var ull) || ull.ValueKind == JsonValueKind.Null) return "";
        return ull.TryGetProperty("Label", out var lbl) && lbl.ValueKind == JsonValueKind.String
            ? lbl.GetString() ?? ""
            : "";
    }

    /// <summary>Read an EnumProperty-shaped value: <c>row.{name}.Value</c> as string.</summary>
    public static string EnumValue(JsonElement row, string name) =>
        row.TryGetProperty(name, out var node) && node.TryGetProperty("Value", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
