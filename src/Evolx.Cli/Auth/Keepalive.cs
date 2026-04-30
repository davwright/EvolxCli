using System.Diagnostics;
using System.Text.Json;

namespace Evolx.Cli.Auth;

/// <summary>
/// Keeps refresh-token inactivity clocks alive across all tenants the user has signed
/// into via `az login`. Without this, a tenant left untouched for 90 days requires
/// re-MFA when you next try to use it.
///
/// Strategy: on every `ev` invocation, check our own marker file. If the last keepalive
/// was &gt;= 7 days ago, mint a throwaway access token against every tenant in
/// `az account list`. This slides each tenant's inactivity clock forward.
///
/// Cost: zero in normal case (just a file-stat). Once a week: ~150ms per tenant.
/// </summary>
public static class Keepalive
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);
    private const string ManagementResource = "https://management.core.windows.net/";

    private static string MarkerPath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".evolx");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "last-keepalive");
        }
    }

    /// <summary>
    /// Call once per `ev` invocation. Returns immediately if a keepalive ran in the last
    /// RefreshInterval. Otherwise, refreshes every tenant az knows about.
    ///
    /// Failures (one or more tenants dead, az not installed, etc.) are logged to stderr
    /// but never throw — keepalive is best-effort, never blocks the user's actual command.
    /// </summary>
    public static async Task RunIfDueAsync(CancellationToken ct = default)
    {
        try
        {
            if (!IsDue()) return;

            var subs = await GetSignedInTenantsAsync(ct);
            if (subs.Count == 0) return;

            // Slide every (user, tenant) pair's inactivity clock forward by minting one
            // throwaway token per subscription. Using --subscription targets the right user
            // automatically and never mutates the active az context.
            int ok = 0, failed = 0;
            foreach (var sub in subs)
            {
                try
                {
                    await AzAuth.GetAccessTokenForSubscriptionAsync(ManagementResource, sub.SubscriptionId, ct);
                    ok++;
                }
                catch
                {
                    failed++;
                    // Don't log here — one dead RT shouldn't spam every command.
                    // The user will hit it at the actual usage site with a clear message.
                }
            }

            // Write marker even on partial failure — we did try, no point retrying every command.
            await File.WriteAllTextAsync(MarkerPath, DateTimeOffset.UtcNow.ToString("o"), ct);

            // Print a single quiet status line to stderr so the user sees something happened.
            // Stderr (not stdout) so it doesn't pollute pipes / scripts that consume ev output.
            var msg = failed == 0
                ? $"[ev] keepalive: refreshed {ok} tenant(s)"
                : $"[ev] keepalive: refreshed {ok}, {failed} failed (run `ev auth status` to inspect)";
            Console.Error.WriteLine(msg);
        }
        catch
        {
            // Never let keepalive break the user's command. Silent failure is correct here.
        }
    }

    private static bool IsDue()
    {
        if (!File.Exists(MarkerPath)) return true;
        try
        {
            var content = File.ReadAllText(MarkerPath).Trim();
            if (!DateTimeOffset.TryParse(content, out var last)) return true;
            return DateTimeOffset.UtcNow - last >= RefreshInterval;
        }
        catch { return true; }
    }

    /// <summary>
    /// Returns one (SubscriptionId, TenantId, User) per unique (user, tenant) pair the
    /// user has signed into. Iterating by subscription is the only reliable way: each
    /// az subscription unambiguously identifies which user can mint tokens in which
    /// tenant. Iterating by tenant alone breaks when multiple users share a tenant or
    /// when one user is signed into multiple tenants.
    /// </summary>
    private record SubInfo(string SubscriptionId, string TenantId, string User);

    private static async Task<List<SubInfo>> GetSignedInTenantsAsync(CancellationToken ct)
    {
        var json = await RunAzCaptureAsync("account list --output json", ct);
        if (string.IsNullOrWhiteSpace(json)) return new();

        using var doc = JsonDocument.Parse(json);
        // Dedupe by (user, tenant) since one tenant can have many subs but they share an RT.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SubInfo>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var subId = el.TryGetProperty("id", out var s) ? s.GetString() : null;
            var tid = el.TryGetProperty("tenantId", out var t) ? t.GetString() : null;
            var user = el.TryGetProperty("user", out var u) && u.TryGetProperty("name", out var un)
                ? un.GetString() : null;
            if (string.IsNullOrEmpty(subId) || string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(user)) continue;

            var key = $"{user}|{tid}";
            if (!seen.Add(key)) continue;
            result.Add(new SubInfo(subId, tid, user));
        }
        return result;
    }

    private static async Task<string> RunAzCaptureAsync(string args, CancellationToken ct)
    {
        ProcessStartInfo psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c az {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
            : new ProcessStartInfo
            {
                FileName = "az",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

        using var proc = Process.Start(psi);
        if (proc == null) return "";
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode == 0 ? stdout : "";
    }
}
