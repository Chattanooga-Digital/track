# Track

Docker Swarm monitoring for Chattanooga Digital. Polls the Docker engine for
per-container **CPU, memory, and disk** usage, groups containers into **stacks**, and
groups stacks into **customers**. Live dashboard over SignalR, history from a local
SQLite time series.

## Quick start (Docker)

```bash
DOCKER_GID=$(stat -c '%g' /var/run/docker.sock) docker compose up --build
# open http://localhost:5020
```

## Quick start (local dev)

```bash
# terminal 1 — backend on :5020
dotnet run --project Track.Website

# terminal 2 — frontend on :5173 (proxies to the backend)
cd Track.Vue && npm install && npm run dev
```

## What it does

1. **Monitors docker stats** on a regular interval (default 15s) for CPU, RAM, and disk
   (writable-layer + volume) usage of every container.
2. **Groups into stacks** using the `com.docker.stack.namespace` (Swarm) or
   `com.docker.compose.project` (Compose) labels.
3. **Groups stacks into customers** — a mapping you manage on the Customers page.

## Layout

| Project | Purpose |
|---|---|
| `Track` | Entities, `TrackContext`, Docker collectors, options |
| `Track.Website` | API, hosted polling services, SignalR hub |
| `Track.Tests` | NUnit tests |
| `Track.Vue` | Vue 3 dashboard (builds into `Track.Website/wwwroot`) |

## Multi-node

Configure `Track:Nodes` in `Track.Website/appsettings.json` with a name + engine
endpoint per node (unix socket or `tcp://host:2375`). With none configured it uses the
local socket. See [CLAUDE.md](CLAUDE.md) for the full architecture and conventions.

## CI / deploy

[`.github/workflows/docker-publish.yml`](.github/workflows/docker-publish.yml) builds the
image and pushes it to GitHub Container Registry as `ghcr.io/chattanooga-digital/track`,
tagged `<VersionPrefix>.<run number>`, on every push to the default branch (also `latest`)
and on `v*.*.*` git tags (semver tags). Pull requests build only.

Each push to `master` then creates a Polecat release of that version, which deploys the
`track` stack to production. The deploy definition is the `track` folder of
`roushtech/deployments`.

Required repository secret (Settings → Secrets and variables → Actions):

| Secret | Value |
|---|---|
| `POLECAT_TOKEN` | a Polecat personal access token |

The image builds `linux/amd64`. To also publish arm64, add `platforms: linux/amd64,linux/arm64`
to the build step (slower, via QEMU).
