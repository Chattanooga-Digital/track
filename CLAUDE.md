# Track - Claude Code Guide

## Project Overview

Track is a Docker Swarm monitoring app. It polls the Docker engine on one or more
nodes at a regular interval for per-container CPU, memory, and disk usage, groups
those containers into **stacks** (by Docker/Compose labels), and groups stacks into
**customers** (a user-defined mapping). A Vue 3 dashboard shows live totals per
customer and per stack, and historical charts. It also backs up every volume to
off-site S3 with restic (see **Backups**).

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
  `StacksController`, `BucketsController`, `S3SourcesController`, `BackupsController`) and the
  SignalR hub carry `[Authorize]`. Approving agents, editing the backup target and replacing live
  data need the `Administrators` role. Only the auth and health
  endpoints and the static SPA files are anonymous — the SPA loads, then shows the login
  screen until `/auth/me` succeeds.
- Auth-cookie signing keys are Data Protection keys; set `Track:DataProtectionKeysPath`
  to a mounted-volume path (the compose file points it at `/app/data/keys`) so sessions
  survive restarts.
- The Vue side gates on `stores/authStore.ts`: `App.vue` calls `initialize()` once, shows
  `LoginView` until authenticated, and only then starts the live snapshot feed.
- **Password links.** `AccountEmails` emails an Identity password-reset token as
  `<Site URL>/set-password?user=&token=`: a `SetPassword` invitation while the user has no password, a
  `PasswordReset` after. Admins send one when creating a user (no password needed) or from the Users list;
  anyone can ask from the login page (`POST /auth/forgot`, by username or email, same answer either way, one
  link per address per 5 minutes). `POST /auth/set-password` uses the token, clears any lockout and signs the
  user in; `App.vue` shows `SetPasswordView` instead of the login on that route. Links use the configured Site
  URL, never the request's Host header. Users without an email have a null `Email`, not a placeholder.

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
- **Shared infrastructure**: a `SharedStack` or `SharedBucket` row marks a stack or bucket that serves the
  whole cluster (proxy, orchestration, monitoring, backups). Each belongs to at most one of a customer or
  shared infrastructure; assigning it to either moves it, from the owner's side (`PUT /stacks/shared`,
  `PUT /buckets/shared`, `PUT /customers/{id}/stacks|buckets`) or the item's (`PUT /stacks/{name}/assignment`,
  `PUT /buckets/assignment`). The shared group is reported (dashboard, billing, `BillingSample.IsShared`) but
  never billed, like unassigned.
- Stacks and buckets in neither appear under the "Unassigned stacks" group, which should normally be empty.

### Label-less standalone containers (env-var identity)

Plain `docker run` containers (e.g. a Discourse forum) have no stack/compose label. Rather
than fall into a single "No stack" bucket, the agent groups them by a chosen **environment
variable's value** (its `DISCOURSE_HOSTNAME`, say), which becomes a synthetic stack name and
then flows through the normal stack → customer assignment unchanged.

- `ContainerGroupResolver` (agent, `Services/Docker/`) derives the key: stack/compose label
  first, then the `digital.chattanooga.track.stack` label (no inspect); otherwise it inspects the
  container once (env is immutable, cached by container ID) and returns the value of the first
  configured identity env var present. Track's image sets that label to `track`, so a standalone
  agent never lands in Unidentified.
- The ordered identity list lives centrally (`IdentityEnvVar` table, edited via
  `/api/v0/grouping/identity-env-vars` and the **Unidentified** UI view) and is handed back to
  agents in the ingest push **response** (`IngestResponse`) — the one central→agent channel.
- **Security:** the agent surfaces only env var **key names** (in `StatsReport.Inventory`, held
  live in `ContainerInventoryHolder`), never values. Only the single chosen identity value ever
  reaches the central, as the stack name. Container secrets never leave the node. Secret-looking
  keys (names matching `SensitiveEnvKeys.Markers` such as `pass`, `secret`, `token`, `key`) are dropped
  from the inventory, refused as an identity by the resolver, and rejected by `GroupingController`,
  so a secret's value can never be sent as a stack name.
