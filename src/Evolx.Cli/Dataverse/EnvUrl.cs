namespace Evolx.Cli.Dataverse;

/// <summary>
/// Accepts the env URL forms users actually type and normalizes to a full https URL.
///
/// Forms accepted:
///   osis-dev.crm4                          -> https://osis-dev.crm4.dynamics.com
///   osis-dev.crm4.dynamics.com             -> https://osis-dev.crm4.dynamics.com
///   https://osis-dev.crm4.dynamics.com     -> https://osis-dev.crm4.dynamics.com
///   https://osis-dev.crm4.dynamics.com/    -> https://osis-dev.crm4.dynamics.com  (trailing / stripped)
/// </summary>
public static class EnvUrlResolver
{
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Empty environment URL.", nameof(input));

        var s = input.Trim().TrimEnd('/');

        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return s.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

        // Bare host: add the dynamics.com suffix if it's missing
        if (!s.Contains(".dynamics.com", StringComparison.OrdinalIgnoreCase))
            s = $"{s}.dynamics.com";

        return $"https://{s}";
    }
}
