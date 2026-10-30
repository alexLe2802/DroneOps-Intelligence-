# KAN-78 recovery rehearsal evidence 2026-10-30

## Result

The local tool rehearsal completed successfully against a newly created database
named `droneops_recovery_kan78`, which was deleted immediately after verification.
No automatic production migration was executed.

- Database coverage matched: account/license metadata 61, mission/review 43,
  telemetry 983, incidents 36 and private-media references 17.
- Private media transport matched: 2 expected and 2 restored fixture objects.
- Measured RPO: 0.0096 hours, within the 24-hour objective.
- Measured RTO: 0.0099 hours, within the 4-hour objective.
- The deployment ZIP built successfully with 2,307 manifest entries and no
  `.env`, `appsettings.Local.json`, `session.private.json` or `*.private.*` file.
- The package explicitly records `automaticProductionMigration: false`.

## Defects found and corrected

The rehearsal found and corrected three restore defects: platform-owned Supabase
schemas were included in the application dump; `--clean` failed against an empty
target; and the dump attempted to recreate PostgreSQL's existing default `public`
schema. The corrected flow dumps only `public` and `droneops`, requires an empty
target, restores in one transaction and omits only the default schema creation TOC
entry while retaining its application objects.

## Scope and remaining production evidence

This was a tool-level rehearsal, not production recovery approval. The configured
Supabase Storage service key was rejected, so private-media transfer used separate
in-memory source and target Storage API fixtures. The database target was a separate
database but on the configured PostgreSQL service, not a separately network-isolated
project. A final staging rehearsal must use a distinct PostgreSQL/Storage project,
valid private media, recorded operator/reviewer identity and infrastructure evidence
before KAN-78 is marked Done.