- A container with no matching identity var stays in the inventory (`GET /api/v0/inventory`)
  awaiting a choice; there is no image fallback (images collide across customers).

## Backups

Every volume is backed up nightly with **restic** to one off-site S3 bucket, unless a label or Track
excludes it. Track decides which volumes, when and where, and dumps the databases it knows; restic does
chunking, dedupe, encryption and upload. Backup data goes node → S3 directly, never
through the central.

- **Agent approval.** Each agent keeps a random token at `Track:AgentTokenPath` (`/app/data/agent-token`;
  mount a volume there) and sends it as `X-Track-Agent-Token` with the ingest key.
  `AgentEnrollmentRegistry` (central, `AgentEnrollment` table) ties a node name to one token. Metrics
  are accepted from any agent, but only an **approved** token gets jobs, and only approved agents'
  volume, mount and swarm reports feed the catalog, because those decide which commands run where.
  Approve, revoke or accept a changed token on the Agents page (`POST /agents/{name}/approve|revoke`).
- **What exists where.** Stats pushes carry the node's `NodeRole` and every running container's volume
  mounts with its `backupbot.*` labels (`VolumeMount`). Disk pushes give volume sizes. Manager agents
  push a `SwarmReport` (`/api/v0/ingest/swarm`): services, running tasks per node, mounts, placement
  constraints and service labels, which only a manager's engine can list. `BackupCatalog.Build` (pure,
  tested) turns that into one entry per copy that matters: `Single` (the copy a running container
  uses), `PerNode` (a global service, or mounted on several nodes: key `volume@node`), `StaleCopy`
  (leftover on a node that doesn't use it, not backed up) and `Ambiguous`. Anonymous volumes (a
  redeploy replaces them, so there's nothing to restore into), restore volumes and the restic cache
  volume are skipped. `BackupCatalog.AnonymousDatabases` warns when a database keeps its data in one.
- **Ignoring.** A volume is ignored (`ExclusionSource`) by a Track override, a label, or by Docker measuring
  it at 0 bytes (`Empty`), which lifts on its own once it holds data; an unknown size never counts as
  empty. Ignored volumes sit on the page's Ignored tab. `PUT /backups/volumes/override` takes several
  volume names for bulk ignore or include.
- **Labels** use backup-bot-two's names (`BackupLabels`): `backupbot.backup: "false"`,
  `backupbot.backup.volumes.<name>: "false"` and `.path`, `backupbot.backup.pre-hook|post-hook`,
  `backupbot.restore.pre-hook|post-hook`. `<name>` may be the full volume name or the compose key.
  `BackupVolumeOverride` rows (UI) win over labels: `Excluded` and `Method`, null meaning "not overridden".
- **Database dumps** (`DatabaseDumps`). A volume mounted at a known data path by an official
  `postgres`/`postgis`/`timescaledb`, `mariadb` or `mysql`/`percona` image is dumped instead of copied:
  Track's choice (`Method`), then a label pre-hook (files), then the detected engine. The commands are
  fixed per engine and name credentials by variable (`POSTGRES_USER`, `MARIADB_ROOT_PASSWORD`, the `_FILE`
  variants), so values never leave the container. The agent streams `pg_dumpall --clean --if-exists` or
  `mariadb-dump --databases <non-system> --add-drop-database` through `docker exec` into the helper's
  stdin; restic runs `--stdin-from-command` reading `/proc/1/fd/0` and saves the snapshot only if the agent
  touched a marker after the dump exited 0, so a failed dump or a dead agent leaves no partial snapshot.
  The snapshot holds `/data/<volume>/database.sql`, tagged `dump=<engine>` (`BackupSnapshot.DumpEngine`).
  Databases Track can't dump (Bitnami, Mongo) or set to copy files without a hook are flagged.
- **Test restore** (`Verify` job, agent): pulls the snapshot's image if needed, starts it with a random
  password, no network and a 1 GB limit (labelled `digital.chattanooga.track.backup.scratch`), waits for
  TCP readiness (the entrypoint's setup server is socket-only), streams `restic dump` into the import
  command, counts databases and tables, and removes it. The result is `BackupJob.Summary`; the latest per
  volume is shown on the page.
- **Repositories.** One restic repo per owner under the target prefix: `customer-{id}`, `shared`,
  `unassigned` (`BackupRepository`). Passwords are never stored: `RepoPasswordDeriver` is
  HMAC-SHA256(master key, repo key), base64, matching
  `printf %s "$REPO" | openssl dgst -sha256 -hmac "$MASTER_KEY" -binary | base64`. The master key comes
  from `Track:Backups:MasterKey` or the Swarm secret file `Track:Backups:MasterKeyFile`
  (`/run/secrets/track_backup_master_key`). `BackupTarget` holds the bucket and two keys (agent key for
  writing, maintenance key for prune), encrypted with Data Protection; it is not an `S3Source`.
- **Jobs** (`BackupJob`). Settings start paused. `BackupScheduler` (every 30s) queues each protected volume once its daily
  slot passes (`BackupSettings.BackupMinuteUtc` plus a stable per-volume offset inside `SpreadMinutes`),
  dispatches within `MaxJobsPerNode`/`MaxJobsTotal`, initialises repos with `restic init`, requeues jobs
  an agent never fetched and fails ones whose agent went quiet. The push response lists an approved
  node's job IDs; the agent fetches each from `/api/v0/agent/jobs/{id}` (the repo password is derived
  there), posts progress (which answers with a cancel flag) and a result.
- **Agent execution** (`BackupJobRunner`). Hooks run with `docker exec` (`sh -c`) in the service's
  running container (`HookRunner`); a failed pre-hook uploads nothing, post-hooks always run. restic
  runs in a helper container from the agent's own image (restic is bundled at `/usr/local/bin/restic`)
  as root, with the volume at `/data/<volume>` (read-only for backups) and a node cache volume,
  labelled `digital.chattanooga.track.backup.job`. A restarted agent adopts its helper instead of
  starting another. The restic host is the catalog key, so parent snapshots survive a node move.
- **Restore.** New volume (`<volume>-restore-<yyyyMMdd-HHmmss>`), download (`restic dump` streamed by the
  central), or **replace**: a `Replace` parent job walks child steps (safety snapshot without hooks or
  path filters, restore pre-hook, scale services to 0 via a manager agent, `restic restore --delete`,
  scale back, restore post-hook). Non-swarm containers are stopped and started by the node's agent.
  `BackupCatalogService.ReplaceBlockedReason` refuses unless the service's placement resolves to the
  volume's node alone. Safety snapshots are tagged `track-pre-restore` so they never take a day's
  retention slot; regular ones are tagged `track`. A dump snapshot instead replaces by **import**: a
  safety dump, the rest of its stack scaled to 0 (Swarm) or stopped by the agent (otherwise), then an
  `Import` job that streams the dump into the running database, then the stack back
  (`DumpReplaceBlockedReason`). Postgres connections are terminated first; the import's only expected
  errors are the dump recreating the connected superuser (`IsHarmlessImportError`).
- **Browse.** `GET /backups/snapshots/{id}/files?path=` runs `restic ls` on the central for one folder;
  `/download?path=&directory=` streams one file, or a folder as tar. Paths are relative to the volume
  and may not contain `.` or `..`.
- **Maintenance** (`BackupMaintenance`, central, every minute when due): weekly `forget` per
  `KeepDaily/Weekly/Monthly` grouped by host, pre-restore snapshots kept 30 days, `prune`, repo size
  from `restic stats` (billed per customer as backup storage), snapshot index resync, and a monthly
  `check --read-data-subset`. A failed run backs off for an hour (a cancelled backup's lock goes stale
  after 30 minutes). The bucket should have versioning and default object-lock retention so a stolen
  agent key can't destroy history; the target test checks that.
- **Live job logs** use RoushTech.Asio. Each `BackupJob` has a `LogSessionId`; `JobLogs` (central) opens the
  session and runs the scheduler's work for a job inside it, so `ILogger` lines (repository setup, restic's
  stderr as `ResticRunner` reads it, dispatch) land there. Agents activate the session the job carries and
  every `OutputLog` line plus progress every 10s is logged; Asio forwarding sends them through
  `CentralLogTransport` to `POST /api/v0/agent/jobs/log`, which only accepts a session belonging to a job on
  the calling node. Browsers watch `/hubs/job-log` (in memory, 2000 lines per job, 300 jobs). Only `Track.*`
  categories at Information and above reach a session. Requests never wait on the scheduler: they call
  `BackupRuntimeState.WakeScheduler()`, and repository setup gives up after 2 minutes.
