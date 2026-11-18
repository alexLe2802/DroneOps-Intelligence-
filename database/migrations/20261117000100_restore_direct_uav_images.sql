DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'UAV'
          AND column_name = 'PendingImageStoragePath'
    ) THEN
        EXECUTE $migration$
            UPDATE public."UAV"
            SET "ImageStoragePath" = "PendingImageStoragePath",
                "ImageContentType" = "PendingImageContentType",
                "ImageUpdatedAt" = COALESCE("PendingImageSubmittedAt", NOW()),
                "ImageModerationStatus" = NULL,
                "ImageModerationAttempts" = 0,
                "ImageModerationNextAttemptAt" = NULL,
                "ImageModeratedAt" = NULL,
                "ImageModerationReason" = NULL,
                "PendingImageStoragePath" = NULL,
                "PendingImageContentType" = NULL,
                "PendingImageSubmittedAt" = NULL,
                "PendingImageSubmittedBy" = NULL
            WHERE "PendingImageStoragePath" IS NOT NULL
        $migration$;

        DROP INDEX IF EXISTS public."IX_UAV_PendingImageReview";
    END IF;
END $$;

UPDATE public."UAV"
SET "ImageModerationStatus" = NULL,
    "ImageModerationAttempts" = 0,
    "ImageModerationNextAttemptAt" = NULL,
    "ImageModeratedAt" = NULL,
    "ImageModerationReason" = NULL
WHERE "ImageStoragePath" IS NOT NULL
  AND "ImageModerationStatus" IN ('Pending', 'Processing', 'RetryScheduled', 'ManualReview');

DO $$
BEGIN
    IF to_regclass('public."Notification"') IS NOT NULL THEN
        UPDATE public."Notification"
        SET "IsRead" = TRUE,
            "ReadAt" = COALESCE("ReadAt", NOW()),
            "ReferenceType" = 'UAVManualReviewQueueClosed',
            "Message" = 'All pending UAV image reviews have been resolved.'
        WHERE "Type" = 'UavManualReview'
          AND "ReferenceType" = 'UAVManualReviewQueueActive';
    END IF;
END $$;

COMMENT ON COLUMN public."UAV"."ImageModerationStatus" IS
    'Null for a published image; Rejected after a manager removes inappropriate content.';
