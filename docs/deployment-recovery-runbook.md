# KAN-78 — Deployment, backup and isolated recovery runbook

This is the production operator runbook for DroneOps. Commands are examples; an
operator must substitute the named target and record it in the change/evidence
record. The application does **not** migrate a production database at startup.

## Recovery objectives and protected data

- Daily backup, maximum RPO 24 hours; isolated restore rehearsal RTO 4 hours.
- The PostgreSQL custom dump includes the DroneOps-owned `public` and `droneops`
  schemas: accounts, pilot/license metadata,
  missions/approvals/update requests, telemetry, incidents/transitions/alerts,
  gateway bindings and the remaining relational history. Supabase platform schemas
  (`auth`, `storage`, `realtime`, and extensions) are platform-managed and are not
  replayed; private storage objects are protected separately as described below.
- The same backup set includes every object in the private Supabase Storage bucket
  `user-media`: license evidence, avatars and UAV images. A database-only dump is
  not a complete backup.
- Backup output must be written to encrypted, access-controlled storage with
  versioning/object lock. Replicate it to a different failure domain. Never commit
  a dump, media object, service key, evidence containing secrets, or private IDs.

## Required environment variables

Backup host (dedicated least-privilege operator account):

```text
DRONEOPS_BACKUP_DATABASE_URL=postgresql://...?...sslmode=require
DRONEOPS_BACKUP_STORAGE_URL=https://SOURCE_PROJECT.supabase.co
DRONEOPS_BACKUP_STORAGE_SERVICE_KEY=<server secret/service_role>
```

Restore rehearsal host (a different database and Storage project):

```text
DRONEOPS_RESTORE_ISOLATED=YES
DRONEOPS_RESTORE_DATABASE_URL=postgresql://.../droneops_recovery?...sslmode=require
DRONEOPS_RESTORE_STORAGE_URL=https://ISOLATED_PROJECT.supabase.co
DRONEOPS_RESTORE_STORAGE_SERVICE_KEY=<isolated project server key>
```

Secrets belong in the platform secret manager and process environment, never a
command line, package, log, `.env` file in the repository, or evidence artifact.
The tools pass PostgreSQL credentials through `PG*` child-process variables.

## Daily backup

The host needs Python 3.11+, PostgreSQL client tools matching or newer than the
server (`pg_dump`, `psql`), outbound TLS to PostgreSQL and Supabase, sufficient
temporary disk, and synchronized UTC time.

```powershell
python scripts/recovery/backup.py `
  --target-environment production `
  --output-root E:\EncryptedBackups\DroneOps
```

Linux cron example (secret variables are injected by the service manager, not
placed in crontab): `15 18 * * *` runs at 01:15 Asia/Bangkok the next day. Windows
Task Scheduler should likewise run daily under the backup service account. Alert
on non-zero exit, missing `manifest.json`, checksum failure, or latest successful
`completedAt` older than 24 hours. Retention/deletion is controlled by the backup
vault policy, not this tool; use at least 35 daily and 12 monthly immutable sets.

The deployment package includes hardened systemd templates in `deploy/systemd`.
Install them under `/etc/systemd/system`, create `/etc/droneops/backup.env` owned
by root with mode `0600`, then run `systemctl daemon-reload` and
`systemctl enable --now droneops-backup.timer`. Confirm with
`systemctl list-timers droneops-backup.timer`, trigger one manual service run and
connect service failure/backup-age alerts to the operations channel.

The backup directory appears atomically only after DB dump, coverage query, all
private objects, indexes and SHA-256 manifest complete. A `.tmp-*` directory is a
failed/incomplete set and must never be selected for restore.

## Isolated restore rehearsal

1. Create a disposable network/project with no production DNS, queues, SMTP,
   webhook, AI, Firebase or gateway credentials. Block outbound side effects.
2. Create an empty PostgreSQL database and a distinct private `user-media` bucket.
   Verify the URLs/account/project IDs are not production.
3. Select the newest completed backup and verify its vault/object-lock audit.
4. Run the restore. The explicit target and confirmation are written to evidence:

