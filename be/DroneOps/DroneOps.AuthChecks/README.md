# Auth and profile checks

Run the default executable for offline role normalization, endpoint authorization contracts and registration validation checks.

For an integration check against the database and Storage configured in `DroneOps.API/appsettings.Local.json`, run:

```sh
dotnet run --project be/DroneOps/DroneOps.AuthChecks -- --license-workflow /absolute/path/to/SEP490
```

The integration check creates temporary users and a pilot, persists phone/birth/experience changes, verifies email-change OTP logic, uploads license evidence, tests ownership, pending-request rejection and manager approval/rejection. It uses a captured email transport: no actual email is sent. It deletes its temporary users, pilot, requests and Storage objects in `finally`. Existing operator and manager roles must exist. This check makes real temporary database/Storage writes; use a development project.

These controller/service checks do not cover browser interactions or actual SMTP delivery. The original Firebase session checks are archived in `FirebaseSessionChecks.legacy.txt` because those endpoints no longer exist.
