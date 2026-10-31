BEGIN;

ALTER TABLE "PostFlightReport"
    ADD COLUMN IF NOT EXISTS "CoveredTelemetryFrom" timestamp with time zone NULL;

UPDATE "PostFlightReport" report
SET "CoveredTelemetryFrom" = mission."StartTime" AT TIME ZONE 'UTC'
FROM "Mission" mission
WHERE report."MissionId" = mission."Id"
  AND report."CoveredTelemetryFrom" IS NULL
  AND mission."StartTime" IS NOT NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM "FlightCompletionEvent"
        WHERE "IsTrusted" = TRUE
        GROUP BY "MissionId" HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Multiple trusted completion events exist for one mission; reconcile before applying the post-flight execution snapshot migration.';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_FlightCompletionEvent_Mission_Trusted"
    ON "FlightCompletionEvent" ("MissionId") WHERE "IsTrusted" = TRUE;

COMMIT;
