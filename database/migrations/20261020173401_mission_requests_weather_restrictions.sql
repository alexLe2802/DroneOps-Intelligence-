BEGIN;
ALTER TABLE public."Mission" ADD COLUMN IF NOT EXISTS "AltitudeReference" text NULL;
ALTER TABLE public."Geofence" ADD COLUMN IF NOT EXISTS "VerticalRule" text NOT NULL DEFAULT 'Unspecified',
 ADD COLUMN IF NOT EXISTS "AltitudeReference" text NULL,
 ADD COLUMN IF NOT EXISTS "MinAltitudeMeters" numeric NULL, ADD COLUMN IF NOT EXISTS "MaxAltitudeMeters" numeric NULL,
 ADD COLUMN IF NOT EXISTS "Revision" bigint NOT NULL DEFAULT 1;
CREATE TABLE IF NOT EXISTS public."MissionUpdateRequest" (
 "Id" uuid PRIMARY KEY,"MissionId" uuid NOT NULL,"ActorId" uuid NOT NULL,"IsInitial" boolean NOT NULL DEFAULT false,
 "Status" text NOT NULL,"BasePlanHash" text NOT NULL,"ProposedPlanJson" jsonb NOT NULL,"ValidationRunId" uuid NULL,
 "CreatedAt" timestamptz NOT NULL,"ReviewedBy" uuid NULL,"ReviewedAt" timestamptz NULL,"Reason" text NULL
);
INSERT INTO public."MissionUpdateRequest" ("Id","MissionId","ActorId","IsInitial","Status","BasePlanHash","ProposedPlanJson","CreatedAt")
SELECT gen_random_uuid(),m."Id",m."CreatedBy",true,'Pending','',jsonb_build_object('name',m."Name",'description',m."Description",'uavId',m."UAVId",'altitudeReference',m."AltitudeReference",'startTime',m."StartTime",'endTime',m."EndTime",'waypoints',COALESCE((SELECT jsonb_agg(jsonb_build_object('sequenceOrder',w."SequenceOrder",'latitude',w."Latitude",'longitude',w."Longitude",'altitude',w."Altitude",'actionType',w."ActionType") ORDER BY w."SequenceOrder") FROM public."Waypoint" w WHERE w."MissionId"=m."Id"),'[]'::jsonb)),now()
FROM public."Mission" m WHERE m."Status" IN ('Pending','Pending Review','PendingApproval','Submitted') AND NOT EXISTS (SELECT 1 FROM public."MissionUpdateRequest" r WHERE r."MissionId"=m."Id");
ALTER TABLE public."MissionUpdateRequest" ADD COLUMN IF NOT EXISTS "BasePlanJson" jsonb NOT NULL DEFAULT '{}';
CREATE UNIQUE INDEX IF NOT EXISTS "UX_MissionUpdateRequest_Pending" ON public."MissionUpdateRequest" ("MissionId") WHERE "Status"='Pending';
CREATE INDEX IF NOT EXISTS "IX_MissionUpdateRequest_History" ON public."MissionUpdateRequest" ("MissionId","CreatedAt" DESC);
ALTER TABLE public."MissionValidationRun" ADD COLUMN IF NOT EXISTS "UpdateRequestId" uuid NULL;
CREATE INDEX IF NOT EXISTS "IX_MissionValidationRun_Request" ON public."MissionValidationRun" ("UpdateRequestId","CheckedAt" DESC);
ALTER TABLE public."MissionApproval" ADD COLUMN IF NOT EXISTS "UpdateRequestId" uuid NULL;
CREATE TABLE IF NOT EXISTS public."GeofenceRevision" (
 "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),"GeofenceId" uuid NOT NULL,"Revision" bigint NOT NULL,"Action" text NOT NULL,"SnapshotJson" jsonb NOT NULL,"RecordedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_GeofenceRevision" ON public."GeofenceRevision" ("GeofenceId","Revision");
INSERT INTO public."GeofenceRevision" ("GeofenceId","Revision","Action","SnapshotJson") SELECT g."Id",g."Revision",'Baseline',to_jsonb(g) FROM public."Geofence" g ON CONFLICT DO NOTHING;
CREATE OR REPLACE FUNCTION public.droneops_geofence_revision() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' THEN NEW."Revision"=OLD."Revision"+1; END IF;
 IF TG_OP='DELETE' THEN
 INSERT INTO public."GeofenceRevision" ("GeofenceId","Revision","Action","SnapshotJson") VALUES (OLD."Id",OLD."Revision"+1,'Deleted',to_jsonb(OLD)); RETURN OLD;
 ELSE
 INSERT INTO public."GeofenceRevision" ("GeofenceId","Revision","Action","SnapshotJson") VALUES (NEW."Id",NEW."Revision",TG_OP,to_jsonb(NEW)); RETURN NEW;
 END IF;
END $$;
REVOKE ALL ON FUNCTION public.droneops_geofence_revision() FROM PUBLIC;
DROP TRIGGER IF EXISTS droneops_geofence_revision ON public."Geofence";
CREATE TRIGGER droneops_geofence_revision BEFORE INSERT OR UPDATE OR DELETE ON public."Geofence" FOR EACH ROW EXECUTE FUNCTION public.droneops_geofence_revision();
-- Capture schema/rule metadata additions without rewriting any older revision snapshot.
UPDATE public."Geofence" g SET "VerticalRule"=g."VerticalRule" WHERE NOT EXISTS (SELECT 1 FROM public."GeofenceRevision" r WHERE r."GeofenceId"=g."Id" AND r."Revision"=g."Revision" AND r."SnapshotJson" ? 'VerticalRule');
CREATE TABLE IF NOT EXISTS public."MissionWeatherCache" ("Key" text PRIMARY KEY,"ForecastJson" jsonb NOT NULL,"FetchedAt" timestamptz NOT NULL,"ExpiresAt" timestamptz NOT NULL);
ALTER TABLE public."MissionUpdateRequest" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."GeofenceRevision" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."MissionWeatherCache" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public."MissionUpdateRequest",public."GeofenceRevision",public."MissionWeatherCache" FROM anon,authenticated;
COMMIT;