```powershell
python scripts/recovery/restore.py `
  --backup E:\EncryptedBackups\DroneOps\droneops-YYYYMMDDTHHMMSSZ `
  --target-environment recovery `
  --confirm RESTORE-RECOVERY `
  --evidence artifacts\recovery-rehearsal-YYYYMMDD.json
```

The tool rejects production/live target names, requires the isolation sentinel,
rejects the source Storage URL, verifies every backup checksum, requires a newly
created empty target database, restores it in one `pg_restore --single-transaction`, restores private objects, compares coverage
counts and records RPO/RTO. Partial media uploaded by a failed run is deleted. The
disposable target database/project should be destroyed after evidence review.

Evidence is PASS only when RPO is 0–24 hours, RTO is at most 4 hours, every coverage
group matches, and all private media objects restore. Attach sanitized tool output,
manifest SHA-256, package SHA-256, UTC start/end, target environment/project names,
operator/reviewer and smoke-test result. Never call a development restore a
production recovery rehearsal without documenting its isolated target.

## Deployment package and preflight

Build from a reviewed commit on a clean CI worker:

```powershell
python scripts/recovery/package_deployment.py `
  --output artifacts\droneops-<commit>.zip `
  --version <commit> --install-frontend
```

The package contains Release API binaries, Next.js standalone output, gateway,
ordered SQL migrations, recovery tools, this runbook and SHA-256 per file. It does
not contain local settings, `.env`, private gateway sessions or service keys and
does not run migrations. Verify package hash/signature before promotion.

Preflight checklist:

1. Record target environment, current/new commit, package hash, approver, maintenance
   window and last successful backup age. Stop if backup is older than 24 hours.
2. Validate all required runtime env vars in the target secret manager: PostgreSQL,
   JWT issuer/audience/key, integration key, Supabase URL/service key, mail/AI only
   where enabled. Do not print values.
3. DNS: lower TTL before a planned cutover, validate A/AAAA/CNAME and retain the
   prior target for rollback. TLS: valid full chain, SAN hostname, renewal and
   modern protocols. Firewall: only 443 public; DB/Storage/admin paths restricted;
   gateway egress and reverse-proxy SSE buffering/timeouts validated.
4. Clock: API, DB, gateway and proxy use NTP; alert on material drift because JWT,
   telemetry latency, incident evidence and RPO depend on UTC time.
5. Capacity/disk, health endpoint, logs/metrics/alerts and private media access are
   ready. Keep production side effects disabled on staging/recovery.

## Manual migration order — never automatic in production

The authoritative order is `database/migration-order.txt`, included in the package.
Two migrations share prefix `005`; therefore alphabetical guessing is forbidden.
After a fresh backup and approval, an operator applies each pending SQL file with
`psql --set ON_ERROR_STOP=1 --single-transaction --file <file>` where supported,
records file/hash/start/end/result, and runs schema checks. API `--migrate-*` flags
are development/explicit maintenance commands only; never add them to production
startup, readiness probes or deployment hooks.

## Cutover and verification

Deploy API and web as new immutable instances, inject target secrets, keep traffic
off, then run schema/readiness checks. Shift traffic gradually. Verify login and
license metadata, mission/review read, telemetry ingest/latest/SSE, incident flow,
signed private-media read, gateway authentication, error rate and clock timestamps.
Evidence must state `targetEnvironment`, hostname/project (without secrets), commit,
package hash and migration list.

## Rollback

- Application-only failure with backward-compatible schema: shift DNS/load balancer
  to the previous immutable API/web package; do not reverse data migrations blindly.
- Migration failure before commit: `ON_ERROR_STOP`/transaction rolls back that file;
  stop and investigate. Never mark it applied manually.
- Committed incompatible migration or data corruption: stop writers, preserve logs,
  create a forensic snapshot, then restore the last verified DB **and matching private
  media set** into a new isolated target. Validate it before traffic cutover. RPO is
  measured from backup `completedAt`; RTO starts at incident recovery authorization.
- Restore prior DNS target/TTL, TLS certificate/route and firewall rules from the
  recorded change. Confirm NTP, health, SSE and signed media after rollback.

Rollback is a reviewed incident action. Never point a rehearsal restore at production
or automatically run down-migrations during application startup.
