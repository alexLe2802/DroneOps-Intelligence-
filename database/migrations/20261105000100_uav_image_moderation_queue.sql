ALTER TABLE public."UAV"
    ADD COLUMN IF NOT EXISTS "ImageModerationStatus" text NULL,
    ADD COLUMN IF NOT EXISTS "ImageModerationAttempts" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "ImageModerationNextAttemptAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ImageModeratedAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ImageModerationReason" text NULL;

CREATE INDEX IF NOT EXISTS "IX_UAV_ImageModerationQueue"
    ON public."UAV" ("ImageModerationNextAttemptAt")
    WHERE "ImageModerationStatus" IN ('Pending', 'RetryScheduled', 'Processing');

COMMENT ON COLUMN public."UAV"."ImageModerationStatus" IS
    'Pending, Processing, RetryScheduled, Approved, Rejected, or ManualReview. Provider failures never mean Rejected.';
