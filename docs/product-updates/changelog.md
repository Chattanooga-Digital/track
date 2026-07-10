# Changelog

Welcome to the Track changelog. Here you'll find a history of product updates,
improvements, bug fixes, and new features by version.

## Releases
### 0.1.0 - 2026-07-10
#### Features
 - Polls Docker stats on a regular interval for per-container CPU, memory, and disk usage
 - Groups containers into stacks by Docker Swarm or Compose labels, and stacks into customers
 - Live dashboard over SignalR with per-customer and per-stack usage totals
 - Historical CPU, memory, and disk charts per stack, backed by a local SQLite time series
 - Admin login gating all configuration and metrics, built on ASP.NET Core Identity with a seeded admin account
 - Billing view showing blended cluster utilization (CPU and memory share) per customer
 - Multi-node support: configure each Swarm node's engine endpoint, unreachable nodes reported as offline
