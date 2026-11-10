BEGIN;
ALTER TABLE "TelemetryRecord" ADD COLUMN IF NOT EXISTS "BatteryObservedAt" timestamptz;
ALTER TABLE "TelemetryRecord" ADD COLUMN IF NOT EXISTS "AltitudeObservedAt" timestamptz;
ALTER TABLE "TelemetryRecord" ADD COLUMN IF NOT EXISTS "SpeedObservedAt" timestamptz;
ALTER TABLE "Incident" ADD COLUMN IF NOT EXISTS "Revision" integer NOT NULL DEFAULT 0;
ALTER TABLE "TelemetryRecord" ADD COLUMN IF NOT EXISTS "SourceBootId" varchar(100);
ALTER TABLE "TelemetryRecord" ADD COLUMN IF NOT EXISTS "SourceEventId" varchar(128);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_TelemetryRecord_SourceEvent"
 ON "TelemetryRecord" ("SourceId", "SourceBootId", "SourceEventId")
 WHERE "SourceBootId" IS NOT NULL AND "SourceEventId" IS NOT NULL;
CREATE TABLE IF NOT EXISTS "MissionFlightExecution" (
 "MissionId" uuid PRIMARY KEY REFERENCES "Mission"("Id"), "UavId" uuid NOT NULL REFERENCES "UAV"("Id"),
 "ConnectionId" uuid NOT NULL REFERENCES "DroneConnection"("Id"), "PlanHash" text NOT NULL, "CompiledHash" text NOT NULL,
 "ApprovedPlanJson" text NOT NULL, "WaypointCommandsJson" text NOT NULL, "ReachedCommandsJson" text NOT NULL,
 "StartedAt" timestamptz NOT NULL, "CompletedAt" timestamptz, "Outcome" text NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_MissionFlightExecution_ActiveUav"
 ON "MissionFlightExecution" ("UavId") WHERE "CompletedAt" IS NULL;
CREATE TABLE IF NOT EXISTS "MonitoringRuleRevision" (
 "Id" uuid PRIMARY KEY, "SettingsJson" text NOT NULL, "ChangedBy" uuid NOT NULL REFERENCES "User"("Id"), "CreatedAt" timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS "IX_MonitoringRuleRevision_CreatedAt" ON "MonitoringRuleRevision" ("CreatedAt" DESC);
CREATE OR REPLACE FUNCTION public.protect_mission_execution_snapshot() RETURNS trigger
SET search_path = pg_catalog, public AS $$
BEGIN
 IF (NEW."MissionId",NEW."UavId",NEW."ConnectionId",NEW."PlanHash",NEW."CompiledHash",NEW."ApprovedPlanJson",NEW."WaypointCommandsJson",NEW."StartedAt")
 IS DISTINCT FROM (OLD."MissionId",OLD."UavId",OLD."ConnectionId",OLD."PlanHash",OLD."CompiledHash",OLD."ApprovedPlanJson",OLD."WaypointCommandsJson",OLD."StartedAt")
 THEN RAISE EXCEPTION 'Execution snapshot is immutable'; END IF;
 IF OLD."CompletedAt" IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'Terminal execution is immutable'; END IF;
 RETURN NEW;
END; $$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS "TR_MissionFlightExecution_Immutable" ON "MissionFlightExecution";
CREATE TRIGGER "TR_MissionFlightExecution_Immutable" BEFORE UPDATE ON "MissionFlightExecution"
 FOR EACH ROW EXECUTE FUNCTION protect_mission_execution_snapshot();
CREATE OR REPLACE FUNCTION public.protect_rule_revision() RETURNS trigger
SET search_path = pg_catalog, public AS $$
BEGIN RAISE EXCEPTION 'Monitoring rule revisions are append-only'; END; $$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS "TR_MonitoringRuleRevision_Immutable" ON "MonitoringRuleRevision";
CREATE TRIGGER "TR_MonitoringRuleRevision_Immutable" BEFORE UPDATE OR DELETE ON "MonitoringRuleRevision"
 FOR EACH ROW EXECUTE FUNCTION protect_rule_revision();
-- API database role owns authorization. Do not expose execution snapshots or rule audit via the public Data API.
ALTER TABLE public."MissionFlightExecution" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."MonitoringRuleRevision" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public."MissionFlightExecution", public."MonitoringRuleRevision" FROM anon, authenticated;
COMMIT;
