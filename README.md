# Evolx CLI (`ev`)

Single CLI tool wrapping Azure DevOps, Dataverse, and Canvas App tooling. Subcommand tree, real type-checked C#, no curl-and-jq one-liners. Auth via existing `az login` session — no PATs.

## Quick start

```powershell
# Build & run from source (during dev)
cd src/Evolx.Cli
dotnet run -- ado wi list --type Issue --top 10
```

Or pack and install globally:

```powershell
cd src/Evolx.Cli
dotnet pack -c Release
dotnet tool install -g --add-source ./nupkg Evolx.Cli
ev ado wi list --type Issue
```

## Verbs

### `ev ado wi …` — Azure DevOps work items

| Command | Description |
|---|---|
| `ev ado wi create <type> <title> [--description X] [--parent N]` | Create a work item |
| `ev ado wi close <ids> [--state Done]` | Set state on one or more (comma-separated) ids |
| `ev ado wi get <id>` | Show one work item |
| `ev ado wi list [--state X] [--type Y] [--assigned-to Z] [--top N]` | List with filters |

Defaults to org `evolx` / project `Evolx.Intern.Microsoft` unless overridden by `--organization`/`--project` or env vars `EVOLX_ADO_ORG` / `EVOLX_ADO_PROJECT`.

### Examples

```powershell
# List all open issues assigned to me
ev ado wi list --type Issue --assigned-to "@me" --state "To Do"

# Create an epic, then a child issue under it
ev ado wi create Epic "M1: ..." --description "Goal: ..."
ev ado wi create Issue "POC: ..." --parent 90

# Close a batch
ev ado wi close 81,82,83,84
```

## Auth

`ev` reads your existing `az login` session — run `az login` once and it's good for the cache lifetime. No PATs to manage. The CLI shells out to `az account get-access-token` per request to fetch fresh tokens for the right resource:

- **Azure DevOps** REST → `499b84ac-1321-427f-aa17-267ca6975798` (the well-known ADO resource id)
- **Dataverse** (when added) → the org URL itself

## Architecture

```
src/Evolx.Cli/
  Program.cs                    — Spectre.Console.Cli command tree
  Auth/AzAuth.cs                — wraps `az account get-access-token`
  Ado/
    AdoClient.cs                — typed HttpClient wrapper for ADO REST
    Models.cs                   — DTOs (WorkItem, JsonPatchOp, etc.)
  Commands/
    Settings.cs                 — shared --organization/--project flags
    Ado/WorkItem/
      CreateCommand.cs
      CloseCommand.cs
      GetCommand.cs
      ListCommand.cs
```

Each verb is a single C# class deriving from `AsyncCommand<TSettings>`. Add a new one, register it in `Program.cs`, get help text + arg parsing for free.

## Tests

```powershell
# Unit + gateway tests (no network — fast, ~6s)
dotnet test --filter "Category!=Live"

# Live integration tests (hit real osis ADO/Dataverse — needs az login)
dotnet test --filter "Category=Live"

# Everything
dotnet test
```

Test layout:

- `tests/Evolx.Cli.Tests/Dataverse/` — pure-logic unit tests (env URL parsing, profile shape)
- `tests/Evolx.Cli.Tests/Http/` — gateway tests with `FakeHttpHandler`: 200, 204, 429-with-retry, 429-exhausted, 4xx-errors, network failure, bearer/header round-tripping
- `tests/Evolx.Cli.Tests/Live/` — `[Trait("Category","Live")]` integration tests against osis-dev. Read-only by default. Skip in CI by filtering them out.

## Further reading

- [docs/AUTHENTICATION.md](docs/AUTHENTICATION.md) — how `az` brokers tokens, multi-tenant keepalive, comparison with pac/spkl/Daxif
- [docs/PERFORMANCE.md](docs/PERFORMANCE.md) — startup timing breakdown, when AOT pays off, why DLL splitting doesn't

## Roadmap

- **`ev ado wi`** ✅ done (create, close, get, list)
- **`ev ado pr`** — list/create/comment on pull requests
- **`ev ado repo`** — list, clone, find
- **`ev dv …`** — Dataverse query, seed, export (replaces DataverseCmdlets PowerShell module incrementally)
- **`ev canvas …`** — wraps canvas-app-tester invocations
- **`ev solution …`** — pack/import/export Power Platform solutions

## License

MIT
