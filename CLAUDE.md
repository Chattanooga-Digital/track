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

## Roles: Agent and Central

Track is **one app, one image, two roles**, selected by `Track:Mode` (`Central` default,
or `Agent`). `Program.cs` branches on it at startup. An **agent** runs on every node
(Swarm `mode: global`), reads its own local Docker socket, and pushes readings to the
**central** instance over HTTP. The central serves the UI/API/live feed and aggregates
what agents push — it does **not** read any Docker socket (pure aggregator). This keeps
each node's Docker API off the network: agents only make outbound calls.

## How the Data Flows

```
[each node]                              [central]
DockerStatsCollector ─┐                  IngestController ─► CurrentStateHolder ─► SnapshotBuilder ─► SignalR ─► Vue
                      ├─► Push services ──► POST /ingest        (in-memory)             │
DockerDiskCollector ──┘   (HTTP + secret)      │                                        │
                                               └─► ContainerSamples / DiskSamples ──────┴─► StatsController /history ─► charts
                                                   (SQLite, central only)
```

1. **Agent — `StatsPushHostedService`** (interval `Track:PollIntervalSeconds`, default
   15s) lists local containers, pulls a one-shot stats reading for each, and POSTs a
   `StatsReport` to the central's `/api/v0/ingest/stats`.
2. **Agent — `DiskPushHostedService`** (interval `Track:DiskPollIntervalSeconds`, default
   300s — `/system/df` is expensive) POSTs a `DiskReport` of per-container writable-layer
   and per-volume sizes to `/api/v0/ingest/disk`.
3. **Central — `IngestController`** validates the shared secret (`X-Track-Key` vs
   `Track:Ingest:Key`, constant-time), writes `ContainerSample`/`DiskSample` rows to
   SQLite, merges into the in-memory `CurrentStateHolder`, then broadcasts a grouped
   `SnapshotModel` over SignalR.
4. **Central — `NodeReaperHostedService`** marks a node offline and drops its stale
   samples once it stops pushing for `Track:NodeOfflineAfterSeconds` (default 45s), then
   rebroadcasts. This replaces the per-poll reachability check the old direct-poll model had.
   Node state is in-memory (keyed by hostname); the **Agents** UI shows per-node stats and can
   delete a decommissioned node via `AgentsController` (`DELETE /api/v0/agents/{name}`) — a
   still-running agent simply reappears on its next push.
5. **Central — `SamplePruningHostedService`** deletes samples older than `Track:RetentionDays`.
6. **Central — `SnapshotBuilder`** joins the latest results with the customer/stack
   assignments from SQLite into the customer → stack → container tree the UI renders.

Agents identify themselves by a stable node name (`AgentNodeNameResolver`): an explicit
`Track:NodeName`, else the Docker engine's own hostname (`/info` Name) — never the
container hostname, which changes on restart and would mint phantom nodes.

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
  `StacksController`, `BucketsController`, `S3SourcesController`) and the SignalR hub carry
  `[Authorize]`. Only the auth and health
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

### Monthly billing history

Past months cannot be reconstructed from raw samples (cluster capacity is in-memory only,
raw samples are pruned after `Track:RetentionDays`, and stack/bucket assignments change),
so billing is recorded as it happens. `BillingSampleHostedService` (central-only, every
`Track:BillingSampleIntervalSeconds`, default 300s) calls `BillingSampleRecorder`, which
builds the live snapshot and writes one `ClusterCapacitySample` (the tick) plus one
`BillingSample` per customer group — cluster shares, usage bytes, and the customer
attribution frozen at record time (`CustomerId` is deliberately not a foreign key, and
`CustomerName` is denormalized, so history survives customer deletion). Ticks are skipped
while no nodes are online. Retention is separate and long (`Track:BillingRetentionDays`,
default 730 days).

`GET /api/v0/stats/billing/months` lists recorded months; `GET /api/v0/stats/billing?month=yyyy-MM`
returns per-customer averages. Sums divide by the month's total tick count (not the
customer's own row count), so a customer present for part of a month bills proportionally.
The Billing page has a period selector: Live (the SignalR snapshot) or any recorded month.

## S3 Object Storage (Buckets)

Docker's `/system/df` only sees container writable layers and named volumes, so bucket
storage (and bind mounts) are invisible to the agents. Object storage is therefore polled
**directly by the central**, not by agents:

- **Sources live in the DB** (`S3Source` table), managed live from the **Storage** UI
  (`S3SourcesController`, `/api/v0/s3-sources`) — add, edit, disable, delete, and
  test-connection without a restart. Secret keys are stored **encrypted** via Data
  Protection (`S3SourceSecretProtector`, same key ring as the auth cookie) and never
  returned to clients. `Name` is the stable key bucket assignments reference, so it is
  immutable after creation. A one-time seed copies any `Track:S3Sources` config into the
  DB on first startup (empty table), after which config is ignored.
- **Sizing depends on the source `Kind`** (`S3UsageCollector`, `Track/Services/S3/`):
  - `Minio` — one signed call to the admin data-usage API (`MinioAdminClient`,
    `GET /minio/admin/v3/datausageinfo`, hand-rolled SigV4 since there is no .NET madmin
    client) returns every bucket's size **and object count** from the scanner. No
    per-object walk. Works for MinIO and RustFS.
  - `GenericS3` — falls back to walking `ListObjectsV2` (one request per 1000 objects).
    Portable but O(objects); use only for stores without an admin data-usage API.
  - The S3 client sets `RequestChecksumCalculation`/`ResponseChecksumValidation` to
    `WHEN_REQUIRED`. AWSSDK v4 defaults to `WHEN_SUPPORTED`, whose flexible-checksum
    headers make MinIO/RustFS reject requests as `SignatureDoesNotMatch`.
- The poll itself lives in `S3UsageRefresher` (scoped, `Track.Website/Services/`): loads enabled
  sources from the DB, reconciles the live state (`RetainBucketSources` drops deleted/disabled
  sources), polls each (per-source failures isolated; last good reading kept), writes `DiskSample`
  rows of `DiskUsageKind.Bucket` (`NodeName` = source, `Name` = bucket), merges into
  `CurrentStateHolder` via `SetBucketUsage`, and rebroadcasts. Passing a source name polls just
  that one (no reconcile — it knows nothing about the sources it skipped).
- `S3UsageHostedService` (central-only) is just a timer over it, running every
  `Track:S3PollIntervalSeconds` (default 900s). Because that interval is long, the refresher is
  also called on demand: `S3SourcesController` polls a source right after create/update (so its
  buckets are assignable immediately, not after the next cycle), and `POST /s3-sources/refresh`
  polls every enabled source from the Storage page's "Refresh usage" button. `POST /s3-sources/test`
  only proves reachability — it does not populate buckets.
- Buckets are **first-class**, not synthetic stacks. A `BucketAssignment` row
  (`SourceName` + `BucketName` → `CustomerId`, unique per bucket) maps a bucket to the
  customer billed for it, mirroring `StackAssignment`. `BucketsController`
  (`GET /api/v0/buckets`) lists them; `CustomersController` `PUT /customers/{id}/buckets`
  assigns them (moving a bucket already owned elsewhere), matching the stack flow.
  Deleting a source releases its bucket assignments.
- `SnapshotBuilder` places each bucket under its customer (or the unassigned group) and
  rolls bucket bytes into `UsageTotalsModel.ObjectStorageBytes`, kept **separate** from
  node-local `DiskBytes` at every level. Object storage does not affect blended billing
  (which is CPU + memory only). Object count is carried in live state only (not persisted
  as history).

Optional first-run seed (Central mode only; the UI manages sources thereafter):

```
Track__S3Sources__0__Name        stable id, used as the bucket's source in assignments
Track__S3Sources__0__Endpoint    e.g. http://minio:9000
Track__S3Sources__0__Kind        Minio (default) or GenericS3
Track__S3Sources__0__AccessKey
Track__S3Sources__0__SecretKey
Track__S3Sources__0__Region      optional, defaults to us-east-1 (MinIO ignores it)
Track__S3PollIntervalSeconds     optional, default 900
```

## Grouping Model

- **Stack**: derived from container labels — `com.docker.stack.namespace` (Swarm) or
  `com.docker.compose.project` (Compose). See `Services/Docker/DockerLabels.cs`.
- **Customer**: a `Customer` row with a set of `StackAssignment` rows (one per stack
  name). Stack names are matched case-insensitively and are unique across customers —
  assigning a stack that already belongs to another customer moves it.
- Stacks not assigned to any customer appear under the "Unassigned stacks" group.

### Label-less standalone containers (env-var identity)

