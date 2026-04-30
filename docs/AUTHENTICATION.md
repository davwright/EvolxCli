# Authentication

How `ev` (and the team broadly) handles authentication for Azure DevOps, Dataverse, and the rest of the Microsoft cloud.

## TL;DR — the rule

> **`az login` once. Everything else uses `az` to fetch tokens.** `pac` is only invoked when an action genuinely requires it (canvas pack/unpack today; possibly nothing tomorrow).

```bash
# One time (or once every ~90 days):
az login

# That's it. From now on, ev gets fresh tokens silently:
ev ado wi list
ev dv query evo_sites
```

If `az` says you're signed in, `ev` can talk to ADO, Dataverse, Graph, and any Entra-protected resource. No PATs, no token files in `~/.evolx/`, no `pac auth create` ceremony.

## Why this is the simplest approach

`az login` does an interactive Entra sign-in (browser pop-up, MFA, conditional access — all handled by Microsoft) and stashes a **refresh token** in your OS credential store. Refresh tokens last ~90 days of inactivity and ~1 year of active use. As long as you run any `az ...` command (or `ev`, indirectly) at least once a quarter, **you stay signed in for the full year**.

Each call to `az account get-access-token --resource X` does one of:

1. Returns a cached access token if it's still valid (typically ~1h)
2. Silently uses the refresh token to get a fresh access token
3. (Rarely) prompts you to re-auth if the refresh token expired or was revoked

`ev` calls this on every command. You never see it. There's nothing to manage.

## Token lifetimes (for reference)

| Token | Default lifetime | Notes |
|---|---|---|
| Access token (any resource) | ~1 hour | Carries the actual API permission. Short for security. |
| Refresh token | ~24h inactivity, up to 90 days for tenant policy | Used to silently mint new access tokens. |
| Primary refresh token (Windows) | ~14 days, auto-renews while signed in to Windows | If the machine is Entra-joined, this is the deepest layer. |
| MFA prompt frequency | Tenant-configured, typically 7-90 days | "Stay signed in" extends this. |

For a developer using their machine daily, **MFA fires every few weeks at most**. For Evolx-managed devices that are Entra-joined, often less.

## Resource ids cheat-sheet

```bash
# Azure DevOps (any tenant — same resource id)
az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798

# Dataverse — the org URL is the resource
az account get-access-token --resource https://osis-dev.crm4.dynamics.com

# Microsoft Graph
az account get-access-token --resource https://graph.microsoft.com

# Power Platform admin (BAP)
az account get-access-token --resource https://service.powerapps.com/

# Power Platform service (Power Automate, Power Apps designer APIs)
az account get-access-token --resource https://service.flow.microsoft.com/
```

`ev` knows these via constants in `src/Evolx.Cli/Auth/AzAuth.cs`.

## Comparison: pac, az, TypeScript, spkl, Daxif

### `pac` (Power Platform CLI)

- **Auth model:** `pac auth create --url <env>` → opens a browser → stashes credentials in `%LOCALAPPDATA%\Microsoft\PowerAppsCli\authprofiles_v2.json`
- **Token cache lifetime:** Same Entra refresh-token mechanism under the hood, but pac's token cache is **separate from az's**. Signing in to pac doesn't sign you in to az and vice-versa.
- **What it's good for:** Canvas pack/unpack (`pac canvas pack/unpack`), PCF scaffolding (`pac pcf init`), solution component listing (`pac solution list-components`). It maintains 5000+ lines of format-aware code that would be a multi-week project to reimplement.
- **What it's bad for:** Anything where you want diagnostic visibility. `pac solution import` returns "Imported successfully" even when nothing changed (we've hit this in production). Custom REST gives you `OData-EntityId`, `Retry-After`, structured error payloads — pac throws them away.
- **Verdict:** Use only for the format-aware pieces. Don't use `pac auth` as a primary credential store.

### `az` (Azure CLI)

- **Auth model:** `az login` → browser → refresh token in OS credential store (Windows Credential Manager, macOS Keychain, etc.)
- **Token cache lifetime:** ~90 days inactivity, full year for active use, indefinite for daily use on a managed device.
- **What it's good for:** Tokens for any Entra-protected resource. Service principal sign-in (`az login --service-principal`). Federated identity in CI (`az login --federated-token`). Single source of truth — every other tool can borrow tokens from here.
- **What it's bad for:** Application-specific operations (it doesn't know about Dataverse solutions or canvas apps).
- **Verdict:** **The credential broker for everything.**

### TypeScript / `@azure/identity`

- **Auth model:** `DefaultAzureCredential()` tries (in order): env vars → managed identity → VS Code creds → Azure CLI creds → interactive browser
- **Reuse of az session:** `AzureCliCredential()` literally shells out to `az account get-access-token`. So if `az login` worked, your TS app gets tokens for free.
- **Token cache lifetime:** Inherits from whichever underlying credential won the chain. With az: same as az.
- **What it's good for:** TypeScript apps that need Entra tokens (Functions, internal tools, MSAL.js wrappers). The chain pattern means your code works locally (via `az`) AND in CI (via managed identity) without changes.
- **Verdict:** If you're writing TS, use `@azure/identity` and let it find `az`. Don't roll your own MSAL.js flow unless you're building a user-facing OAuth app.

### `spkl` (Scott Durrow's Power Apps deployment toolkit)

- **Auth model:** Connection strings in `spkl.json` — username + password, or app-id + secret. Stores credentials in plaintext config files.
- **Modern fork (`spkl 2.x`):** Supports `az` token brokering via `--useAzureCli` flag.
- **What it's good for:** CI deployments where you can't browse to log in and managed identity isn't an option. Webresource sync (`spkl webresources`), plugin registration (`spkl plugins`).
- **What it's bad for:** Local dev — connection strings in config files are a security anti-pattern in 2026.
- **Verdict:** Niche tool. If you reach for it, prefer the `--useAzureCli` mode and avoid embedding secrets in spkl.json.

