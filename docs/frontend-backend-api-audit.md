## UI integration completed

- Existing geofence CRUD, UAV create/list and Manager approval/status clients now run against canonical-role backend.
- Profile UI fetches `/api/users/pilot-profile` for Operators and shows license/phone/birth date/experience/drone fields with independent error/retry handling.
- Manager UAV UI retains the approved record, supports lookup through existing detail API and confirmed deactivate through PUT /api/admin/uavs/{id}/deactivate.
- Registration list maps PilotLicenseNo/DroneModel/UsagePurpose; 403 message no longer claims backend role support is pending.
- API error decoder accepts both message and Message response keys.
- FE/BE builds, targeted ESLint, TypeScript and auth integration checks verified. Real authenticated user CRUD/SMTP delivery has not been exercised.

# Role alignment update — 2026-10-13

FE and BE now use `operation_manager` / `uav_operator`. Controller authorization, flight-history scope checks, session/account role DTOs, registration role creation, JWT role output and profile/account SQL have been aligned. Legacy DB roles and existing bearer role claims are normalized to canonical names during rollout.

Program.cs was restored from 56af540, retaining Firebase cookie sessions, JWT scheme selection, AI services, CSRF and rate limiting. JWT role normalization was added for existing bearer tokens.

Migration `database/migrations/003_canonical_roles.sql` is included in the API migration runner. It consolidates User.RoleId onto canonical role rows without deleting historical role rows. The migration has not been executed against Supabase; compatibility queries support existing names until migration. Run the backend with `--migrate-auth` to apply the bundled migrations when deploying this change.

Backend was rebuilt and restarted in Development on localhost:5159 after integration. Direct and FE-proxied CSRF requests return 200; protected anonymous geofence/profile requests return 401. Missing mission lifecycle, live telemetry/incident and weather APIs remain pending Jira tasks; role alignment does not implement them. Authenticated geofence CRUD still needs an actual user-session check.

# Previous source audit — before role alignment

## Verification scope

Read current checkout controllers, services, authentication, SQL mappings, frontend API clients and proxy. Read live Swagger at http://localhost:5159/swagger/v1/swagger.json (200), /api/auth/csrf (200) and /api/auth/me without a session (401). No authenticated mutations or database changes were made. Swagger exposes routes, not role correctness; authenticated integration remains unverified.

## Result: role migration is incomplete in this checkout

Frontend and AccountRoles currently use `operations_manager` and `uav_operator`. The requested backend manager role is `operation_manager`, so the contract is not yet aligned. SessionAuthenticationHandler still adds legacy Pilot/Admin role claims.

| API | Role still in source | Required follow-up |
|---|---|---|
| POST /api/uavs; GET /api/uavs/myUAV | Pilot | Replace canonical operator role; POST ownership policy must also match SRS Manager-create / Operator-view rules |
| GET /api/admin/uavs/pending; GET /api/admin/uavs/{id}; PUT /api/admin/uavs/{id}/reject, /activate, /deactivate | Admin,SystemAdmin | Canonical manager authorization; route name admin may remain for compatibility |
| GET /api/geofences; GET /api/geofences/{id} | SystemAdmin,Admin,Pilot | Canonical manager/operator read access |
| POST /api/geofences; PUT/DELETE /api/geofences/{id} | SystemAdmin,Admin | Canonical manager mutation access |
| POST /api/missions; GET /api/missions/{id} | Pilot | Canonical role and record scope; detail GET is an empty Ok() stub |
| POST /api/mission-suggestions; GET /mine; GET /{id} | Pilot | Canonical operator role and scope |
| GET /api/pilot-registrations; GET /{id}; POST /{id}/approve, /reject | Admin,SystemAdmin | Canonical manager role |
| GET /api/users/pilot-profile | Pilot | Canonical operator role |
| /api/flight-history and /analytics | IsInRole(Pilot/Admin/OperationsManager) | Update scope checks; otherwise new-role operators can bypass operator-only filtering |
| /api/flight-history/{missionId}/report, /telemetry, /incidents, /finalize | Authorize only | Verify record-level scope and mutation permissions; controller does not provide explicit per-record checks |
| /api/accounts and /{id}/access | AccountRoles.Manager = operations_manager | Align manager canonical spelling; update account provisioning SQL |
| /api/auth/session, /google, /me, /sessions, /logout* | Legacy database-role mappings | Align account/session DTOs, SQL role filters, authentication scheme and frontend session guards |
| /api/users/profile | SQL maps Pilot/OperationsManager | Align profile SQL mappings with renamed database roles |

Additional backend blockers:
- PostgresAuthStore selects only database roles Pilot/OperationsManager, maps them to web roles and provisions/selects Pilot. Merely renaming database Role.Name will break session lookup/provisioning.
- PilotRegistrationService approval still creates Pilot; AuthService registration still selects Pilot and emits raw database-role JWT claims.
- database/migrations/001_auth.sql still inserts Pilot/OperationsManager. Provide a coordinated migration retaining ownership/history.
- Current Program.cs registers JWT Bearer and lacks AddDroneOpsAuth, UseAuthErrorHandling and UseAuthCsrfProtection setup; live runtime nevertheless exposes functional session endpoints. Source and running binary differ; restart/rebuild only after source configuration is reconciled.
- Registration list still omits PilotLicenseNo mapping. Approval persists Approved before sending SMTP email and may report failure after committing approval.
- Pilot approval creates legacy User/Pilot records but does not set WebAccessProvisioned or provision Firebase access; validate how an approved applicant becomes a usable web-session account.

## Frontend wiring already present

- Session-cookie API client and CSRF mutation handling, accounts, user profile, UAV create/list/admin status actions, mission create, flight history/analytics, AI assessment and registration review clients.
- KAN-27 geofence list/detail/create/update/delete is wired to the existing backend routes, with map editor and Manager-only mutation controls. Runtime permission checks remain required.
- Proxy forwards query parameters; role guards currently expect operations_manager, not operation_manager. Keep this until the canonical backend contract is consistently migrated.
- Mission registry currently falls back to flight-history and hardcodes latestVersion=1; it is not a complete planning registry.
- License documents are browser IndexedDB evidence, not server uploads.

## APIs not implemented in current source/live Swagger

These are proposed contracts, not existing backend endpoints:
- KAN-33: GET /api/missions; real GET /api/missions/{id}; PUT /api/missions/{id}.
- KAN-34: GET /api/missions/{id}/versions; POST /submit; GET /validation; POST /cancel.
- KAN-35: GET /api/missions/pending-approvals; POST /{id}/approve, /reject.
- KAN-29: POST /api/integrations/telemetry/samples; GET /api/live/missions; GET /api/missions/{id}/telemetry/latest.
- KAN-36: GET /api/live/stream; GET /api/incidents; GET /api/incidents/{id}.
- KAN-37: POST /api/incidents/{id}/acknowledge, /resolve; GET /api/alerts, with detector/persistent alert handling.
- KAN-31: GET /api/missions/{id}/weather; POST /api/missions/{id}/weather/refresh.
- UAV edit and complete managed fleet registry are missing for SRS management actions.
- Server license-document upload/read contracts are missing.

## Next integration gate

Agree on operation_manager vs operations_manager, migrate database/auth/services/controllers together, keep cookie-session support compatible with the FE proxy, rebuild the running backend from this checkout, and verify two authenticated users across allowed/denied scopes. Do not claim readiness based only on Swagger or a role display label.
