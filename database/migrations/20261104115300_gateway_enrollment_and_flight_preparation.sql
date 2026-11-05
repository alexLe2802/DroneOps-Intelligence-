-- Explicit deployment migration; do not run from normal API startup.
ALTER TABLE public."Mission" ADD COLUMN IF NOT EXISTS "AutomaticFlightRequested" boolean NOT NULL DEFAULT false;
CREATE TABLE IF NOT EXISTS public."GatewayEnrollment" (
    "UavId" uuid PRIMARY KEY REFERENCES public."DroneHardwareIdentity"("UavId") ON DELETE RESTRICT,
    "OwnerId" uuid NOT NULL REFERENCES public."User"("Id") ON DELETE RESTRICT,
    "HardwareIdentity" text NOT NULL,
    "TokenHash" text NOT NULL CHECK ("TokenHash" ~ '^[0-9A-F]{64}$'),
    "GatewayName" text NOT NULL,
    "SystemId" integer NOT NULL CHECK ("SystemId" BETWEEN 1 AND 255),
    "ComponentId" integer NOT NULL CHECK ("ComponentId" BETWEEN 1 AND 255),
    "EnrolledAt" timestamptz NOT NULL DEFAULT now(),
    "RevokedAt" timestamptz
);
CREATE TABLE IF NOT EXISTS public."MissionAutomationState" (
    "MissionId" uuid PRIMARY KEY REFERENCES public."Mission"("Id") ON DELETE CASCADE,
    "OwnerId" uuid NOT NULL REFERENCES public."User"("Id") ON DELETE RESTRICT,
    "LastPresenceAt" timestamptz,
    "ConfirmedAt" timestamptz,
    "ConfirmedPlanHash" text,
    "ConnectionId" uuid REFERENCES public."DroneConnection"("Id") ON DELETE RESTRICT,
    CONSTRAINT "AutomationConfirmationComplete" CHECK (
        ("ConfirmedAt" IS NULL AND "ConfirmedPlanHash" IS NULL AND "ConnectionId" IS NULL) OR
        ("ConfirmedAt" IS NOT NULL AND "ConfirmedPlanHash" IS NOT NULL AND "ConnectionId" IS NOT NULL)
    )
);
ALTER TABLE public."GatewayEnrollment" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."MissionAutomationState" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public."GatewayEnrollment", public."MissionAutomationState" FROM PUBLIC, anon, authenticated;
