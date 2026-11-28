BEGIN;
CREATE TABLE IF NOT EXISTS public."RegistryDocument" (
    "Id" uuid PRIMARY KEY,
    "MissionId" uuid REFERENCES public."Mission"("Id") ON DELETE CASCADE,
    "UavId" uuid REFERENCES public."UAV"("Id") ON DELETE CASCADE,
    "UploadedBy" uuid NOT NULL REFERENCES public."User"("Id"),
    "UploadedAt" timestamptz NOT NULL,
    "Name" varchar(180) NOT NULL,
    "Content" bytea NOT NULL CHECK (octet_length("Content") BETWEEN 5 AND 2097152),
    "Decision" text,
    "ReviewedBy" uuid REFERENCES public."User"("Id"),
    "ReviewedAt" timestamptz,
    "Reason" text,
    CHECK (num_nonnulls("MissionId", "UavId") = 1)
);
CREATE INDEX IF NOT EXISTS "IX_RegistryDocument_Mission" ON public."RegistryDocument" ("MissionId", "UploadedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_RegistryDocument_Uav" ON public."RegistryDocument" ("UavId") WHERE "UavId" IS NOT NULL;
ALTER TABLE public."UAV" ADD COLUMN IF NOT EXISTS "ManufacturerDocumentId" uuid;
ALTER TABLE public."MissionApproval" ADD COLUMN IF NOT EXISTS "PermitDecision" text, ADD COLUMN IF NOT EXISTS "PermitDocumentId" uuid;
REVOKE ALL ON public."RegistryDocument" FROM PUBLIC, anon, authenticated;
COMMIT;
