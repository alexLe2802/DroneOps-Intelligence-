# KAN-50 drone connection deployment acceptance

This checklist is environment evidence. Passing fixture or InMemory tests does not
replace the PostgreSQL, HTTPS, proxy and restart checks below.

## Change record and migration order

Record the target environment, UTC start/end time, deploy commit, operator and
database host without passwords. Before applying anything, save a database backup
and the SHA-256 hashes of `migration-order.txt`,
`20261021112520_drone_gateway_sessions.sql` and
`20261021115253_drone_hardware_identity.sql` in the change record.

Apply only on an explicitly authorized environment:

```powershell
dotnet run --project be/DroneOps/DroneOps.API --no-launch-profile -- `
  --migrate-drone-connections --environment Staging
dotnet run --project be/DroneOps/DroneOps.AuthChecks -c Release -- `
  --drone-connections (Resolve-Path .)
```

The API does not apply these migrations during normal startup. The session migration
must precede the hardware-identity migration. The second migration revokes legacy
active sessions that have no observed UID; it never invents an identity.

Rollback is code-first: stop traffic, restore the prior application package and keep
both gateway tables so pairing, revocation and permanent binding history remain
available. Do not drop columns or tables during an emergency rollback. If a database
restore is required, restore into an isolated target first, run AuthChecks there and
record the restored backup identifier and RTO/RPO evidence.

## PostgreSQL security evidence

Archive the complete successful AuthChecks output. It verifies PostgreSQL mappings,
RLS, direct grants, foreign keys, unique constraints, hashed secrets, owner scope,
revocation, reconnect, conflict rollback, concurrent pairing-code consumption,
concurrent UID claims, fixture cleanup and the absence of a shadow `UavId1` column.
Never include connection strings, pairing codes or gateway bearer tokens in evidence.

## Network and process topology

Record `API_BASE_URL`, public DNS names, reverse-proxy and API instance counts,
firewall source/destination rules, certificate issuer/expiry and clock-sync status.
Remote gateway traffic must use HTTPS for pairing/control and WSS for the Jetson
data plane. Browser requests must use the same-origin
server proxy/session; public browser configuration must not contain the backend
credential or gateway routes.

For gateway WebSocket, verify `Upgrade`/`Connection` forwarding, idle timeout and
reconnect without replaying queued old telemetry as live. For browser SSE, disable
proxy buffering and compression transformations, keep the upstream
read timeout longer than the expected monitoring session, and verify an authorized
stream survives proxy and API restarts by reconnecting to the latest valid snapshot.
Record response headers and timestamps, but redact authorization values.

The built-in pair and ingest limiters are process-local. A deployment with more than
one API instance must use a shared edge limiter and sticky-independent event delivery,
or remain failed for this criterion. Record the edge product/configuration and prove
that requests distributed across instances share the same limit. Do not claim the
in-process limiter or an in-process event mechanism is sufficient for a multi-instance
deployment.

## Acceptance decision

PASS requires all PostgreSQL checks plus staging evidence for HTTPS, browser proxy,
unbuffered SSE, timeout, restart/reconnect, backup/rollback and the declared instance
topology. Missing environment evidence is `In Review`/`To Do`, never inferred from
local functional tests and never reported as production approval.
