# DroneOps deployment backup and recovery runbook

This runbook defines the controlled backup, deployment-package and isolated restore rehearsal for DroneOps. Production restore is deliberately unsupported by these tools. A restore rehearsal must use a new empty PostgreSQL database and a different private-storage project from the source.

## Recovery objectives

- Recovery point objective (RPO): at most 24 hours for the reference deployment.
- Recovery time objective (RTO): at most 4 hours for an isolated restore rehearsal.
- Backup scope: account and licence metadata, missions and review evidence, telemetry, incidents, and every object in the private `user-media` bucket.
- Evidence: retain `manifest.json`, `database-coverage.json`, `private-media-index.json`, and the restore evidence JSON produced by `restore.py`.

Daily execution is defined by `deploy/systemd/droneops-backup.timer`. Confirm that the timer is enabled and that its last run succeeded before deployment.

## Required tools and secrets

Use Python 3, PostgreSQL client tools compatible with the server (`pg_dump`, `pg_restore`, and `psql`), and a Supabase service-role key restricted to the intended project. Store secrets in `/etc/droneops/backup.env` or another protected secret store. Never place them in a deployment ZIP or evidence file.

Backup variables:

```text
DRONEOPS_BACKUP_DATABASE_URL
DRONEOPS_BACKUP_STORAGE_URL
DRONEOPS_BACKUP_STORAGE_SERVICE_KEY
```

Restore variables must identify different, isolated resources:

```text
DRONEOPS_RESTORE_DATABASE_URL
DRONEOPS_RESTORE_STORAGE_URL
DRONEOPS_RESTORE_STORAGE_SERVICE_KEY
DRONEOPS_RESTORE_ISOLATED=YES
```

## Create and verify a backup

From the repository root, run:

```powershell
py -3 scripts/recovery/backup.py --target-environment production --output-root D:\droneops-backups
```

The command creates a timestamped directory atomically. It must finish without `BACKUP FAILED`. Before accepting the backup:

1. Confirm `manifest.json` reports `formatVersion: 1`, the expected source target, and the expected private-media count.
2. Confirm `database-coverage.json` contains account/licence, mission/review, telemetry, incident, and private-media-reference counts.
3. Keep the complete directory together. Checksums cover the database dump, coverage file, media index, and each downloaded media object.
4. Copy the backup to access-controlled storage with retention longer than the 24-hour RPO window.

## Build a deployment package

The package is secret-free and never applies migrations automatically:

```powershell
py -3 scripts/recovery/package_deployment.py --output artifacts/droneops-release.zip --version <commit-or-release>
```

Record the printed SHA-256 value. Open the ZIP and confirm it contains `DEPLOYMENT-RECOVERY-RUNBOOK.md`, `recovery-tools`, `migrations/migration-order.txt`, API output, web output, and gateway files. Apply database migrations manually in the listed order only after a successful backup and approval.

## Isolated restore rehearsal

Create a new empty PostgreSQL database. Create or select a non-production storage project with an empty `user-media` bucket. Verify network routes and credentials cannot reach production. Set `DRONEOPS_RESTORE_ISOLATED=YES` only after completing that check.

Run the restore with the exact confirmation matching the target name:

```powershell
py -3 scripts/recovery/restore.py `
  --backup D:\droneops-backups\droneops-YYYYMMDDTHHMMSSZ `
  --target-environment recovery `
  --confirm RESTORE-RECOVERY `
  --evidence artifacts\kan78\restore-evidence.json
```

`restore.py` refuses production-like target names, a non-empty database, a storage URL equal to the source, a damaged manifest, or missing explicit isolation. Database restore uses one transaction. If media upload fails, uploaded rehearsal objects are removed where possible and the evidence remains `Failed`.

## Acceptance checks

A rehearsal is successful only when the evidence JSON reports all of the following:

- `status` is `Passed`;
- `databaseRestored` and `privateMediaRestored` are true;
- `coverageMatches` and `privateMediaMatches` are true;
- `rpoMet` is true and `rpoHours` is between 0 and 24;
- `rtoMet` is true and `rtoHours` is at most 4;
- `automaticProductionMigration` is false.

Also perform read-only application checks against the isolated target: sign in with a rehearsal account, open one mission and its review history, read telemetry and incident history, and load one restored private document. Do not connect a real UAV, send flight commands, or reuse production sessions.

## Failure and rollback

Do not mark the rehearsal successful when any coverage count differs, an object is missing, or an objective is exceeded. Preserve the evidence JSON and logs, destroy the failed isolated database and bucket, correct the cause, and repeat with a new empty target. A deployment failure does not authorize an automatic production restore; keep the service stopped or roll back the application package according to the approved deployment decision.

## Automated checks

Run the recovery regression suite before packaging:

```powershell
py -3 -m unittest discover -s scripts/recovery -p "test_*.py" -v
```

These checks cover isolation guards, secret handling, storage traversal, manifest tamper/path/size detection, migration ordering, storage round-trip behavior, and the required runbook input. They do not replace the isolated PostgreSQL and storage rehearsal described above.
