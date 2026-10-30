BEGIN;

ALTER TABLE public."Geofence"
    ADD COLUMN IF NOT EXISTS "RestrictionType" text NOT NULL DEFAULT 'Restricted';

ALTER TABLE public."Geofence" DROP CONSTRAINT IF EXISTS "CK_Geofence_RestrictionType";
ALTER TABLE public."Geofence" ADD CONSTRAINT "CK_Geofence_RestrictionType"
    CHECK ("RestrictionType" IN ('OperationalBoundary', 'Restricted', 'NoFly'));

-- The existing revision trigger increments and snapshots this schema addition.
UPDATE public."Geofence" g SET "RestrictionType"=g."RestrictionType"
WHERE NOT EXISTS (
    SELECT 1 FROM public."GeofenceRevision" r
    WHERE r."GeofenceId"=g."Id" AND r."Revision"=g."Revision"
      AND r."SnapshotJson" ? 'RestrictionType'
);

ALTER TABLE public."MissionValidationRun"
    ADD COLUMN IF NOT EXISTS "ResultStatus" text NOT NULL DEFAULT 'Error',
    ADD COLUMN IF NOT EXISTS "RestrictionRevision" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "EvidenceJson" jsonb NOT NULL DEFAULT '{}';

UPDATE public."MissionValidationRun"
SET "ResultStatus" = CASE
    WHEN COALESCE(("ResultJson"->>'error')::boolean, false) THEN 'Error'
    WHEN COALESCE(("ResultJson"->>'incomplete')::boolean, false) THEN 'Incomplete'
    WHEN "IsValid" THEN 'Pass'
    ELSE 'Fail'
END,
"RestrictionRevision" = COALESCE("ResultJson"->>'restrictionRevision', ''),
"EvidenceJson" = jsonb_build_object(
    'planHash', "PlanHash",
    'plan', "PlanJson",
    'findings', COALESCE("ResultJson"->'findings', '[]'::jsonb),
    'restrictions', COALESCE("ResultJson"->'restrictionEvidence', '[]'::jsonb)
);

ALTER TABLE public."MissionValidationRun"
    DROP CONSTRAINT IF EXISTS "CK_MissionValidationRun_ResultStatus";
ALTER TABLE public."MissionValidationRun"
    ADD CONSTRAINT "CK_MissionValidationRun_ResultStatus"
    CHECK ("ResultStatus" IN ('Pass', 'Fail', 'Incomplete', 'Error'));

COMMIT;
