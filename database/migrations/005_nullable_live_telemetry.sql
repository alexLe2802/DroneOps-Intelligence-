BEGIN;

-- KAN-43: optional MAVLink metrics remain NULL when their source message is absent.
ALTER TABLE "TelemetryRecord"
    ALTER COLUMN "Latitude" DROP NOT NULL,
    ALTER COLUMN "Longitude" DROP NOT NULL,
    ALTER COLUMN "Armed" DROP NOT NULL;

ALTER TABLE "TelemetryRecord"
    ADD COLUMN IF NOT EXISTS "HeadingDegrees" numeric NULL,
    ADD COLUMN IF NOT EXISTS "BatteryVoltageVolts" numeric NULL,
    ADD COLUMN IF NOT EXISTS "BoardTemperatureCelsius" numeric NULL;

-- Retry safety and latest/playback query paths. Partial indexes ignore legacy NULL values.
-- Preserve legacy duplicate samples but clear replay keys after the first occurrence so
-- the new uniqueness constraint can be introduced without deleting telemetry history.
WITH ranked AS (
    SELECT "Id", ROW_NUMBER() OVER (
        PARTITION BY "IdempotencyKey"
        ORDER BY "ReceivedAt", "Id"
    ) AS duplicate_number
    FROM "TelemetryRecord"
    WHERE "IdempotencyKey" IS NOT NULL
)
UPDATE "TelemetryRecord" AS telemetry
SET "IdempotencyKey" = NULL
FROM ranked
WHERE telemetry."Id" = ranked."Id"
  AND ranked.duplicate_number > 1;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_TelemetryRecord_IdempotencyKey"
    ON "TelemetryRecord" ("IdempotencyKey")
    WHERE "IdempotencyKey" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_TelemetryRecord_Mission_Latest"
    ON "TelemetryRecord" ("MissionId", "SourceTimestamp" DESC, "ReceivedAt" DESC)
    WHERE "IsValid" = TRUE;

CREATE INDEX IF NOT EXISTS "IX_TelemetryRecord_UAV_Latest"
    ON "TelemetryRecord" ("UAVId", "SourceTimestamp" DESC, "ReceivedAt" DESC)
    WHERE "IsValid" = TRUE;

COMMIT;
