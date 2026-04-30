using System.Text.Json;
using System.Text.Json.Serialization;

namespace Evolx.Cli.Dataverse;

/// <summary>
/// Persists the user's currently-selected Dataverse environment so subsequent `ev dv ...`
/// commands don't need --env every call. Lives at ~/.evolx/profile.json.
///
/// Deliberately simple: just env URL. No tokens cached here — those come from `az`.
/// </summary>
public sealed class DvProfile
{
    [JsonPropertyName("envUrl")]
    public string? EnvUrl { get; set; }

    [JsonPropertyName("setAtUtc")]
    public DateTimeOffset? SetAtUtc { get; set; }

    private static string ProfilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".evolx");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "profile.json");
        }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static DvProfile Load()
    {
        if (!File.Exists(ProfilePath)) return new DvProfile();
        try
        {
            var json = File.ReadAllText(ProfilePath);
            return JsonSerializer.Deserialize<DvProfile>(json, Options) ?? new DvProfile();
        }
        catch
        {
            // Corrupt profile file shouldn't break the user's command.
            return new DvProfile();
        }
    }

    public void Save()
    {
        SetAtUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(ProfilePath, JsonSerializer.Serialize(this, Options));
    }

    public static void Clear()
    {
        if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
    }

    /// <summary>
    /// Resolves the env URL to use for a command: explicit --env flag wins, else profile.
    /// Throws a clear error if neither is set.
    /// </summary>
    public static string Resolve(string? explicitEnv)
    {
        if (!string.IsNullOrWhiteSpace(explicitEnv))
            return EnvUrlResolver.Normalize(explicitEnv);

        var p = Load();
        if (string.IsNullOrWhiteSpace(p.EnvUrl))
            throw new InvalidOperationException(
                "No Dataverse environment bound. Run `ev dv connect <env>` first, or pass --env <url>.");

        return p.EnvUrl;
    }
}