- The Backups page reloads on the hub's `backups` event (`snapshotStore.backupsVersion`).

## Email

Central only. The SMTP server is one `MailSettings` row (none means mail is off), edited by administrators on the
**Settings** page (`EmailSettingsController`, `/api/v0/settings/email`), along with the Site URL emailed links
point at. The password is encrypted with Data
Protection (`MailSecretProtector`) and never returned. `POST /settings/email/test` sends the `Test` email using the
form's unsaved values, falling back to the stored password when the field is blank.

- **Templates** follow Procyon: each `EmailKind` member has an embedded `Track/Templates/Email/<Member>.liquid` with
  the subject in front matter and `{% layout '_layout.liquid' %}` in the body. `EmailRenderer` (Fluid) HTML-encodes
  every output, so templates must not add `| escape`, then PreMailer inlines the CSS. The model goes through JSON, so
  templates use camelCase names. A new email is an enum member plus its template; a test renders every kind.
- **Sending** goes through `EmailQueue.QueueAsync`, which renders at once and stores an `OutboundEmail`, or stores
  nothing and returns false while mail is off, so enabling it later doesn't send a backlog of stale alerts.
  `EmailDeliveryHostedService` sends due mail every minute or when woken, retries after 1m, 5m, 30m and 2h, then marks
  it Failed. A failure ends the pass so one dead server costs one timeout. Rows are deleted after 30 days.

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

