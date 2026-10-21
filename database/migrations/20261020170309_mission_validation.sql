BEGIN;
CREATE TABLE IF NOT EXISTS public."MissionValidationRun" (
 "Id" uuid PRIMARY KEY,
 "MissionId" uuid NULL,
 "ActorId" uuid NOT NULL,
 "Stage" text NOT NULL,
 "IsValid" boolean NOT NULL,
 "CheckedAt" timestamptz NOT NULL,
 "PlanHash" text NOT NULL,
 "PlanJson" jsonb NOT NULL,
 "ResultJson" jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_MissionValidationRun_Latest" ON public."MissionValidationRun" ("MissionId", "CheckedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_MissionValidationRun_Preflight" ON public."MissionValidationRun" ("MissionId", "Stage", "CheckedAt" DESC);
ALTER TABLE public."MissionValidationRun" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public."MissionValidationRun" FROM anon, authenticated;
ALTER TABLE public."MissionApproval" ADD COLUMN IF NOT EXISTS "ValidationRunId" uuid NULL;
COMMIT;
