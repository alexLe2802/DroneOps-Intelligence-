# Frontend–backend API audit

Verified against the development Swagger document at `/swagger/v1/swagger.json` on 2026-10-12.

| Backend capability | API | Frontend coverage | Current session-role compatibility |
|---|---|---|---|
| Firebase session and CSRF | `/api/auth/*` | Login, logout, session management | Compatible |
| Account provisioning | `/api/accounts*` | Operations Manager operator roster | Compatible |
| User profile | `/api/users/profile` | Profile view/edit | Compatible |
| AI assessment | `/api/ai/assessments*` | Mission selector, generation and history | Compatible |
| Flight history and analytics | `/api/flight-history`, `/analytics` | Dashboard and Flight History screen | Compatible (`[Authorize]`) |
| Post-flight report, telemetry, incidents | `/api/flight-history/{missionId}/*` | API is proxied; detail UI remains pending | Compatible (`[Authorize]`) |
| Geofences | `/api/geofences*` | Read-only Geofences screen | **Blocked:** controller expects `Pilot/Admin/SystemAdmin`; web session emits `uav_operator/operations_manager` |
| Pilot UAV registry | `/api/uavs`, `/api/uavs/myUAV` | List, detail and create contract connected | **Blocked:** controller expects `Pilot`; web session emits `uav_operator` |
| Admin UAV workflow | `/api/admin/uavs*` | Edit/Deactivate controls retained as pending SRS actions | **Blocked:** controller expects `Admin/SystemAdmin`; SRS assigns these actions to Operations Manager |
| Missions | `/api/missions*` | Create mission connected; registry uses Flight History | **Blocked:** controller expects `Pilot`; web session emits `uav_operator` |
| Mission suggestions | `/api/mission-suggestions*` | API proxied; dedicated UI remains pending | **Blocked:** controller expects `Pilot`; web session emits `uav_operator` |
| Pilot registrations | `/api/pilot-registrations*` | Existing account provisioning flow uses `/api/accounts` | Admin API role does not match the web Operations Manager role |

## SRS decision for UAV actions

SRS section 3.1.3 and UC-02 assign UAV list/create/edit/deactivate to the **Operations Manager**. The Operator/Pilot has view-only access to assigned UAVs. The required removal behavior is **deactivation**, not hard deletion, because historical mission references must remain intact.

The frontend therefore keeps Edit and Deactivate controls as explicitly pending Operations Manager actions. They do not mutate local data. Backend work should add/update the Operations Manager authorization and implement the missing edit endpoint while preserving mission history.
