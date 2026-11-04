-- Optional primary-battery measurements; old samples remain unknown (NULL).
ALTER TABLE public."TelemetryRecord"
    ADD COLUMN IF NOT EXISTS "BatteryCurrentAmps" numeric,
    ADD COLUMN IF NOT EXISTS "BatteryTemperatureCelsius" numeric,
    ADD COLUMN IF NOT EXISTS "BatteryConsumedMah" integer,
    ADD COLUMN IF NOT EXISTS "BatteryCellVoltagesVolts" numeric[];
