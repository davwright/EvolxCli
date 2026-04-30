using System.Diagnostics;

namespace Evolx.Cli.Auth;

/// <summary>
/// Reuses an existing `az login` session to fetch access tokens for downstream APIs.
/// No interactive prompts; no PAT files. If the user isn't logged in, throws with a
/// clear message telling them to run `az login`.
/// </summary>
public static class AzAuth
{
    /// <summary>Azure DevOps resource id — same for any tenant.</summary>
    public const string AzureDevOpsResource = "499b84ac-1321-427f-aa17-267ca6975798";

    /// <summary>Dataverse resource is the org URL itself; pass `https://contoso.crm4.dynamics.com`.</summary>
    public static string DataverseResource(string envUrl) => envUrl.TrimEnd('/');

    public static async Task<string> GetAccessTokenAsync(string resource, CancellationToken ct = default)
    {
        var stdout = await RunAzAsync(
            $"account get-access-token --resource {resource} --query accessToken -o tsv", ct);
        var token = stdout.Trim();
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("`az account get-access-token` returned an empty token.");
        return token;
    }

    /// <summary>Returns the signed-in user's Entra object id (from `az ad signed-in-user show`).</summary>
    public static async Task<string> GetCurrentUserObjectIdAsync(CancellationToken ct = default)
    {
        var output = await RunAzAsync("ad signed-in-user show --query id -o tsv", ct);
        var id = output.Trim();
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("`az ad signed-in-user show` returned empty.");
        return id;
    }

    /// <summary>
    /// Runs `az &lt;args&gt;` and returns stdout. Wraps the Windows-vs-Linux shim quirk:
    /// `az` is a .cmd file on Windows and Process.Start can't invoke that directly when
    /// UseShellExecute=false, so we route through cmd /c there.
    /// </summary>
    private static async Task<string> RunAzAsync(string args, CancellationToken ct)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c az {args}",
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
                FileName = "az",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start `az`. Is the Azure CLI installed and on PATH?");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`az {args}` failed (exit {proc.ExitCode}). If this is an auth error, run `az login` first.\n{stderr.Trim()}");
        }

        return stdout;
    }
}
