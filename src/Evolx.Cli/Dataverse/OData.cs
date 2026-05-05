namespace Evolx.Cli.Dataverse;

/// <summary>
/// OData v4 literal-escaping helper. Centralized so every cmdlet escapes the same way
/// — no inline `Replace("'", "''")` scattered through the call sites. Query-string
/// composition itself uses <see cref="Http.QueryString.Build"/>.
/// </summary>
internal static class OData
{
    /// <summary>
    /// Escape a string literal for use inside a single-quoted OData value, per OData 4.0
    /// §5.1.1.6.1 — single quotes are doubled. The result is the inner text only;
    /// callers wrap it in `'...'` themselves where the syntax demands it.
    /// </summary>
    public static string EscapeLiteral(string value) => value.Replace("'", "''");
}
