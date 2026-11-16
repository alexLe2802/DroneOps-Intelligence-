ALTER TABLE public."UAV"
    ADD COLUMN IF NOT EXISTS "PendingImageStoragePath" text NULL,
    ADD COLUMN IF NOT EXISTS "PendingImageContentType" text NULL,
    ADD COLUMN IF NOT EXISTS "PendingImageSubmittedAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "PendingImageSubmittedBy" uuid NULL,
    ADD COLUMN IF NOT EXISTS "ImageReviewedBy" uuid NULL;

UPDATE public."UAV"
SET "PendingImageStoragePath" = "ImageStoragePath",
    "PendingImageContentType" = "ImageContentType",
    "PendingImageSubmittedAt" = COALESCE("ImageUpdatedAt", NOW()),
    "ImageStoragePath" = NULL,
    "ImageContentType" = NULL,
    "ImageModerationStatus" = 'Pending',
    "ImageModerationAttempts" = 0,
    "ImageModerationNextAttemptAt" = NULL,
    "ImageModeratedAt" = NULL,
    "ImageModerationReason" = 'Waiting for manager review.'
WHERE "ImageStoragePath" IS NOT NULL
  AND "ImageModerationStatus" IN ('Pending', 'Processing', 'RetryScheduled', 'ManualReview');

DROP INDEX IF EXISTS public."IX_UAV_ImageModerationQueue";
CREATE INDEX IF NOT EXISTS "IX_UAV_PendingImageReview"
    ON public."UAV" ("PendingImageSubmittedAt")
    WHERE "PendingImageStoragePath" IS NOT NULL
      AND "ImageModerationStatus" = 'Pending';

COMMENT ON COLUMN public."UAV"."ImageModerationStatus" IS
    'Manual manager decision state for a proposed UAV image: Pending, Approved, or Rejected.';