Plain `docker run` containers (e.g. a Discourse forum) have no stack/compose label. Rather
than fall into a single "No stack" bucket, the agent groups them by a chosen **environment
variable's value** (its `DISCOURSE_HOSTNAME`, say), which becomes a synthetic stack name and
then flows through the normal stack → customer assignment unchanged.

- `ContainerGroupResolver` (agent, `Services/Docker/`) derives the key: stack/compose label
  first (no inspect); otherwise it inspects the container once (env is immutable, cached by
  container ID) and returns the value of the first configured identity env var present.
- The ordered identity list lives centrally (`IdentityEnvVar` table, edited via
  `/api/v0/grouping/identity-env-vars` and the **Unidentified** UI view) and is handed back to
  agents in the ingest push **response** (`IngestResponse`) — the one central→agent channel.
- **Security:** the agent surfaces only env var **key names** (in `StatsReport.Inventory`, held
  live in `ContainerInventoryHolder`), never values. Only the single chosen identity value ever
  reaches the central, as the stack name. Container secrets never leave the node. Secret-looking
  keys (names matching `SensitiveEnvKeys.Markers` — `password`, `secret`, `token`, …) are dropped
  from the inventory, refused as an identity by the resolver, and rejected by `GroupingController`,
  so a secret's value can never be sent as a stack name.
- A container with no matching identity var stays in the inventory (`GET /api/v0/inventory`)
  awaiting a choice; there is no image fallback (images collide across customers).

## Multi-Node / Swarm Configuration

`docker stats` and `/system/df` are per-engine — a node only reports its own
containers. Rather than exposing every node's Docker API over the network, Track covers
a swarm by running an **agent on each node** that reads its local socket and pushes to
the **central** instance. Deploy the agent as a Swarm `mode: global` service so it lands
on every node (including new ones) automatically.

Example stacks live in `deploy/`:
- `deploy/track-central.stack.yml` — the central service behind Traefik. No socket mount.
- `deploy/track-agent.stack.yml` — the global agent. Mounts each node's socket read-only.

Key config:

```
# Central
Track__Ingest__Key      shared secret agents must present (unset = ingest disabled)

# Agent
Track__Mode=Agent
Track__Central__Url     e.g. http://track:5020 (central's swarm service name on the shared overlay)
Track__Central__Key     must equal the central's Track__Ingest__Key
Track__NodeName         optional; in Swarm set to {{.Node.Hostname}} (else the engine hostname is used)
```

The agent reads its own socket, so it still needs the docker-socket group. In the stack
that is `user: "5678:<GID>"` where `<GID>` is `stat -c '%g' /var/run/docker.sock` on the
node. An agent that stops pushing is marked offline by the central after
`Track:NodeOfflineAfterSeconds`, and its stale samples are dropped rather than shown as
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
- Comment sparingly. Only the genuinely non-obvious *why* or an edge case earns a comment,
  and keep it to a single short line. No multi-line explanations, no restating the code. Most
  code needs no comment at all.

### Entities

Each entity implements `IEntityTypeConfiguration<T>` on itself; `TrackContext`
discovers them with `ApplyConfigurationsFromAssembly`. Do not put fluent config in
`OnModelCreating`. All `DateTime` values are UTC.

### Database

- SQLite, one file (`track.db`). **EF Core migrations are applied on startup** by
  `DatabaseInitializer.InitializeAsync` (`context.Database.MigrateAsync()`), then the admin
  is seeded. Migrations live in `Track/Data/Migrations/`.
- Always async EF methods; `.AsNoTracking()` on read-only queries; project with
  `.Select(...)` rather than loading full entities.
- The two sample tables are append-heavy time series — indexed on `Timestamp` and
  `(StackName, Timestamp)`. Pruning keeps them bounded.

#### Migrations

`dotnet ef` builds the context through `TrackContextFactory` (an
`IDesignTimeDbContextFactory` in `Track/Data/`) so scaffolding does not run the Website's
startup/seeding. Add a migration after any entity change — combine all schema changes for
one piece of work into a single migration:

```bash
# Add a migration (from repo root). Migrations live in Track/Data/Migrations, so pass --output-dir.
dotnet ef migrations add <Name> \
    --project Track/Track.csproj \
    --startup-project Track.Website/Track.Website.csproj \
    --output-dir Data/Migrations

# Remove the last (unapplied) migration
dotnet ef migrations remove \
    --project Track/Track.csproj \
    --startup-project Track.Website/Track.Website.csproj
```

There is no need to apply migrations by hand — the app runs them on the next startup.

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
