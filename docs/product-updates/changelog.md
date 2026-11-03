# Changelog

Welcome to the Track changelog. Here you'll find a history of product updates,
improvements, bug fixes, and new features by version.

## Releases
### 0.9.1 - 2026-11-03
#### Changes
 - Deleting a user, customer or S3 source asks for confirmation in the app instead of a browser popup, and shows any error there
 - Shorter wording across the app, with Billed and Not billed tags on each group

### 0.9.0 - 2026-11-03
#### Features
 - S3 buckets can be marked as shared infrastructure too, such as backups, so they are reported but not billed
 - Change who a stack or bucket belongs to from the Stacks page or the new bucket list on the Storage page, not just from the customer

### 0.8.0 - 2026-11-03
#### Features
 - Mark stacks such as the proxy and monitoring as shared infrastructure from the Customers page. They get their own group on the dashboard and billing, with their share reported but never billed, so Unassigned only holds stacks that still need a home

### 0.7.1 - 2026-11-03
#### Changes
 - The version now shows the commit the running build came from, such as v0.7.1+5e13aef
 - Navigation moved to a side menu, and every page keeps a readable width instead of stretching across wide screens
 - The dashboard lines each stack's CPU, memory, disk and cluster share up in columns, with its containers nested underneath, and lists the heaviest stacks first
 - The billing table shows usage under each charge, and the period picker switches between live and recent months in one click

### 0.7.0 - 2026-07-18
#### Features
 - Billing now shows dollars, not just percentages. Set your monthly cluster cost and per-GB storage rates from the new Rates button on the Billing page
 - Compute is billed as a utility with full cost recovery: your whole cluster cost is split across paying customers by blended usage, so idle capacity and unassigned overhead are spread across customers instead of absorbed. Even a lightly used cluster recovers 100% of its cost
 - Disk and object storage bill at a flat per-GB rate as separate line items; set the disk rate to 0 to treat local disk as bundled into the compute cost
 - Both the live view and each recorded month show per-customer compute, disk, object storage, and total charges, priced at your current rates

### 0.6.1 - 2026-07-18
#### Bug Fixes
 - Agents no longer go permanently offline after a timeout: a slow or hung central (or Docker daemon) previously killed the agent's stats push loop for good, so nodes showed "unreachable: No report received" with zero CPU and memory until the agent was restarted. The push loops now treat a timeout as a transient failure and retry on the next tick
 - The central push now times out after 30 seconds instead of stalling a poll tick for up to 100 seconds
 - The live dashboard reconnects indefinitely after a longer central outage, instead of giving up after ~30 seconds and staying stuck on "Reconnecting" until a page reload

### 0.6.0 - 2026-07-12
#### Features
 - Month-by-month billing: pick a billing period on the Billing page to see each customer's average blended utilization, memory, disk, and object storage for that month, alongside the live view
 - The central records a billing sample every 5 minutes (configurable via `Track:BillingSampleIntervalSeconds`), freezing cluster capacity and customer assignments as they were at the time
 - Billing history is kept for 2 years by default (`Track:BillingRetentionDays`), independent of the raw sample retention

### 0.5.1 - 2026-07-12
#### Bug Fixes
 - Saving an S3 source now polls it right away, so its buckets are assignable to customers immediately instead of after the next poll cycle (up to 15 minutes)

#### Features
 - Added a "Refresh usage" button on the Storage page to poll every enabled S3 source on demand
 - New Users page (administrators only) to create, edit, disable, and delete accounts, grant or revoke administrator access, and set another user's password
 - Change your own password from the account menu
 - Disabled accounts cannot sign in, and disabling, demoting, or resetting a password signs that user out of existing sessions
 - Guards keep the app reachable: you cannot disable, demote, or delete your own account, and the last active administrator cannot be removed

### 0.5.0 - 2026-07-11
#### Features
 - Manage S3 sources live from the new Storage page: add, edit, disable, delete, and test a connection without a restart
 - MinIO and RustFS sources are sized in one call via the admin data-usage API, reporting each bucket's size and object count without listing every object
 - Bucket object counts are shown on the dashboard
 - Secret keys are stored encrypted and never sent back to the browser

#### Bug Fixes
 - Fixed "SignatureDoesNotMatch" errors against MinIO and RustFS caused by the AWS SDK v4 default of adding checksum headers

#### Changes
 - S3 sources now live in the database and are managed from the UI; any Track:S3Sources config is imported once on first startup

### 0.4.0 - 2026-07-11
#### Features
 - Track S3 object storage: the central polls configured S3-compatible endpoints (MinIO, AWS) for per-bucket sizes
 - Assign buckets to customers from the Customers page, the same way you assign stacks
 - Object storage is shown as its own figure on the dashboard, kept separate from node-local disk, and rolled into customer and cluster totals
 - Configure endpoints via `Track:S3Sources` (name, endpoint, access key, secret key); polling runs on the slow `Track:S3PollIntervalSeconds` interval (default 15 minutes)

### 0.3.0 - 2026-07-11
#### Features
 - New Agents page listing every connected agent with its status, last-seen time, containers tracked, unidentified count, CPU cores, and live CPU and memory usage
 - Delete a dead agent from the Agents page to clear a decommissioned node from the roster
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
