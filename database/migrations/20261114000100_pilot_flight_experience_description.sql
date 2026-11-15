BEGIN;

SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

ALTER TABLE public."Pilot"
    ADD COLUMN IF NOT EXISTS "FlightExperienceDescription" varchar(500);

UPDATE public."Pilot" AS p
SET "FlightExperienceDescription" = (
    SELECT left(nullif(btrim(pr."Description"), ''), 500)
    FROM public."PilotRegistration" AS pr
    JOIN public."User" AS u ON u."Id" = p."UserId"
    WHERE lower(btrim(pr."Email")) = lower(btrim(u."Email"))
      AND pr."Status" = 'Approved'
      AND nullif(btrim(pr."Description"), '') IS NOT NULL
    ORDER BY pr."CreatedAt" DESC, pr."Id" DESC
    LIMIT 1
)
WHERE p."FlightExperienceDescription" IS NULL
  AND EXISTS (
      SELECT 1
      FROM public."PilotRegistration" AS pr
      JOIN public."User" AS u ON u."Id" = p."UserId"
      WHERE lower(btrim(pr."Email")) = lower(btrim(u."Email"))
        AND pr."Status" = 'Approved'
        AND nullif(btrim(pr."Description"), '') IS NOT NULL
  );

COMMIT;
