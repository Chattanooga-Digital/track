# Track - Claude Code Guide

## Project Overview

Track is a Docker Swarm monitoring app. It polls the Docker engine on one or more
nodes at a regular interval for per-container CPU, memory, and disk usage, groups
those containers into **stacks** (by Docker/Compose labels), and groups stacks into
**customers** (a user-defined mapping). A Vue 3 dashboard shows live totals per
customer and per stack, and historical charts.

## Solution Structure

```
Track/            # Core library: entities, TrackContext, Docker collectors, options
Track.Website/    # ASP.NET Core Web API backend + hosted polling services + SignalR hub
Track.Tests/      # NUnit test suite
Track.Vue/        # Vue 3 + TypeScript frontend (builds into Track.Website/wwwroot)
```

## Technology Stack

**Backend:**
- .NET 10.0, ASP.NET Core 10.0
- Entity Framework Core 10.0 with SQLite (local time-series storage)
- [Docker.DotNet](https://github.com/dotnet/Docker.DotNet) for container list + stats
- Raw `HttpClient` over the Docker socket for `GET /system/df` (disk usage; not exposed by Docker.DotNet)
- SignalR for pushing live snapshots to the browser
- OpenAPI (dev only)

**Frontend:**
- Vue 3 + TypeScript, Vite bundler
- Vuetify 4 (Material Design components), dark theme by default
- Pinia for state (the live snapshot store)
- Axios for REST, `@microsoft/signalr` for the live feed
- Chart.js + vue-chartjs for history charts

## How the Data Flows

```
DockerStatsCollector ─┐
                      ├─► StatsPollingHostedService ─► CurrentStateHolder ─► SnapshotBuilder ─► SignalR ─► Vue
DockerDiskCollector ──┘            │                        (in-memory)            │
                                   └─► ContainerSamples / DiskSamples (SQLite) ────┴─► StatsController /history ─► charts
```

1. **`StatsPollingHostedService`** (interval `Track:PollIntervalSeconds`, default 15s)
   lists containers on every configured node and pulls a one-shot stats reading for
   each. It writes raw `ContainerSample` rows to SQLite, updates the in-memory
   `CurrentStateHolder`, then broadcasts a grouped `SnapshotDto` over SignalR.
2. **`DiskPollingHostedService`** (interval `Track:DiskPollIntervalSeconds`, default
   300s — `/system/df` is expensive) records per-container writable-layer sizes and
   per-volume sizes as `DiskSample` rows and merges them into `CurrentStateHolder`.
3. **`SamplePruningHostedService`** deletes samples older than `Track:RetentionDays`.
4. **`SnapshotBuilder`** joins the latest poll results with the customer/stack
   assignments from SQLite into the customer → stack → container tree the UI renders.

## Authentication

**ASP.NET Core Identity**, cookie-based, following the Lutra pattern. `User : IdentityUser<Guid>`
and `Role : IdentityRole<Guid>` (`Track/Data/Models/`), and `TrackContext` derives from
`IdentityDbContext<User, Role, Guid>`. Do not hand-roll password hashing — Identity's
`UserManager`/`SignInManager` own it.

- Wired in `AddTrack` (`Track/DependencyInjection/ServiceCollectionExtensions.cs`) via
  `AddIdentity<User, Role>().AddEntityFrameworkStores<TrackContext>().AddDefaultTokenProviders()`,
  with `ConfigureApplicationCookie` returning 401/403 for the API instead of redirecting.
  The classlib carries a `FrameworkReference` to `Microsoft.AspNetCore.App` so these
  extensions resolve (same as Lutra's core project).
- Seeded on first startup by `DatabaseInitializer.SeedAdminAsync` using `RoleManager` +
  `UserManager.CreateAsync` from `Track:AdminUsername` / `Track:AdminEmail` /
  `Track:AdminPassword` (defaults `admin` / `admin`), added to the `Administrators` role.
- `AuthController` (`/api/v0/auth`) exposes `login` (`SignInManager.PasswordSignInAsync`,
  `lockoutOnFailure: true`), `logout` (`SignInManager.SignOutAsync`), and `me`.
- All data + configuration endpoints (`StatsController`, `CustomersController`,
  `StacksController`) and the SignalR hub carry `[Authorize]`. Only the auth and health
  endpoints and the static SPA files are anonymous — the SPA loads, then shows the login
  screen until `/auth/me` succeeds.
- Auth-cookie signing keys are Data Protection keys; set `Track:DataProtectionKeysPath`
  to a mounted-volume path (the compose file points it at `/app/data/keys`) so sessions
  survive restarts.
- The Vue side gates on `stores/authStore.ts`: `App.vue` calls `initialize()` once, shows
  `LoginView` until authenticated, and only then starts the live snapshot feed.

## Blended Cluster Utilization (Billing)

Each stats poll also reads `GET /info` per node (`NCPU`, `MemTotal`) to build a
`ClusterCapacity` (summed cores + memory across online nodes). `SnapshotBuilder` then
computes, at container-group, stack, customer, and cluster level:

- `cpuPercentOfCluster` = summed container CPU% ÷ (cores × 100) × 100. Container CPU% is
  per single core (100 = one full core), so cluster CPU capacity in the same unit is
  `cores × 100`.
- `memoryPercentOfCluster` = summed used memory ÷ cluster memory × 100.
- `blendedPercentOfCluster` = mean of the two — the single billing figure.

The `Billing` page ranks customers by blended share and surfaces the unbilled
(unassigned) remainder.

## Grouping Model

- **Stack**: derived from container labels — `com.docker.stack.namespace` (Swarm) or
  `com.docker.compose.project` (Compose). See `Services/Docker/DockerLabels.cs`.
  A container with neither label is grouped under the "No stack" bucket.
- **Customer**: a `Customer` row with a set of `StackAssignment` rows (one per stack
  name). Stack names are matched case-insensitively and are unique across customers —
  assigning a stack that already belongs to another customer moves it.
- Stacks not assigned to any customer appear under the "Unassigned stacks" group.

## Multi-Node / Swarm Configuration

Nodes are configured in `appsettings.json` under `Track:Nodes` (or the
`Track__Nodes__*` env vars). With none configured, the app falls back to the local
socket `unix:///var/run/docker.sock`.

```json
"Track": {
  "Nodes": [
    { "Name": "manager", "Endpoint": "unix:///var/run/docker.sock" },
    { "Name": "worker-1", "Endpoint": "tcp://10.0.0.11:2375" }
  ]
}
```

`docker stats` and `/system/df` are per-engine (a node only reports its own
containers), so to cover a whole swarm either expose each node's engine over TCP and
list them all here, or run one Track instance per node. A node that is unreachable
is reported as offline in the UI; its stale samples are dropped rather than shown as
current.

## C# Coding Conventions

Follows the standard house style (see `.editorconfig`):
- **File-scoped namespaces**, 4-space indent, `var` where the type is apparent
- **Nullable reference types** and **implicit usings** enabled
- **Collection expressions** (`[]`, `[.. items]`) over `new List<T>()`
- **Primary constructors** for services and controllers; on controllers, re-expose
  injected dependencies as `protected` properties (matches the Lutra pattern)
- `sealed` on hosted services and collectors that are not meant to be inherited
- Private fields `_camelCase`; interfaces `I`-prefixed; DTOs `...Model` suffix
- Keep comments to the non-obvious *why*, not a restatement of the code

### Entities

Each entity implements `IEntityTypeConfiguration<T>` on itself; `TrackContext`
discovers them with `ApplyConfigurationsFromAssembly`. Do not put fluent config in
`OnModelCreating`. All `DateTime` values are UTC.

### Database

- SQLite, one file (`track.db`). Schema is created on startup by
  `DatabaseInitializer` (migrations if present, else `EnsureCreated`).
- Always async EF methods; `.AsNoTracking()` on read-only queries; project with
  `.Select(...)` rather than loading full entities.
- The two sample tables are append-heavy time series — indexed on `Timestamp` and
  `(StackName, Timestamp)`. Pruning keeps them bounded.

### Controllers

- Route prefix `/api/v0/{resource}`, always `async Task<IActionResult>`
- Register services in `Track/DependencyInjection/ServiceCollectionExtensions.cs`
  via `AddTrack(IConfiguration)`; scoped request-time helpers (e.g. `SnapshotBuilder`)
  are registered in `Program.cs`.

## Error Handling

- Backend: don't swallow exceptions. The polling loops are the one deliberate
  exception — each tick is wrapped so a transient Docker/DB failure logs and retries
  on the next tick instead of killing the `BackgroundService`. Per-node failures are
  isolated so one bad node doesn't blank the others.
- Frontend: don't wrap API calls in `try/catch` that leaves a component blank; let
  errors surface. `catch {}` / `catch (e) { console.error(e) }` is banned.

## Frontend Conventions (Vue 3 / TypeScript)

- 2-space indent; formatting via `oxfmt`, linting via `oxlint` then `eslint`
- All Axios calls go through the typed clients in `src/api/` — no raw Axios in components
- Path alias `@` → `src/`
- The live snapshot arrives through `stores/snapshotStore.ts` (SignalR); views read it
  via `storeToRefs`. History is fetched on demand through `statsApi.getHistory`.
- Vuetify components for all UI

## Build & Local Development

```bash
# Backend (from repo root) — serves on http://localhost:5020
dotnet run --project Track.Website

# Frontend dev server (from Track.Vue/) — http://localhost:5173, proxies /api and /hubs to :5020
cd Track.Vue && npm install && npm run dev

# Tests
cd Track.Tests && dotnet test
```

The Vue app builds into `Track.Website/wwwroot` (`npm run build`), which the backend
serves as static files with an SPA fallback, so production is a single container.

## Docker

`docker compose up --build` runs the whole thing against the local engine. The socket
is mounted read-only. Because the app reads `/var/run/docker.sock` as a non-root user,
the image's `docker` group GID must match the host's:

```bash
DOCKER_GID=$(stat -c '%g' /var/run/docker.sock) docker compose up --build
```

## Formatting

```bash
dotnet format                    # backend, from repo root
cd Track.Vue && npm run format # frontend
```

## Versioning & Changelog

The app version lives in `Track.Website/Track.Website.csproj` as `<VersionPrefix>`.
The changelog is `docs/product-updates/changelog.md`. When completing a piece of work,
add a one-line entry under the current version (bump `VersionPrefix` for a new release),
newest version at the top, dated today, categorized `#### Features`, `#### Changes`, or
`#### Bug Fixes`. Keep entries short and user-facing; no em dashes.

## Git Workflow

- Do not commit or push unless explicitly asked.

## Key Things to Avoid

- Don't call `docker stats` with streaming on — use the one-shot two-read path in
  `DockerStatsCollector` so `PreCPUStats` is populated for the CPU delta.
- Don't compute memory as raw `usage` — subtract the inactive file cache to match
  what `docker stats` shows (handled in `ComputeMemoryUsedBytes`).
- Don't poll `/system/df` on the fast stats interval; it is heavy.
- Don't load full entities or use synchronous EF calls.
- Don't let one unreachable node fail the whole poll — failures are per-node.
```
