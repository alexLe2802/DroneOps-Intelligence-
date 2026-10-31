BEGIN;

ALTER TABLE public."User" ADD COLUMN IF NOT EXISTS "ProfileVersion" bigint NOT NULL DEFAULT 1;
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM public."User" GROUP BY lower(trim("Email")) HAVING count(*)>1) THEN
        RAISE EXCEPTION 'Cannot enforce normalized email uniqueness: duplicate accounts exist';
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS "UX_User_NormalizedEmail" ON public."User" (lower(trim("Email")));

CREATE TABLE IF NOT EXISTS droneops."EmailChangeChallenge" (
    "Id" uuid PRIMARY KEY,
    "AccountId" uuid NOT NULL REFERENCES public."User"("Id") ON DELETE CASCADE,
    "NewEmail" text NOT NULL,
    "CodeHash" varchar(64) NOT NULL CHECK (length("CodeHash")=64),
    "FailedAttempts" integer NOT NULL DEFAULT 0,
    "ExpiresAt" timestamptz NOT NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    "ConsumedAt" timestamptz NULL,
    "Status" text NOT NULL CHECK ("Status" IN ('Pending','Consumed','Expired','Locked','DeliveryFailed','Superseded'))
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_EmailChangeChallenge_Account_Pending"
    ON droneops."EmailChangeChallenge"("AccountId") WHERE "Status"='Pending';
CREATE INDEX IF NOT EXISTS "IX_EmailChangeChallenge_Expiry"
    ON droneops."EmailChangeChallenge"("ExpiresAt") WHERE "Status"='Pending';

CREATE TABLE IF NOT EXISTS droneops."AccountWorkflowAudit" (
    "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "ActorId" uuid NULL REFERENCES public."User"("Id"),
    "TargetId" uuid NULL REFERENCES public."User"("Id"),
    "Action" text NOT NULL,
    "Result" text NOT NULL,
    "OccurredAt" timestamptz NOT NULL DEFAULT now(),
    "DetailsJson" jsonb NOT NULL DEFAULT '{}'
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_PilotUpdateRequest_Pending"
    ON public."PilotUpdateRequest"("PilotId") WHERE "Status"='Pending';
CREATE TABLE IF NOT EXISTS public."PilotUpdateDecisionAudit" (
    "Id" uuid PRIMARY KEY,
    "RequestId" uuid NOT NULL REFERENCES public."PilotUpdateRequest"("Id"),
    "ActorId" uuid NOT NULL REFERENCES public."User"("Id"),
    "TargetUserId" uuid NOT NULL REFERENCES public."User"("Id"),
    "Result" text NOT NULL CHECK ("Result" IN ('Approved','Rejected')),
    "OccurredAt" timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PilotUpdateDecisionAudit_Request"
    ON public."PilotUpdateDecisionAudit"("RequestId");

ALTER TABLE droneops."EmailChangeChallenge" ENABLE ROW LEVEL SECURITY;
ALTER TABLE droneops."AccountWorkflowAudit" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."PilotUpdateDecisionAudit" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON droneops."EmailChangeChallenge",droneops."AccountWorkflowAudit",public."PilotUpdateDecisionAudit" FROM anon,authenticated;

COMMIT;
