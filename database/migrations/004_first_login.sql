BEGIN;
DO $$
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='User' AND column_name='RequiresFirstLogin') THEN
  ALTER TABLE public."User" ADD COLUMN "RequiresFirstLogin" boolean NOT NULL DEFAULT false;
  -- Legacy approval records have no persisted activation evidence; require one verification.
  UPDATE public."User" u SET "RequiresFirstLogin"=true
  WHERE NOT u."WebAccessProvisioned" AND EXISTS (SELECT 1 FROM public."PilotRegistration" p WHERE lower(trim(p."Email"))=lower(trim(u."Email")) AND p."Status"='Approved');
 END IF;
END $$;
COMMIT;
