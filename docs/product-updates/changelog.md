# Changelog

Welcome to the Track changelog. Here you'll find a history of product updates,
improvements, bug fixes, and new features by version.

## Releases
### 0.3.0 - 2026-07-11
#### Features
 - Group label-less standalone containers (plain docker run, e.g. Discourse) by an environment variable's value, which becomes a stack you can assign to a customer
 - New Unidentified view lists standalone containers with their available environment variable names, so you can pick a grouping identity from the app
 - Identity environment variables are configured centrally and pushed to agents automatically; only the chosen variable's value is sent, container secrets never leave the node
 - Secret-looking environment variables (names containing password, secret, token, and similar) are hidden and cannot be chosen as a grouping identity

### 0.2.0 - 2026-07-11
#### Features
 - Agent/central architecture: run a lightweight agent on every Swarm node (mode: global) that reads its local Docker socket and pushes metrics to a central instance, covering a whole cluster without exposing any node's Docker API to the network
 - Central instance is a pure aggregator, authenticated agent pushes over a shared secret, no Docker socket required on the central node
 - Agents identify by a stable node name (the Docker engine hostname) so a restarted agent does not appear as a new node
 - Example Swarm stacks for the central and agent roles under `deploy/`

#### Changes
 - One image, two roles selected by `Track:Mode` (Central or Agent); the central marks a node offline when it stops pushing (`Track:NodeOfflineAfterSeconds`)
 - Removed CORS configuration; the SPA and API are same-origin in every deployment
 - Login screen and dashboard now render correctly (Vuetify components are registered)

### 0.1.0 - 2026-07-10
#### Features
 - Polls Docker stats on a regular interval for per-container CPU, memory, and disk usage
 - Groups containers into stacks by Docker Swarm or Compose labels, and stacks into customers
 - Live dashboard over SignalR with per-customer and per-stack usage totals
 - Historical CPU, memory, and disk charts per stack, backed by a local SQLite time series
 - Admin login gating all configuration and metrics, built on ASP.NET Core Identity with a seeded admin account
 - Billing view showing blended cluster utilization (CPU and memory share) per customer
 - Multi-node support: configure each Swarm node's engine endpoint, unreachable nodes reported as offline
