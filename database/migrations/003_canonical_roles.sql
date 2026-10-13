BEGIN;
ALTER TABLE droneops.roles DROP CONSTRAINT IF EXISTS roles_code_check;
INSERT INTO droneops.roles(code, display_name) VALUES ('operation_manager', 'Operations Manager') ON CONFLICT (code) DO NOTHING;
DELETE FROM droneops.roles WHERE code = 'operations_manager';
ALTER TABLE droneops.roles ADD CONSTRAINT roles_code_check CHECK (code IN ('uav_operator', 'operation_manager'));
INSERT INTO public."Role"("Id", "Name", "CreatedAt")
SELECT gen_random_uuid(), canonical, now() FROM (VALUES ('uav_operator'), ('operation_manager')) AS roles(canonical)
WHERE NOT EXISTS (SELECT 1 FROM public."Role" WHERE "Name" = canonical);
UPDATE public."User" u SET "RoleId" = canonical."Id"
FROM public."Role" legacy, public."Role" canonical
WHERE u."RoleId" = legacy."Id" AND canonical."Name" = CASE
 WHEN lower(legacy."Name") IN ('pilot', 'uav_operator') THEN 'uav_operator'
 WHEN lower(legacy."Name") IN ('admin', 'systemadmin', 'operationsmanager', 'operations_manager', 'operation_manager') THEN 'operation_manager'
 ELSE legacy."Name" END AND u."RoleId" <> canonical."Id";
COMMIT;
