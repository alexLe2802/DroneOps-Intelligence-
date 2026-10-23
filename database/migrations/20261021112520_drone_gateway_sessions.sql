-- Private connection credentials are accessible only through the backend.
CREATE TABLE IF NOT EXISTS public."DroneConnection" (
 "Id" uuid PRIMARY KEY,
 "OwnerId" uuid NOT NULL REFERENCES public."User"("Id"),
 "UavId" uuid NOT NULL REFERENCES public."UAV"("Id"),
 "MissionId" uuid NOT NULL REFERENCES public."Mission"("Id"),
 "PairCodeHash" text NOT NULL UNIQUE,
 "TokenHash" text UNIQUE,
 "CreatedAt" timestamptz NOT NULL,
 "CodeExpiresAt" timestamptz NOT NULL,
 "ExpiresAt" timestamptz NOT NULL,
 "PairedAt" timestamptz,
 "LastGatewayAt" timestamptz,
 "LastSampleAt" timestamptz,
 "RevokedAt" timestamptz,
 "GatewayName" text,
 "SystemId" integer NOT NULL DEFAULT 0 CHECK ("SystemId" BETWEEN 0 AND 255),
 "ComponentId" integer NOT NULL DEFAULT 0 CHECK ("ComponentId" BETWEEN 0 AND 255)
);
CREATE UNIQUE INDEX IF NOT EXISTS "DroneConnection_one_active_uav" ON public."DroneConnection"("UavId") WHERE "RevokedAt" IS NULL;
CREATE INDEX IF NOT EXISTS "DroneConnection_owner_created" ON public."DroneConnection"("OwnerId","CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS "DroneConnection_mission" ON public."DroneConnection"("MissionId");
ALTER TABLE public."DroneConnection" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public."DroneConnection" FROM anon, authenticated;
