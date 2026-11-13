BEGIN;
ALTER TABLE "MissionUpdateRequest"
    ADD COLUMN IF NOT EXISTS "PilotConfirmedBy" uuid,
    ADD COLUMN IF NOT EXISTS "PilotConfirmedAt" timestamptz,
    ADD COLUMN IF NOT EXISTS "PilotConfirmedValidationRunId" uuid;
-- No historical confirmation or mission status is inferred from legacy data.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_MissionUpdateRequest_Pending"
    ON "MissionUpdateRequest" ("MissionId") WHERE "Status" = 'Pending';
COMMIT;
