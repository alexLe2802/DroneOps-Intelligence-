ALTER TABLE public."UAV"
    ADD COLUMN IF NOT EXISTS "ImageStoragePath" text NULL;

ALTER TABLE public."UAV"
    ADD COLUMN IF NOT EXISTS "ImageContentType" text NULL;

ALTER TABLE public."UAV"
    ADD COLUMN IF NOT EXISTS "ImageUpdatedAt" timestamp with time zone NULL;
