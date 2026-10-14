-- Apply explicitly with the API's --migrate-auth command. No Firebase secrets or raw JWTs are stored.
BEGIN;
CREATE SCHEMA IF NOT EXISTS droneops;
CREATE TABLE IF NOT EXISTS droneops.roles (
    code text PRIMARY KEY CHECK (code IN ('uav_operator', 'operations_manager')),
    display_name text NOT NULL
);
INSERT INTO droneops.roles(code, display_name) VALUES
    ('uav_operator', 'UAV Operator / Pilot'),
    ('operations_manager', 'Operations Manager')
ON CONFLICT (code) DO NOTHING;
-- User/Role are supplied by the existing application schema.
ALTER TABLE public."User" ADD COLUMN IF NOT EXISTS "FirebaseUid" text;
ALTER TABLE public."User" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
ALTER TABLE public."User" ADD COLUMN IF NOT EXISTS "WebAccessProvisioned" boolean NOT NULL DEFAULT false;
ALTER TABLE public."User" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamptz NOT NULL DEFAULT now();
ALTER TABLE public."User" ADD COLUMN IF NOT EXISTS "CreatedBy" uuid REFERENCES public."User"("Id");
CREATE UNIQUE INDEX IF NOT EXISTS ux_user_firebase_uid ON public."User"("FirebaseUid") WHERE "FirebaseUid" IS NOT NULL;
INSERT INTO public."Role"("Id", "Name", "CreatedAt")
SELECT gen_random_uuid(), 'OperationsManager', now()
WHERE NOT EXISTS (SELECT 1 FROM public."Role" WHERE "Name" = 'OperationsManager');
INSERT INTO public."Role"("Id", "Name", "CreatedAt")
SELECT gen_random_uuid(), 'Pilot', now()
WHERE NOT EXISTS (SELECT 1 FROM public."Role" WHERE "Name" = 'Pilot');
CREATE TABLE IF NOT EXISTS droneops.auth_sessions (
    id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES public."User"("Id"),
    token_hash varchar(64) NOT NULL UNIQUE CHECK (length(token_hash) = 64),
    login_proof_hash varchar(64) NOT NULL UNIQUE CHECK (length(login_proof_hash) = 64),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
    revoked_at timestamptz,
    user_agent varchar(300) NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_auth_sessions_account ON droneops.auth_sessions(account_id, expires_at) WHERE revoked_at IS NULL;
CREATE TABLE IF NOT EXISTS droneops.auth_audit (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_id uuid REFERENCES public."User"("Id"),
    target_id uuid REFERENCES public."User"("Id"),
    action text NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now()
);
-- This private schema is accessed by the backend connection only, never the Supabase client API.
REVOKE ALL ON SCHEMA droneops FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA droneops FROM PUBLIC;
ALTER TABLE droneops.auth_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE droneops.auth_audit ENABLE ROW LEVEL SECURITY;
ALTER TABLE droneops.roles ENABLE ROW LEVEL SECURITY;
COMMIT;
