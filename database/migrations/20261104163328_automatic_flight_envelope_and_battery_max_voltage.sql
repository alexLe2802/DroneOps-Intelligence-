-- Nullable fields preserve legacy manufacturer specs and nominal battery values.
-- Existing automatic missions require resubmission; do not fabricate approval.
ALTER TABLE public."Mission" ADD COLUMN IF NOT EXISTS "AutomaticFlightLimitsJson" text;
ALTER TABLE public."UAV" ADD COLUMN IF NOT EXISTS "BatteryMaxVoltage" numeric;
ALTER TABLE public."UAV" ADD COLUMN IF NOT EXISTS "FirmwareNavigationDefaultsJson" text;
ALTER TABLE public."Waypoint" ADD COLUMN IF NOT EXISTS "NavigationJson" text;
ALTER TABLE public."Mission" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."UAV" ENABLE ROW LEVEL SECURITY;
