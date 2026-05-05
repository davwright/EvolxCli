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

### `ev dv …` — Dataverse

| Command | Description |
|---|---|
| `ev dv connect <env>` / `--clear` | Bind this shell to a Dataverse environment (or forget). |
| `ev dv whoami` | Show the bound env + WhoAmI. |
| `ev dv query <table> [--filter --select --orderby --top]` | OData GET with filters. |
| `ev dv data <table> [--all] [--page-size N]` | Paged GET; `--all` follows `@odata.nextLink`. |
| `ev dv create <table> --json '{…}'` | POST a new row. |
| `ev dv update <table> <id> --json '{…}'` | PATCH an existing row. |
| `ev dv delete <table> <id>` | DELETE a row by id. |
| `ev dv columns <table> [--custom-only --required --type X]` | List columns + types. |
| `ev dv tables [--include-system]` | List entity definitions (default: custom only). |
| `ev dv table <logical>` | Full metadata + attributes for one table. |
| `ev dv choices [--name <schema>]` | List global option sets, or expand one. |
| `ev dv metadata --out file.xml [--filter PREFIX]` | Download `$metadata` CSDL XML. |
| `ev dv roles` | List security roles. |
| `ev dv role <name-or-id> [--privileges]` | Show one role; expand its privileges. |
| `ev dv user-roles <user>` | Roles assigned to a user (GUID, email, or partial name). |

Most read verbs accept `--json` to print the raw response body. `--json` flags on write verbs accept either inline JSON or `@path/to/file.json`.

### `ev dv schema …` — schema mutation

| Command | Description |
|---|---|
| `ev dv schema table new <schema-name> [flags] [--solution X --publish]` | Create a table. Flags: `--display-name`, `--display-name-de`, `--plural-name`, `--plural-name-de`, `--description`, `--description-de`, `--ownership`, `--activity`, `--enable-auditing`, `--enable-duplicate-detection`, `--enable-offline`, `--enable-notes`, `--enable-activities`. |
| `ev dv schema table update <logical> [flags]` | Update labels / auditing / duplicate-detection on an existing table. |
| `ev dv schema table remove <logical> --yes` | Delete a table (gated on `--yes`). |
| `ev dv schema column new <table> <schema-name> --type X [flags]` | Create a column. `--type` ∈ text, memo, integer, decimal, money, boolean, date, datetime, choice, multi-choice, lookup, customer, image. Type-specific flags: `--max-length`, `--precision`, `--min/--max`, `--true-label/--false-label`, `--choices "a;b;c"`, `--global-option-set`, `--target` (lookup), `--max-size-kb` / `--can-store-full-image`. |
| `ev dv schema column update <table> <column> [flags]` | Update labels / required level / max length on a column. |
| `ev dv schema column remove <table> <column> --yes` | Delete a column. |
| `ev dv schema column copy --from <env>:<table>.<col> --to <table>.<col> [--filter --overwrite]` | Copy a column's values across environments. |
| `ev dv schema choice new <name> --options "a;b;c" [flags]` | Create a global option set. |
| `ev dv schema choice update <name> [flags]` | Update labels or add new options. |
| `ev dv schema choice remove <name> --yes` | Delete a global option set. |
| `ev dv schema many-to-many <name> --table-a X --table-b Y [--intersect-name X]` | Create an N:N relationship. |
| `ev dv schema polymorphic-lookup <table> <name> --targets a,b,c [flags]` | Create a polymorphic lookup column. |
| `ev dv schema publish [--all \| --table X --option-set Y]` | Publish customizations. |

Every mutation accepts `--solution <NAME>` to scope the change to a Dataverse solution (sets `MSCRM.SolutionUniqueName`) and `--publish` to publish the affected entity / option set after the change lands. After every mutation we re-read to confirm the change actually applied — surfaces silent-skip-on-duplicate-name behavior as `SchemaMutationDidNotApplyException`.

### `ev pp …` — Power Platform admin

| Command | Description |
|---|---|
| `ev pp envs` | List Power Platform environments via the BAP admin API. |

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

- **`ev ado wi`** ✅ done (create, close, get, list, comment, link)
- **`ev ado pr`** ✅ done (list, get, create, comment)
- **`ev ado repo`** ✅ done (list, clone)
- **`ev dv …` Cluster A — read & trivial CRUD** ✅ done (connect, whoami, query, data, create, update, delete, columns, tables, table, choices, metadata, roles, role, user-roles)
- **`ev dv schema …` Cluster B — schema mutation** ✅ done (table new/update/remove, column new/update/remove/copy, choice new/update/remove, many-to-many, polymorphic-lookup, publish)
- **`ev pp envs`** ✅ done — list Power Platform environments via BAP
- **`ev canvas …`** — wraps canvas-app-tester invocations
- **`ev solution …`** — pack/import/export Power Platform solutions

## License

MIT
