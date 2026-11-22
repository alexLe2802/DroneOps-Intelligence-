BEGIN;

SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

-- 1. Bổ sung trạng thái và version của giấy phép trong bảng Pilot
ALTER TABLE public."Pilot"
    ADD COLUMN IF NOT EXISTS "LicenseStatus" varchar(30) NOT NULL DEFAULT 'Active',
    ADD COLUMN IF NOT EXISTS "LicenseVersion" bigint NOT NULL DEFAULT 1;

-- 2. Bảng lưu vết kiểm toán (Audit Trail) thay đổi trạng thái giấy phép
CREATE TABLE IF NOT EXISTS public."PilotLicenseAudit" (
    "Id" uuid PRIMARY KEY,
    "PilotId" uuid NOT NULL REFERENCES public."Pilot"("Id") ON DELETE RESTRICT,
    "Action" text NOT NULL,
    "PreviousStatus" text NOT NULL,
    "NewStatus" text NOT NULL,
    "Reason" text NOT NULL,
    "ActorId" uuid NOT NULL REFERENCES public."User"("Id"),
    "OccurredAt" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_PilotLicenseAudit_PilotId"
    ON public."PilotLicenseAudit"("PilotId", "OccurredAt" DESC);

COMMIT;
