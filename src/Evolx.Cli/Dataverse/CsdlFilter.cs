using System.Xml.Linq;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// CSDL ($metadata) document pruning. Used by `ev dv metadata --filter` to keep only
/// schema entries whose Name starts with a publisher prefix. Namespace-aware: matches
/// elements by LocalName so we don't hard-code the OData EDM namespace URI.
/// </summary>
internal static class CsdlFilter
{
    private static readonly string[] Targets =
        { "EntityType", "ComplexType", "Action", "Function", "EntityContainer" };

    /// <summary>
    /// Remove every <see cref="Targets"/> element whose <c>Name</c> attribute does not start
    /// with <paramref name="prefix"/> (case-insensitive). Mutates the document in place.
    /// </summary>
    public static void Prune(XDocument doc, string prefix)
    {
        var toRemove = doc.Descendants()
            .Where(e => Targets.Contains(e.Name.LocalName))
            .Where(e =>
            {
                var name = (string?)e.Attribute("Name");
                return !string.IsNullOrEmpty(name) && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var node in toRemove) node.Remove();
    }
}
