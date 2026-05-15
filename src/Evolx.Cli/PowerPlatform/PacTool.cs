using System.Diagnostics;

namespace Evolx.Cli.PowerPlatform;

/// <summary>
/// Thin wrapper around the Power Platform CLI (`pac`). `pac` is an opt-in dependency:
/// `ev` startup never touches it, and only the verbs that genuinely need it
/// (canvas pack/unpack today) call into here. Everything else works without pac installed.
///
/// Auth is intentionally NOT routed through pac — see docs/AUTHENTICATION.md.
/// `pac` is invoked only for local file operations on .msapp bundles.
/// </summary>
public static class PacTool
{
    /// <summary>
    /// Throws a clear, actionable error if `pac` isn't on PATH. Call this before any
    /// pac invocation. Cheap (one process spawn) but only called lazily from verbs that
    /// need pac — never on every `ev` startup.
    /// </summary>
    public static async Task EnsureInstalledAsync(CancellationToken ct = default)
    {
        try
        {
            var (exit, _, _) = await RunPacRawAsync("help", ct);
            if (exit != 0)
                throw new InvalidOperationException(InstallHint());
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(InstallHint(), ex);
        }
    }

    /// <summary>
    /// Runs `pac &lt;args&gt;` and streams stdout/stderr live to the user's console.
    /// Returns the exit code so the caller can propagate it. No output is captured —
    /// pac prints progress lines for long operations (pack/unpack), and a wrapper that
    /// swallows them and dumps at the end is worse than transparent passthrough.
    /// </summary>
    public static async Task<int> RunInteractiveAsync(string args, CancellationToken ct = default)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c pac {args}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "pac",
                Arguments = args,
                UseShellExecute = false,
            };
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(InstallHint());
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode;
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunPacRawAsync(string args, CancellationToken ct)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c pac {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "pac",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(InstallHint());

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, stdout, stderr);
    }

    private static string InstallHint() =>
        "Power Platform CLI (`pac`) is required for this command but was not found on PATH.\n" +
        "Install it via:  winget install Microsoft.PowerPlatformCLI\n" +
        "Then restart your shell so the new PATH entry is picked up.";
}
