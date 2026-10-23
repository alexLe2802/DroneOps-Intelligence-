BEGIN;

CREATE TABLE IF NOT EXISTS "IncidentTransition" (
    "Id" uuid PRIMARY KEY,
    "IncidentId" uuid NOT NULL REFERENCES "Incident"("Id") ON DELETE CASCADE,
    "FromStatus" varchar(32) NOT NULL,
    "ToStatus" varchar(32) NOT NULL,
    "EventType" varchar(32) NOT NULL,
    "Notes" text NULL,
    "ChangedBy" uuid NULL REFERENCES "User"("Id") ON DELETE SET NULL,
    "CreatedAt" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_IncidentTransition_Incident_CreatedAt"
    ON "IncidentTransition" ("IncidentId", "CreatedAt");

CREATE TABLE IF NOT EXISTS "IncidentAlertDelivery" (
    "Id" uuid PRIMARY KEY,
    "IncidentId" uuid NOT NULL REFERENCES "Incident"("Id") ON DELETE CASCADE,
    "TransitionId" uuid NOT NULL REFERENCES "IncidentTransition"("Id") ON DELETE CASCADE,
    "RecipientUserId" uuid NOT NULL REFERENCES "User"("Id") ON DELETE CASCADE,
    "EventType" varchar(32) NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "ReadAt" timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_IncidentAlertDelivery_Transition_Recipient"
    ON "IncidentAlertDelivery" ("TransitionId", "RecipientUserId");
CREATE INDEX IF NOT EXISTS "IX_IncidentAlertDelivery_Unread"
    ON "IncidentAlertDelivery" ("RecipientUserId", "CreatedAt" DESC)
    WHERE "ReadAt" IS NULL;

-- Khôi phục audit tối thiểu cho dữ liệu incident đã có trước migration.
INSERT INTO "IncidentTransition"
    ("Id", "IncidentId", "FromStatus", "ToStatus", "EventType", "Notes", "ChangedBy", "CreatedAt")
SELECT md5(i."Id"::text || ':raised')::uuid, i."Id", '', 'Open', 'Raised', i."Description", NULL, i."CreatedAt"
FROM "Incident" i
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO "IncidentTransition"
    ("Id", "IncidentId", "FromStatus", "ToStatus", "EventType", "Notes", "ChangedBy", "CreatedAt")
SELECT md5(i."Id"::text || ':acknowledged')::uuid, i."Id", 'Open', 'Acknowledged', 'Acknowledged',
       NULL, i."AcknowledgedBy", i."AcknowledgedAt"
FROM "Incident" i
WHERE i."AcknowledgedAt" IS NOT NULL
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO "IncidentTransition"
    ("Id", "IncidentId", "FromStatus", "ToStatus", "EventType", "Notes", "ChangedBy", "CreatedAt")
SELECT md5(i."Id"::text || ':resolved')::uuid, i."Id",
       CASE WHEN i."AcknowledgedAt" IS NULL THEN 'Open' ELSE 'Acknowledged' END,
       'Resolved', 'Resolved', i."ResolutionNotes", i."ResolvedBy", i."ResolvedAt"
FROM "Incident" i
WHERE i."ResolvedAt" IS NOT NULL
ON CONFLICT ("Id") DO NOTHING;

-- Lịch sử transition là append-only ở cả application lẫn database.
CREATE OR REPLACE FUNCTION prevent_incident_transition_mutation()
RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'IncidentTransition is append-only';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS "TR_IncidentTransition_AppendOnly" ON "IncidentTransition";
CREATE TRIGGER "TR_IncidentTransition_AppendOnly"
BEFORE UPDATE OR DELETE ON "IncidentTransition"
FOR EACH ROW EXECUTE FUNCTION prevent_incident_transition_mutation();

COMMIT;