### Daxif (delegate-A daxif)

- **Auth model:** F# library, takes credentials as function parameters. Originally PowerShell-friendly with username/password; supports app-id + cert and interactive flows in newer versions.
- **Token cache lifetime:** None of its own — caller manages it.
- **What it's good for:** F# / .NET deployment scripts where you want strongly-typed wrappers around Dataverse SDK. Plugin registration, webresource diff, solution merge.
- **What it's bad for:** Any context where you don't already have a .NET F# build pipeline. Heavyweight for the problem.
- **Verdict:** Used by some Microsoft Dynamics shops, not relevant to Evolx unless you adopt F#.

## Comparison table

| Tool | Stores creds where | Lifetime | Reusable by other tools | Best at |
|---|---|---|---|---|
| `az` | OS credential store | ~90d-1y | **Yes** (every other tool can borrow) | Universal token broker |
| `pac` | `%LOCALAPPDATA%\Microsoft\PowerAppsCli\` | Hours-days | No (opaque) | Canvas pack/unpack, PCF scaffolding |
| `@azure/identity` (TS) | Defers to az/MI/etc. | Same as backend | Yes (uses az) | TS apps |
| `spkl` | `spkl.json` (often plaintext) or az | Connection-string lifetime | Limited | Bulk webresource/plugin deploy in CI |
| `Daxif` | Caller's responsibility | Caller's choice | No (library, not a tool) | F# deployment scripts |

## The "set it and forget it" recipe

```bash
# 1. Sign in once
az login

# 2. (Optional) Make MFA last as long as your tenant allows
#    On the browser sign-in page, check "Stay signed in".
#    This adds your device to the persistent-auth allowlist.

# 3. Verify
az account show
ev ado wi list  # should just work
```

If you're on a daily-use machine, you'll **forget you ever signed in** until the next time policy forces a refresh — usually 30-90 days. Run any `ev` command in that window and the refresh happens silently. Run nothing for 90+ days and `az login` again.

### Tenant-policy caveats

If your tenant has aggressive Conditional Access (e.g. "MFA every 7 days regardless"), nothing fixes that on the client. The cure is at the tenant level: longer sign-in frequency, or device-based trust signals (Entra-joined / Intune-compliant device extends the window).

## CI / build agents

Don't use PATs. Use **federated identity** for Azure DevOps Pipelines:

```yaml
# azure-pipelines.yml
- task: AzureCLI@2
  inputs:
    azureSubscription: 'evolx-federated-connection'  # service connection with workload identity federation
    scriptType: bash
    scriptLocation: inlineScript
    inline: |
      az account show
      ev ado wi list
      ev dv query evo_sites --env osis-dev.crm4
```

The `AzureCLI@2` task signs `az` in to the service connection's identity; the rest of the pipeline runs as that identity. No secrets stored anywhere.

For local CI experiments without federation: managed identity on a build VM, or a service principal with a certificate (cert in Key Vault, retrieved via managed identity at job start). Avoid client secrets in pipeline variables.

## Why we deliberately don't cache tokens in `ev`

Every `ev` command shells out to `az account get-access-token`. That's:

- ~150ms per call on a warm cache hit
- ~600ms when az has to refresh

We could cache the access token in `~/.evolx/token.json` to save the 150ms. We don't, because:

1. **No credential storage code = no credential storage bugs.** We can't leak what we don't store.
2. **Refresh transparency.** When the access token expires or is revoked, az's next call notices and refreshes. If we cached, we'd be re-implementing that.
3. **Multi-tenant safety.** `az` knows which tenant/subscription you're currently scoped to. If we cached a token, and you `az login --tenant other`, we'd serve stale tokens until the user noticed.

The 150ms is the cost of correctness. Worth it.

## Troubleshooting

### "az account get-access-token failed (exit 1)"

Run `az login`. If you just did, run `az account show` to confirm the session is alive. If it's not, your refresh token expired (90+ days unused) or was revoked.

### "az is not recognized"

Install Azure CLI: `winget install Microsoft.AzureCLI` (Windows) or `brew install azure-cli` (Mac).

### "Unauthorized" / 401 from a specific resource

The resource id in `--resource` is wrong, or your account doesn't have permission on that environment. Check:

```bash
az account get-access-token --resource <thing> --query expiresOn  # is the token live?
az account show                                                    # are you in the right tenant?
```

For Dataverse specifically: confirm your user has at least the System User role in that environment.

### "MFA required" loops

Tenant Conditional Access fired. Usually clears with `az login --tenant <tenant-id>` followed by completing the MFA challenge in browser. If it loops, the device may not satisfy the device-trust requirement (e.g. CA policy demands Hybrid Entra Join).

## Implementation in `ev`

See [src/Evolx.Cli/Auth/AzAuth.cs](../src/Evolx.Cli/Auth/AzAuth.cs).

The whole auth surface is two methods:

```csharp
// Get a token for any Entra-protected resource
public static async Task<string> GetAccessTokenAsync(string resource, CancellationToken ct = default)

// Look up the current signed-in user (used by `ev ado pr list --mine`)
public static async Task<string> GetCurrentUserObjectIdAsync(CancellationToken ct = default)
```

Both shell out to `az` via a shared `RunAzAsync` helper. ~50 lines total. No MSAL.js, no DPAPI, no `~/.evolx/credentials.json`.

When we eventually need to cache something for performance, the place to put it is **in front of az** (an in-process LRU on token+resource), not behind it (don't reinvent refresh).
