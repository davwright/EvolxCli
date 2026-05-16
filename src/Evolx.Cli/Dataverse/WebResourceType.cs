namespace Evolx.Cli.Dataverse;

/// <summary>
/// Dataverse <c>webresourcetype</c> option-set values, keyed by file extension.
/// Source: Microsoft.Dynamics.CRM.WebResourceType. Matches the mapping in
/// the existing Push-DVWebResource PowerShell cmdlet.
/// </summary>
public static class WebResourceType
{
    private static readonly Dictionary<string, int> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // 1 = Webpage(HTML)   2 = StyleSheet(CSS)  3 = Script(JScript)  4 = Data(XML)
        // 5 = Image(PNG)      6 = Image(JPG)       7 = Image(GIF)       8 = Silverlight(XAP)
        // 9 = StyleSheet(XSL) 10 = Image(ICO)      11 = Image(SVG)      12 = String(RESX)
        ["html"] = 1, ["htm"] = 1,
        ["css"] = 2,
        ["js"] = 3,
        ["xml"] = 4,
        ["png"] = 5,
        ["jpg"] = 6, ["jpeg"] = 6,
        ["gif"] = 7,
        ["xap"] = 8,
        ["xsl"] = 9, ["xslt"] = 9,
        ["ico"] = 10,
        ["svg"] = 11,
        ["resx"] = 12,
    };

    /// <summary>Map a file path or extension to the webresourcetype int. Throws on unknown.</summary>
    public static int FromPath(string filePath)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.');
        if (string.IsNullOrEmpty(ext))
            throw new ArgumentException($"File '{filePath}' has no extension; cannot infer web resource type.");
        if (!ByExtension.TryGetValue(ext, out var t))
            throw new ArgumentException($"Unknown web resource extension '.{ext}'. Pass --type to override.");
        return t;
    }

    /// <summary>Map a friendly type name (--type js) to the enum int. Throws on unknown.</summary>
    public static int FromName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            throw new ArgumentException("Type name was empty.");
        if (!ByExtension.TryGetValue(typeName, out var t))
            throw new ArgumentException(
                $"Unknown web resource type '{typeName}'. Valid: js, html, htm, css, xml, png, jpg, jpeg, gif, ico, svg, resx, xsl, xslt, xap.");
        return t;
    }
}
