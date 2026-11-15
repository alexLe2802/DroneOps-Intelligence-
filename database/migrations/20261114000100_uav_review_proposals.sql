-- Additive migration. Execute only after target verification, fresh verified backup and isolated rehearsal.
BEGIN;
ALTER TABLE "UAV" ADD COLUMN IF NOT EXISTS "HasPendingProposal" boolean NOT NULL DEFAULT false;
ALTER TABLE "UAV" ADD COLUMN IF NOT EXISTS "RegistryVersion" bigint NOT NULL DEFAULT 0;
ALTER TABLE "UAV" ADD COLUMN IF NOT EXISTS "HasApprovedInformation" boolean NOT NULL DEFAULT false;
UPDATE "UAV" SET "HasApprovedInformation" = true WHERE "Status" IN ('Active', 'Inactive') AND NOT "HasApprovedInformation";
CREATE TABLE IF NOT EXISTS "UavReviewRequest" (
  "Id" uuid PRIMARY KEY,
  "UavId" uuid NOT NULL REFERENCES "UAV"("Id") ON DELETE RESTRICT,
  "SubmittedBy" uuid NULL,
  "SubmittedAt" timestamptz NULL,
  "BaseVersion" bigint NOT NULL,
  "Kind" text NOT NULL,
  "Status" text NOT NULL,
  "BaseInformationJson" text NOT NULL,
  "ProposedInformationJson" text NOT NULL,
  "ReviewedBy" uuid NULL,
  "ReviewedAt" timestamptz NULL,
  "Reason" text NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_UavReviewRequest_UavId" ON "UavReviewRequest"("UavId") WHERE "Status" = 'Pending';
-- Preflight: existing duplicate codes must be resolved explicitly; never rename registry identities in a migration.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_UAV_Code_IgnoreCase" ON "UAV" (lower("Code"));
-- Legacy Pending records are reviewed explicitly; do not fabricate old submission/reviewer evidence.
COMMIT;
