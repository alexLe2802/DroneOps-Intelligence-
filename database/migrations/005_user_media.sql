ALTER TABLE public."User" ADD COLUMN IF NOT EXISTS "AvatarPath" text;
INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES ('user-media', 'user-media', false, 10485760, ARRAY['image/jpeg','image/png','image/webp','application/pdf'])
ON CONFLICT (id) DO NOTHING;
