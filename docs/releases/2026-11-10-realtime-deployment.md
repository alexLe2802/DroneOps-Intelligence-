# Realtime release — 10/11/2026

## Database backup and migration

- Supabase project: `diaswbzjhvzwxcadlias` (DroneOps-Intelligence-).
- Backup: `droneops-20261110T102510Z`; PostgreSQL custom dump and private media.
- Backup completed (UTC): `2026-11-10T10:26:01.893334Z`.
- Verified: 13 file checksums, readable pg_restore archive, 10 private-media objects.
- Dump/media remain in the ignored local `artifacts/backups/` directory; never committed to Git.
- Applied Supabase migration: `20261110102712_droneops_realtime_execution`.
- Repository SQL: `database/migrations/20261110000200_realtime_execution.sql`.
- Existing telemetry-episode migration was already applied: `20261110095804_droneops_realtime_episodes`.
- Post-migration verification: 26,367 telemetry records and 48 incidents retained; six required columns present, both new tables have RLS enabled, immutable-evidence triggers present.
- New execution/configuration tables deny anon/authenticated Data API access; the API database role enforces user authorization.
- Security advisor retains one pre-existing mutable-search-path warning for `prevent_incident_transition_mutation`; the two newly introduced trigger functions have explicit search paths.

## Deployment status

- Before deployment, backend health returned commit `f584dc91f1e12d9c83d425b4ac088dd641467f8c`, `role=api`, `monitoringEnabled=true` on Render.
- Backend deployment requires access to the existing Render service; the Render app is not connected in this session.
- No aircraft commands or KAN-51 acceptance flight were performed.
- Report5 remains unchanged. Application test execution is deferred under the owner's earlier instruction; release build/type validation and deployment/schema checks are separate.
