CREATE TABLE IF NOT EXISTS public."DroneHardwareIdentity" (
 "UavId" uuid PRIMARY KEY REFERENCES public."UAV"("Id") ON DELETE RESTRICT,
 "Identity" text NOT NULL UNIQUE CHECK (
   "Identity" ~ '^(UID64-[0-9A-F]{16}|UID2-[0-9A-F]{36})$'
   AND replace(split_part("Identity",'-',2),'0','') <> ''),
 "BoundBy" uuid NOT NULL REFERENCES public."User"("Id"),
 "BoundAt" timestamptz NOT NULL
);
ALTER TABLE public."DroneHardwareIdentity" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public."DroneHardwareIdentity" FROM anon, authenticated;
ALTER TABLE public."DroneConnection" ADD COLUMN IF NOT EXISTS "HardwareIdentity" text;
-- Old sessions have no observed hardware identity; never invent/backfill one.
UPDATE public."DroneConnection" SET "RevokedAt"=now()
WHERE "HardwareIdentity" IS NULL AND "RevokedAt" IS NULL;
CREATE INDEX IF NOT EXISTS "DroneHardwareIdentity_bound_by" ON public."DroneHardwareIdentity"("BoundBy");
