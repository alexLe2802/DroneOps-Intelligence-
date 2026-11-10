BEGIN;
ALTER TABLE "TelemetryRecord" ADD COLUMN IF NOT EXISTS "AltitudeMslMeters" numeric NULL;
ALTER TABLE "Incident" ADD COLUMN IF NOT EXISTS "RecoveredAt" timestamptz NULL;
-- Historical measured recovery can be identified from its append-only transition.
UPDATE "Incident" i SET "RecoveredAt" = r.at
FROM (SELECT "IncidentId", max("CreatedAt") AS at FROM "IncidentTransition"
      WHERE "EventType" IN ('AutoResolved', 'Recovered') GROUP BY "IncidentId") r
WHERE i."Id" = r."IncidentId" AND i."RecoveredAt" IS NULL;
CREATE INDEX IF NOT EXISTS "IX_Incident_ActiveEpisode"
ON "Incident" ("MissionId", "Type") WHERE "RecoveredAt" IS NULL;
COMMIT;
