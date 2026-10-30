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

Registration uses multipart `POST /api/pilot-registrations/with-files`: `data` is registration JSON and `files` contains exactly one PDF or Word (.doc/.docx) document (10 MB maximum). Files are validated before uploading. Object paths are saved in LicenseImage; failed submissions clean up uploaded files. The existing JSON registration API remains available for compatibility.

Authenticated users update their own avatar with multipart `POST /api/users/avatar`, field `file` (JPEG/PNG/WebP, 5 MB max). The unique object path is persisted in User.AvatarPath. Replacing an avatar deletes the old object after saving the new path. GET requires the current user's login cookie through the FE proxy.

Only operation_manager can request registration details. The detail API returns signed license links lasting 15 minutes; refresh document links in the manager screen when expired. Storage has no anonymous/client access policies; uploads and signed links are issued only by BE using the server key. File signatures and declared MIME types are checked.

Existing documents stored only in a browser's IndexedDB are not migrated automatically. New applications persist documents in Supabase and can be reviewed on another machine.

Verification: BE build, FE typecheck, targeted ESLint, verify compilation and the changed frontend files. A temporary HTTP mock check also passed image/PDF validation, spoofed MIME rejection, private upload, signed URLs and cleanup. Live verification against the configured private bucket passed: upload a temporary PNG, create a signed URL, download and compare the original bytes, then delete the temporary object. The local configuration file is ignored by Git.

## License updates

All license changes use `/api/users/pilot-update-requests`; `/api/license-changes`
has been removed. Existing `PilotUpdateRequest` and `LicenseImage` records remain
in place; no data migration is required.

- Operator: `POST /api/users/pilot-update-requests`, multipart `data` (JSON with
  `pilotLicenseNo`, `licenseIssuedDate`, `licenseExpiredDate`) and 1–10 `files`.
- Operator/manager: `GET /api/users/pilot-update-requests?pageNumber=1&pageSize=100&status=Pending`.
  Returns `items`, `pageNumber`, `pageSize`, `totalItems`, `totalPages`.
  Operators only receive their own requests; managers can also filter by `search`.
- Operator/manager: `GET /api/users/pilot-update-requests/{id}`. Includes current
  license fields and signed `licenseImageUrls`; other operators receive 404.
- Manager: `PUT /api/users/pilot-update-requests/{id}/approve` (no body).
- Manager: `PUT /api/users/pilot-update-requests/{id}/reject`, JSON
  `{"rejectReason":"Reason for rejection"}` (1–1000 characters).

Decisions reuse `UserService` and execute in a serializable, retryable transaction.
Only pending requests can be reviewed. Approval of expired licenses is blocked.
`PUT /api/users/pilot-profile` remains for phone, birth date and experience;
license changes must use the multipart submission endpoint. `GET` on that profile
still returns the official license. Pending/rejected requests do not replace it.

License uploads now accept exactly one PDF or Word (.doc/.docx), up to 10 MB. Apply `migrations/20261023000100_license_word_documents.sql` to allow Word MIME types in the existing private bucket. Existing images and avatar uploads remain supported.

The Word MIME allowlist update has also been applied to the connected DroneOps bucket; the bucket remains private with a 10 MB limit.

## Versioned account workflows

Apply `20261030000200_account_profile_workflows.sql` explicitly with:

```sh
dotnet run --project be/DroneOps/DroneOps.API --no-launch-profile -- --migrate-account-workflows --environment Development
```

Profile writes require the `profileVersion` returned by the latest profile read and
return a conflict for stale writes. Avatar objects stay in the private bucket; reads
require an authenticated endpoint and receive a 15-minute signed URL. Failed uploads
do not replace the stored avatar path.

Email challenges are persisted with only a salted challenge hash, scoped to account
and normalized new email. Confirmation locks the challenge/account, rechecks the
normalized unique-email index, updates the email, consumes the challenge, revokes all
sessions and writes actor/target/time/result audit evidence in one transaction.

License proposals remain separate from the current Pilot record. A new proposal marks
the prior pending proposal `Superseded`; a partial unique index enforces one pending
proposal. Authors cannot review their own request. A unique immutable decision audit
turns stale/concurrent decisions into conflicts without duplicate decisions. Manager
and operator notifications continue through the existing persistent notification flow.

Verification:

```sh
dotnet run --project be/DroneOps/DroneOps.AuthChecks -- --license-workflow <absolute-repository-path>
dotnet run --project be/DroneOps/DroneOps.AuthChecks -- --media-access
```
