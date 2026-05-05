using System.Xml.Linq;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Builds the <c>ParameterXml</c> body for the Dataverse <c>PublishXml</c> action.
/// XML construction is via <see cref="XDocument"/> / <see cref="XElement"/> — never
/// string concat — so escaping, namespaces, and BOM-handling are all framework-driven.
/// </summary>
internal static class PublishXml
{
    /// <summary>Empty envelope — Dataverse interprets this as "publish all customizations".</summary>
    public static string PublishAll() => Build(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    /// <summary>
    /// Build a targeted publish envelope. Empty arrays produce missing sections (Dataverse
    /// is OK with that).
    /// </summary>
    public static string Build(
        IReadOnlyList<string> entityLogicalNames,
        IReadOnlyList<string> webResourceIds,
        IReadOnlyList<string> optionSetNames,
        IReadOnlyList<string>? dashboardIds = null,
        bool siteMap = false,
        bool ribbon = false)
    {
        var root = new XElement("importexportxml");

        AppendNamedList(root, "entities", "entity", entityLogicalNames);
        AppendNamedList(root, "webresources", "webresource", webResourceIds);
        AppendNamedList(root, "optionsets", "optionset", optionSetNames);
        if (dashboardIds is { Count: > 0 })
            AppendNamedList(root, "dashboards", "dashboard", dashboardIds);
        if (siteMap)
            root.Add(new XElement("sitemaps", new XElement("sitemap")));
        if (ribbon)
            root.Add(new XElement("ribbons", new XElement("ribbon")));

        // Dataverse accepts the body without an XML declaration; XDocument.ToString()
        // emits the inner XML which is exactly what PublishXml expects in ParameterXml.
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static void AppendNamedList(XElement parent, string container, string item, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return;
        var el = new XElement(container);
        foreach (var n in names) el.Add(new XElement(item, n));
        parent.Add(el);
    }
}