Give the agent a volume at `/app/data` so its token survives redeploys; otherwise every new task is
a new, unapproved agent.

The agent reads its own socket, so it still needs the docker-socket group. In the stack
that is `user: "5678:<GID>"` where `<GID>` is `stat -c '%g' /var/run/docker.sock` on the
node. An agent that stops pushing is marked offline by the central after
`Track:NodeOfflineAfterSeconds`, and its stale samples are dropped rather than shown as
current.

A host outside the swarm runs the agent as a plain `docker run` container with
`Track__Central__Url` set to the central's public URL. Such an agent updates itself:
the ingest response carries the central's build version (`IngestResponse.Version`,
e.g. `0.9.1.23`, which is also the image tag), and when it differs `AgentSelfUpdater`
pulls that tag of its own image repository, starts a copy of its container under the
original name, waits for it to stay up, then removes itself (renaming back if the copy
fails). Agents labelled by Swarm or Compose skip this, as do dev builds without a
four-part version. `Track__AgentAutoUpdate=false` turns it off. It does not work with
`--network host`: the agent still listens on 5020, so the copy can't bind while the
original runs, and the update rolls back. Every push reports the agent's version
(`StatsReport.AgentVersion`), shown on the Agents page against `SnapshotModel.CentralVersion`.

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
add a one-line entry under the current version, newest version at the top, dated today,
categorized `#### Features`, `#### Changes`, or `#### Bug Fixes`. Keep entries short and
user-facing; no em dashes.

Version bumps are rare. Only a significant app improvement gets a minor bump; small features,
tweaks and fixes go under the current version with no bump. Every push to `master` deploys
anyway (the build number is the fourth version part), so a bump is not needed to ship.

## UI Copy

Write like a normal app: short labels, short empty states ("No customers yet."), tags such as
Billed / Not billed instead of sentences. No explanatory paragraphs or hints that restate the
label. No browser dialogs (`confirm`/`alert`/`prompt`); use `ConfirmDialog` or a `v-dialog`.

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
- Don't trust volume, mount, swarm or label data from an agent that isn't approved: hook commands
  come from labels and run via `docker exec`.
- Don't store repository passwords; derive them from the master key.
```
