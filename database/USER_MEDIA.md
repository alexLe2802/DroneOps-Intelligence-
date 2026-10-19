# User media storage

Project: `diaswbzjhvzwxcadlias` (DroneOps). Private bucket: `user-media`.

Set `SupabaseStorage__ServiceKey` on the backend to the project's Supabase secret key or legacy service_role key. For local Development, the ignored `be/DroneOps/DroneOps.API/appsettings.Local.json` can contain:

```json
"SupabaseStorage": {
  "ServiceKey": "your-server-only-key"
}
```

Merge this section with existing configuration; do not replace other sections. Restart BE after changing it. No Supabase keys are needed by FE.

Apply `migrations/005_user_media.sql` for another environment. It has already been applied to the connected DroneOps project. Configure `SupabaseStorage:Url` if using another project (and update the avatar redirect host allowlist in the FE API route).

Registration uses multipart `POST /api/pilot-registrations/with-files`: `data` is registration JSON and repeated `files` parts contain 1–10 JPEG/PNG/WebP/PDF documents (10 MB each). Files are validated before uploading. Object paths are saved in LicenseImage; failed submissions clean up uploaded files. The existing JSON registration API remains available for compatibility.

Authenticated users update their own avatar with multipart `POST /api/users/avatar`, field `file` (JPEG/PNG/WebP, 5 MB max). The unique object path is persisted in User.AvatarPath. Replacing an avatar deletes the old object after saving the new path. GET requires the current user's login cookie through the FE proxy.

Only operation_manager can request registration details. The detail API returns signed license links lasting 15 minutes; refresh document links in the manager screen when expired. Storage has no anonymous/client access policies; uploads and signed links are issued only by BE using the server key. File signatures and declared MIME types are checked.

Existing documents stored only in a browser's IndexedDB are not migrated automatically. New applications persist documents in Supabase and can be reviewed on another machine.

Verification: BE build, FE typecheck, targeted ESLint, verify compilation and the changed frontend files. A temporary HTTP mock check also passed image/PDF validation, spoofed MIME rejection, private upload, signed URLs and cleanup. Live upload requires the backend storage key.
