# Performance & startup — guidance

How `ev` startup actually breaks down, what JIT does, and when (if ever) to reach for AOT or DLL splitting.

## Where the milliseconds go

A typical `ev ado wi list` cold-start, today:

| Phase | Cost | Notes |
|---|---|---|
| Process spawn | ~30 ms | OS-level (PE load, Windows loader). Fixed cost. |
| .NET runtime init | ~60 ms | Runtime DLLs. R2R-precompiled, already optimized. |
| JIT of `ev`'s own code | ~30 ms | Spectre command tree, our commands, the AdoClient |
| JIT of HttpClient on first use | ~50 ms | TLS + HTTP stack |
| `az` subprocess for token | ~150 ms | The elephant in the room |
| HTTP call to ADO | ~80 ms | Network |
| **Total** | **~400 ms** | Roughly half is the az subprocess |

**JIT is ~80ms of the total.** The az subprocess is twice that. Every optimization should target the biggest costs first.

## What JIT actually is

C# compiles in two steps:

1. **Build time** (`dotnet build`): C# → **IL** (Intermediate Language, in `.dll`)
2. **Run time**: IL → **machine code**, by the **JIT** (Just-In-Time compiler) inside the .NET runtime

The IL in a `.dll` is not directly executable. The CPU only runs native instructions. The JIT does the bridge, **lazily, per method, on first call**. Subsequent calls go straight to native — zero overhead.

For a server running for hours, JIT cost amortizes to nothing. For a CLI running 200ms total, it's a measurable chunk of every invocation. Same `Main()`, same `Program.cs`, same Spectre parser get JITted from scratch every time.

## What we already do

- **ReadyToRun (R2R)** — `dotnet publish` with `--self-contained` precompiles common framework methods to native at publish time. JIT skips them. Already on for our self-contained builds.
- **Tiered JIT** — produces fast-but-unoptimized native code first, then recompiles hot methods to optimized in the background. Doesn't help CLIs that exit before tier-1 kicks in.

## What we could do, in order of bang-for-buck

When ev startup becomes a real complaint, attack it in this order:

### 1. In-process token cache for the lifetime of one `ev` call (saves ~150ms per extra call)
Today every HTTP call inside one ev command re-fetches the token via `az`. For commands that make 2+ calls (e.g. `ev ado wi list` followed internally by a batch fetch), cache the token in memory for that process's lifetime.

Don't persist across processes — the value of keeping a fresh token from az is correctness. In-memory is just dedup.

### 2. Native AOT (`<PublishAot>true</PublishAot>`) (saves ~80ms per cold start)
AOT-compiles the **whole app** to native at publish time. Zero JIT at runtime. Startup drops from ~120ms of .NET overhead to ~10ms. Binary shrinks from ~50MB to ~10MB.

Cost: reflection-heavy code breaks. We'd need to:
- Confirm Spectre.Console.Cli works in AOT mode (it does since 0.49 — we're on 0.55, fine)
- Add `[JsonSerializable(typeof(...))]` to a context class so `System.Text.Json` uses source generators instead of reflection
- Audit any other reflection (we have very little — just JsonContent.Create and a JsonDocument scan)

Estimated effort: ~30 minutes of cleanup. The trimmer + AOT compiler will tell us where the warts are.

### 3. Skip the `cmd /c az` shim (saves ~30ms)
`az` on Windows is a `.cmd` file. Process.Start can't invoke `.cmd` directly with `UseShellExecute=false`, so we route through `cmd.exe`. The `cmd.exe` startup is ~30ms.

Workaround: locate `python.exe` (or the actual `az.exe` if a newer Az CLI uses one) and call it directly. More fragile across az versions; the 30ms saving is small. Probably not worth it unless we've already done #1 and #2.

## What NOT to reach for

### DLL splitting / plugin loading

**Does not reduce JIT cost.** The JIT only compiles methods that get called, regardless of which assembly they live in. A monolithic ev where `Commands/Dv/QueryCommand.cs` is never called pays the same JIT cost as a plugin-host ev where Dataverse plugins are never loaded — both are zero.

Real costs of DLL splitting:
- **Versioning hell** — each plugin DLL is its own csproj, version, build, ship. Multiplied by N tools.
- **Loss of refactor power** — IDE rename across one project: instant. Across DLL boundaries: build, copy, replace, restart.
- **Diagnostics get worse** — stack traces span assembly boundaries with confusing transitions.
- **Self-contained publish breaks** unless plugin discovery is carefully pre-wired.
- **All for ~5ms of theoretical win.**

Look at how grown-up CLIs handle it:
- `gh` (GitHub CLI), `kubectl`, `pac` — all monolithic single binaries
- `az` — split into "extensions" because Microsoft has 200 product teams contributing, NOT for performance
- Modern pattern when you DO need extensibility: third-party tools as **separate executables on PATH** (`git foo` finds `git-foo`), not DLL plugins

The ceiling for "monolithic is fine" is way higher than ev will ever reach. Add `Commands/Dv/QueryCommand.cs` next to `Commands/Ado/WorkItem/CreateCommand.cs` in the same project. Spectre auto-discovers them. Easy.

### Trimming alone (without AOT)

`<PublishTrimmed>true</PublishTrimmed>` shrinks the exe from ~50MB to ~10MB but doesn't meaningfully change startup. The runtime is still loaded; only unused IL is dropped. Useful for distribution size, not for perf. Goes hand-in-hand with AOT but isn't a separate lever worth pulling.

## How to measure before optimizing

```powershell
# Cold-start timing on Windows
Measure-Command { ev --help }
# Run 5 times, ignore the first (cache warmup), average the rest.

# What's actually slow
& "C:\Users\dwrigh\.local\bin\ev.exe" ado wi list --top 1 --verbose
# Add timing in Program.cs around await Keepalive.RunIfDueAsync()
# and around await app.RunAsync(args) if you want the breakdown.
```

If cold-start is over 500ms and you can show in numbers that JIT is ≥30% of that — pull lever 2 (AOT). If it's just slow because of az or network — no amount of code tuning will help; pull lever 1 (in-process token cache) and possibly call multiple ADO endpoints in parallel.

## Bottom line

- **JIT** is a runtime cost, ~80ms per cold start, paid every time.
- **R2R** (already on) reduces it; **Native AOT** eliminates it. AOT is the lever if startup becomes a complaint.
- **DLL splitting saves nothing** that monolithic doesn't already have. Don't reach for it.
- Stay monolithic. Add commands to the same project. Revisit only if `ev` grows past ~50 commands AND someone wants to ship customer-specific extensions — at which point the answer is **separate exes on PATH**, not DLL plugins.
