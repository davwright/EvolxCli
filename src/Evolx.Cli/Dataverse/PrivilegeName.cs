namespace Evolx.Cli.Dataverse;

/// <summary>
/// Dataverse privilege names follow the form <c>prv&lt;Action&gt;&lt;Table&gt;</c>, where
/// Action is one of a fixed set. Splits the name into (Action, Table) so callers can
/// render or filter without inline string slicing.
/// </summary>
internal static class PrivilegeName
{
    private static readonly string[] Actions =
        { "Create", "Read", "Write", "Delete", "Append", "AppendTo", "Assign", "Share" };

    private const string Prefix = "prv";

    /// <summary>Returns (action, table). Either may be empty if the name doesn't fit the convention.</summary>
    public static (string Action, string Table) Split(string privilegeName)
    {
        if (string.IsNullOrEmpty(privilegeName) || !privilegeName.StartsWith(Prefix, StringComparison.Ordinal))
            return ("", "");

        var rest = privilegeName.AsSpan(Prefix.Length);

        // Match the longest action prefix (AppendTo before Append, etc.)
        foreach (var action in Actions.OrderByDescending(a => a.Length))
        {
            if (rest.StartsWith(action, StringComparison.Ordinal))
            {
                return (action, rest[action.Length..].ToString());
            }
        }
        return ("", privilegeName);
    }

    /// <summary>Render the depth-mask bitfield (1=User, 2=BU, 4=Parent-BU, 8=Org).</summary>
    public static string DepthLabel(int mask) => mask switch
    {
        1 => "User",
        2 => "BU",
        4 => "Parent BU",
        8 => "Org",
        0 => "(none)",
        _ => $"mask={mask}",
    };

    /// <summary>
    /// Parse a friendly depth name (Basic|Local|Deep|Global) into the int value the
    /// AddPrivilegesRole action expects.
    /// Mapping: Basic=1 (User), Local=2 (BU), Deep=4 (Parent-BU), Global=8 (Org).
    /// </summary>
    public static bool TryParseDepth(string? depth, out int value)
    {
        switch (depth?.Trim().ToLowerInvariant())
        {
            case "basic": case "user": case "1": value = 1; return true;
            case "local": case "bu": case "2": value = 2; return true;
            case "deep": case "parentbu": case "parent-bu": case "4": value = 4; return true;
            case "global": case "org": case "8": value = 8; return true;
            default: value = 0; return false;
        }
    }
}
