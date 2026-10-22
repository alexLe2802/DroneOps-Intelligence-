BEGIN;

ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "Revision" integer NOT NULL DEFAULT 1;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "IsCurrent" boolean NOT NULL DEFAULT TRUE;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "GenerationStatus" text NOT NULL DEFAULT 'Available';
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "CoveredTelemetryThrough" timestamp with time zone NULL;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "GenerationStartedAt" timestamp with time zone NULL;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "GeneratedAt" timestamp with time zone NULL;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "FailedAt" timestamp with time zone NULL;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "FailureReason" text NULL;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "RequestedBy" uuid NULL;
ALTER TABLE "PostFlightReport" ADD COLUMN IF NOT EXISTS "GenerationReason" text NULL;

UPDATE "PostFlightReport"
SET "GenerationStatus" = 'Available',
    "GeneratedAt" = COALESCE("GeneratedAt", "CreatedAt")
WHERE "GenerationStatus" IS NULL OR "GenerationStatus" = 'Available';

DROP INDEX IF EXISTS "UX_PostFlightReport_MissionId";
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PostFlightReport_Mission_Revision"
    ON "PostFlightReport" ("MissionId", "Revision");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PostFlightReport_Current"
    ON "PostFlightReport" ("MissionId") WHERE "IsCurrent" = TRUE;
CREATE INDEX IF NOT EXISTS "IX_PostFlightReport_GenerationQueue"
    ON "PostFlightReport" ("GenerationStatus", "CreatedAt") WHERE "IsCurrent" = TRUE;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PostFlightReport_GenerationStatus') THEN
        ALTER TABLE "PostFlightReport" ADD CONSTRAINT "CK_PostFlightReport_GenerationStatus"
            CHECK ("GenerationStatus" IN ('Queued', 'Generating', 'Available', 'Failed', 'Outdated'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PostFlightReport_Revision') THEN
        ALTER TABLE "PostFlightReport" ADD CONSTRAINT "CK_PostFlightReport_Revision"
            CHECK ("Revision" > 0);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "FlightCompletionEvent" (
    "Id" uuid PRIMARY KEY,
    "MissionId" uuid NOT NULL REFERENCES "Mission" ("Id") ON DELETE CASCADE,
    "UAVId" uuid NOT NULL REFERENCES "UAV" ("Id") ON DELETE RESTRICT,
    "SourceId" text NOT NULL,
    "IdempotencyKey" text NOT NULL,
    "EventType" text NOT NULL,
    "CompletedAt" timestamp with time zone NOT NULL,
    "ReceivedAt" timestamp with time zone NOT NULL,
    "IsTrusted" boolean NOT NULL,
    "Evidence" text NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_FlightCompletionEvent_IdempotencyKey"
    ON "FlightCompletionEvent" ("IdempotencyKey");
CREATE INDEX IF NOT EXISTS "IX_FlightCompletionEvent_Mission_CompletedAt"
    ON "FlightCompletionEvent" ("MissionId", "CompletedAt" DESC);

COMMIT;
