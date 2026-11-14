import { api, ApiError } from "@/lib/auth/client";
import type { AccountRole } from "@/lib/auth/types";
import type { AiAssessment, AiAssessmentRequest, AssignedUav, CreateMissionRequest, CreateUavRequest, FlightHistoryItem, FlightTelemetryPoint, Geofence, GeofenceRequest, IncidentDetail, LiveMission, ManagerMission, MissionDecision, MissionDetail, MissionTelemetry, MissionValidationResult, NotificationFeed, OperationalAnalytics, PagedIncidents, PagedPendingMissionApprovals, PagedResponse, PilotProfile, PortalNotification, PostFlightIncident, PostFlightReport, UpdateProfileRequest, UpdateUavRequest, UserProfile } from "./types";

type MissionApiRecord = Omit<MissionDetail, "uavCode" | "detailAvailable"> & { uavCode?: string | null };

function normalizeMission(mission: MissionApiRecord, uavCode: string | null): MissionDetail {
  return {
    ...mission,
    uavCode,
    averageSpeedMetersPerSecond: mission.averageSpeedMetersPerSecond == null
      ? null
      : Number(mission.averageSpeedMetersPerSecond),
    maxSpeedMetersPerSecond: mission.maxSpeedMetersPerSecond == null
      ? null
      : Number(mission.maxSpeedMetersPerSecond),
    detailAvailable: true,
    waypoints: Array.isArray(mission.waypoints) ? mission.waypoints.map((point, index) => ({
      ...point,
      sequenceOrder: point.sequenceOrder ?? index + 1,
      latitude: Number(point.latitude),
      longitude: Number(point.longitude),
      altitude: point.altitude == null ? null : Number(point.altitude),
      actionType: point.actionType ?? "FlyThrough",
    })) : [],
  };
}

export async function getPilotMissions(): Promise<MissionDetail[]> {
  const missions = await api<MissionApiRecord[]>("/missions/my");
  return missions.map(mission => normalizeMission(mission, mission.uavCode ?? null));
}

export async function getManagerMissions(): Promise<ManagerMission[]> {
  const [missions, fleet] = await Promise.all([
    api<PagedResponse<MissionApiRecord & { createdBy: string }>>("/missions?pageSize=100"),
    getManagedUavs({ pageNumber: 1, pageSize: 100 }).catch(() => null),
  ]);
  const aircraft = new Map((fleet?.items ?? []).map(item => [item.id, item]));
  return (Array.isArray(missions) ? missions as Array<MissionApiRecord & { createdBy: string }> : missions.items).map(record => {
    const assigned = record.uavId ? aircraft.get(record.uavId) : undefined;
    return {
      ...normalizeMission(record, assigned?.code ?? null),
      createdBy: record.createdBy,
      uavName: assigned?.name ?? null,
      uavStatus: assigned?.status ?? null,
      pilotName: assigned?.pilotName ?? null,
    };
  });
}

export type ValidationMissionOption = Pick<MissionDetail, "id" | "name" | "status">;

export async function getValidationMissionOptions(role: AccountRole): Promise<ValidationMissionOption[]> {
  const missions = role === "operation_manager"
    ? await getManagerMissions()
    : await getPilotMissions();
  return missions.map(({ id, name, status }) => ({ id, name, status }));
}

export async function createMission(request: CreateMissionRequest): Promise<MissionDetail> {
  const result = await api<MissionApiRecord>("/missions", { method: "POST", body: JSON.stringify(request) });
  return normalizeMission(result, result.uavCode ?? null);
}

export function submitMission(id: string, operationId: string): Promise<MissionDetail> {
  return api<MissionDetail>(`/missions/${encodeURIComponent(id)}/submit`, { method: "PUT", body: JSON.stringify({ operationId }) });
}

export function confirmHighRiskMission(id: string, updateRequestId: string, validationRunId: string): Promise<{ missionId: string; status: string; message: string }> {
  return api(`/missions/${encodeURIComponent(id)}/confirm-high-risk`, { method: "POST", body: JSON.stringify({ updateRequestId, validationRunId }) });
}

export function cancelHighRiskMission(id: string, updateRequestId: string): Promise<{ missionId: string; message: string }> {
  return api(`/missions/${encodeURIComponent(id)}/cancel-high-risk?updateRequestId=${encodeURIComponent(updateRequestId)}`, { method: "DELETE" });
}

export async function updateMission(id: string, request: CreateMissionRequest): Promise<MissionDetail> {
  const result = await api<MissionApiRecord>(`/missions/${encodeURIComponent(id)}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
  return normalizeMission(result, result.uavCode ?? null);
}

export function validateMission(mission: Pick<MissionDetail, "id">, updateRequestId?: string): Promise<MissionValidationResult> {
  return api<MissionValidationResult>("/missions/validate", {
    method: "POST",
    body: JSON.stringify({
      missionId: mission.id,
      ...(updateRequestId ? { updateRequestId } : {}),

    }),
  });
}

export function startMission(id: string): Promise<MissionValidationResult> {
  return api<MissionValidationResult>(`/missions/${encodeURIComponent(id)}/start`, { method: "POST" });
}

export function cancelMission(id: string, reason: string): Promise<{ message: string }> {
  return api<{ message: string }>(`/missions/${encodeURIComponent(id)}/cancel`, {
    method: "PUT",
    body: JSON.stringify({ reason: reason.trim() }),
  });
}

export function getLiveMissions(): Promise<LiveMission[]> {
  return api<LiveMission[]>("/live/missions");
}

export function getLatestMissionTelemetry(id: string, signal?: AbortSignal): Promise<MissionTelemetry> {
  return api<MissionTelemetry>(`/missions/${encodeURIComponent(id)}/telemetry/latest`, { signal });
}

export function getMissionIncidents(missionId: string, pageSize = 8): Promise<PagedIncidents> {
  const params = new URLSearchParams({ missionId, pageNumber: "1", pageSize: String(pageSize) });
  return api<PagedIncidents>(`/incidents?${params}`);
}

export type IncidentFilters = {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  missionId?: string;
  uavId?: string;
  type?: string;
  severity?: string;
  status?: string;
  fromDate?: string;
  toDate?: string;
};

export function getIncidents(filters: IncidentFilters = {}): Promise<PagedIncidents> {
  const params = new URLSearchParams({
    pageNumber: String(filters.pageNumber ?? 1),
    pageSize: String(filters.pageSize ?? 10),
  });
  if (filters.search?.trim()) params.set("search", filters.search.trim());
  if (filters.missionId) params.set("missionId", filters.missionId);
  if (filters.uavId) params.set("uavId", filters.uavId);
  if (filters.type) params.set("type", filters.type);
  if (filters.severity) params.set("severity", filters.severity);
  if (filters.status) params.set("status", filters.status);
  if (filters.fromDate) params.set("fromDate", filters.fromDate);
  if (filters.toDate) params.set("toDate", filters.toDate);
  return api<PagedIncidents>(`/incidents?${params}`);
}

export type IncidentTransition = { id: string; fromStatus: string; toStatus: string; eventType: string; notes: string | null; changedByName: string | null; createdAt: string };
export function getIncidentHistory(id: string): Promise<IncidentTransition[]> {
  return api<IncidentTransition[]>(`/incidents/${encodeURIComponent(id)}/history`);
}

export function getIncident(id: string): Promise<IncidentDetail> {
  return api<IncidentDetail>(`/incidents/${encodeURIComponent(id)}`);
}

export function acknowledgeIncident(id: string, expectedStatus: string, expectedRevision: number, notes?: string | null): Promise<IncidentDetail> {
  return api<IncidentDetail>(`/incidents/${encodeURIComponent(id)}/acknowledge`, {
    method: "POST",
    body: JSON.stringify({ notes: notes?.trim() || null, expectedStatus, expectedRevision }),
  });
}

export function resolveIncident(id: string, resolutionNotes: string, expectedStatus: string, expectedRevision: number): Promise<IncidentDetail> {
  return api<IncidentDetail>(`/incidents/${encodeURIComponent(id)}/resolve`, {
    method: "POST",
    body: JSON.stringify({ resolutionNotes: resolutionNotes.trim(), expectedStatus, expectedRevision }),
  });
}

export function getNotificationFeed(unreadOnly = false, limit = 50): Promise<NotificationFeed> {
  const params = new URLSearchParams({ unreadOnly: String(unreadOnly), limit: String(limit) });
  return api<NotificationFeed>(`/notifications?${params}`);
}

export function markNotificationRead(id: string): Promise<PortalNotification> {
  return api<PortalNotification>(`/notifications/${encodeURIComponent(id)}/read`, { method: "PATCH" });
}

export function markAllNotificationsRead(): Promise<{ updatedCount: number }> {
  return api<{ updatedCount: number }>("/notifications/read-all", { method: "PATCH" });
}

export function liveMissionStreamUrl(missionId: string): string {
  return `/api/live/stream?missionId=${encodeURIComponent(missionId)}`;
}

export function getPendingMissionApprovals(params: { pageNumber?: number; pageSize?: number; search?: string; fromDate?: string; toDate?: string } = {}): Promise<PagedPendingMissionApprovals> {
  const query = new URLSearchParams({
    pageNumber: String(params.pageNumber ?? 1),
    pageSize: String(params.pageSize ?? 100),
  });
  if (params.search) query.set("search", params.search);
  if (params.fromDate) query.set("fromDate", params.fromDate);
  if (params.toDate) query.set("toDate", params.toDate);
  return api<PagedPendingMissionApprovals>(`/missions/pending-approvals?${query}`);
}

export function approveMission(missionId: string, comment: string, validationRunId?: string, updateRequestId?: string): Promise<MissionDecision> {
  return api<MissionDecision>(`/missions/${encodeURIComponent(missionId)}/approve`, {
    method: "POST",
    body: JSON.stringify({ comment: comment.trim() || null, updateRequestId: updateRequestId ?? null, validationRunId: validationRunId ?? null }),
  });
}

export function rejectMission(missionId: string, reason: string, updateRequestId?: string): Promise<MissionDecision> {
  return api<MissionDecision>(`/missions/${encodeURIComponent(missionId)}/reject`, {
    method: "POST",
    body: JSON.stringify({ reason: reason.trim(), updateRequestId: updateRequestId ?? null }),
  });
}

export async function getMission(id: string): Promise<MissionDetail> {
  let result: Partial<MissionDetail> & Pick<MissionDetail, "id" | "name" | "status">;
  try {
    result = await api<Partial<MissionDetail> & Pick<MissionDetail, "id" | "name" | "status">>(`/missions/${encodeURIComponent(id)}`);
  } catch (error) {
    if (error instanceof SyntaxError || (error instanceof ApiError && (error.status === 403 || error.status === 404))) {
      const summary = (await getPilotMissions()).find(mission => mission.id === id);
      if (summary) return summary;
      throw new Error("Mission details are not available from the backend yet.");
    }
    throw error;
  }
  if (!result?.id || !result.name || !Array.isArray(result.waypoints)) {
    throw new Error("Mission details are not available from the backend yet.");
  }
  return {
    ...result,
    id: result.id,
    name: result.name,
    description: result.description ?? null,
    status: result.status,
    uavId: result.uavId ?? null,
    uavCode: result.uavCode ?? null,
    averageSpeedMetersPerSecond: result.averageSpeedMetersPerSecond == null
      ? null
      : Number(result.averageSpeedMetersPerSecond),
    maxSpeedMetersPerSecond: result.maxSpeedMetersPerSecond == null
      ? null
      : Number(result.maxSpeedMetersPerSecond),
    startTime: result.startTime ?? null,
    endTime: result.endTime ?? null,
    createdAt: result.createdAt ?? "",
    detailAvailable: true,
    waypoints: result.waypoints.map((point, index) => ({
      ...point,
      sequenceOrder: point.sequenceOrder ?? index + 1,
      latitude: Number(point.latitude),
      longitude: Number(point.longitude),
      altitude: point.altitude == null ? null : Number(point.altitude),
      actionType: point.actionType ?? "FlyThrough",
    })),
  };
}

type UavApiRecord = Partial<AssignedUav> & Pick<AssignedUav, "id" | "code" | "name" | "status">;

function normalizeUav(record: UavApiRecord): AssignedUav {
  const numberOrNull = (value: number | null | undefined) => value == null ? null : Number(value);
  return {
    ...record,
    manufacturer: record.manufacturer ?? null,
    model: record.model ?? null,
    serialNumber: record.serialNumber ?? null,
    pilotId: record.pilotId ?? "",
    pilotName: record.pilotName ?? "",
    emptyWeightKg: numberOrNull(record.emptyWeightKg),
    maxTakeoffWeightKg: numberOrNull(record.maxTakeoffWeightKg),
    maxPayloadKg: numberOrNull(record.maxPayloadKg),
    batteryType: record.batteryType ?? null,
    batteryCapacityMah: numberOrNull(record.batteryCapacityMah),
    batteryVoltage: numberOrNull(record.batteryVoltage),
    batteryCellCount: numberOrNull(record.batteryCellCount),
    maxFlightTimeMinutes: numberOrNull(record.maxFlightTimeMinutes),
    maxRangeKm: numberOrNull(record.maxRangeKm),
    maxSpeedKmh: numberOrNull(record.maxSpeedKmh),
    maxWindSpeedMps: numberOrNull(record.maxWindSpeedMps),
    maxOperatingAltitudeMeters: numberOrNull(record.maxOperatingAltitudeMeters),
    purchaseDate: record.purchaseDate ?? null,
    lastMaintenanceAt: record.lastMaintenanceAt ?? null,
    nextMaintenanceAt: record.nextMaintenanceAt ?? null,
    totalFlightHours: Number(record.totalFlightHours ?? 0),
    totalFlights: Number(record.totalFlights ?? 0),
    imageUrl: record.imageUrl ?? null,
    imageModerationStatus: record.imageModerationStatus ?? null,
    imageModeratedAt: record.imageModeratedAt ?? null,
    imageModerationReason: record.imageModerationReason ?? null,
    createdAt: record.createdAt ?? "",
  };
}

export async function getAssignedUavs(): Promise<AssignedUav[]> {
  const aircraft: AssignedUav[] = [];
  let pageNumber = 1;
  let totalPages = 1;
  do {
    const result = await api<PagedResponse<UavApiRecord> | UavApiRecord[]>(
      `/uavs/myUAV?PageNumber=${pageNumber}&PageSize=100`,
    );
    // Keep compatibility with backends that still return the original array contract.
    if (Array.isArray(result)) return result.map(normalizeUav);
    if (!result || !Array.isArray(result.items) || !Number.isInteger(result.totalPages) || result.totalPages < 0) {
      throw new Error("Assigned UAV API returned an invalid paginated response.");
    }
    aircraft.push(...result.items.map(normalizeUav));
    totalPages = result.totalPages;
    pageNumber++;
  } while (pageNumber <= totalPages);
  return aircraft;
}

export async function getAssignedUav(id: string): Promise<AssignedUav | null> {
  const aircraft = await getAssignedUavs();
  return aircraft.find(item => item.id === id) ?? null;
}

export async function createUav(request: CreateUavRequest): Promise<AssignedUav> {
  return normalizeUav(await api<UavApiRecord>("/uavs", { method: "POST", body: JSON.stringify(request) }));
}

export async function updateAssignedUav(id: string, request: UpdateUavRequest): Promise<void> {
  await api(`/uavs/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(request) });
}

export async function uploadUavImage(id: string, file: File): Promise<AssignedUav> {
  const body = new FormData();
  body.set("file", file);
  return normalizeUav(await api<UavApiRecord>(`/uavs/${encodeURIComponent(id)}/image`, {
    method: "PUT",
    body,
  }));
}

export async function deleteAssignedUav(id: string): Promise<void> {
  await api(`/uavs/${encodeURIComponent(id)}`, { method: "DELETE" });
}

export async function getManagedUavs(filters: { pageNumber?: number; pageSize?: number; keyword?: string; status?: string; imageModerationStatus?: string } = {}): Promise<PagedResponse<AssignedUav>> {
  const params = new URLSearchParams({ PageNumber: String(filters.pageNumber ?? 1), PageSize: String(filters.pageSize ?? 20) });
  if (filters.keyword?.trim()) params.set("Keyword", filters.keyword.trim());
  if (filters.status) params.set("Status", filters.status);
  if (filters.imageModerationStatus) params.set("imageModerationStatus", filters.imageModerationStatus);
  const result = await api<PagedResponse<UavApiRecord>>(`/admin/uavs?${params}`);
  return { ...result, items: result.items.map(normalizeUav) };
}

export async function getPendingUavs(keyword = ""): Promise<AssignedUav[]> {
  const params = new URLSearchParams({ page: "1", pageSize: "100" });
  if (keyword.trim()) params.set("keyword", keyword.trim());
  return (await api<UavApiRecord[]>(`/admin/uavs/pending?${params}`)).map(normalizeUav);
}

export async function getManagedUav(id: string): Promise<AssignedUav> {
  return normalizeUav(await api<UavApiRecord>(`/admin/uavs/${encodeURIComponent(id)}`));
}

export async function setManagedUavStatus(id: string, action: "approve" | "activate" | "reject" | "deactivate"): Promise<AssignedUav> {
  return normalizeUav(await api<UavApiRecord>(`/admin/uavs/${encodeURIComponent(id)}/${action}`, { method: "PUT" }));
}

export async function rejectManagedUavImage(id: string, reason: string): Promise<AssignedUav> {
  return normalizeUav(await api<UavApiRecord>(`/admin/uavs/${encodeURIComponent(id)}/image/reject`, {
    method: "POST",
    body: JSON.stringify({ reason }),
  }));
}

export type FlightHistoryFilters = { pageNumber?: number; pageSize?: number; search?: string; status?: string; uavId?: string; fromDate?: string; toDate?: string };

function postFlightUtcBoundary(value: string, endOfDay: boolean): string {
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return `${value}T${endOfDay ? "23:59:59.999" : "00:00:00.000"}Z`;
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toISOString();
}

export function getFlightHistory(filters: FlightHistoryFilters = {}): Promise<PagedResponse<FlightHistoryItem>> {
  const params = new URLSearchParams();
  params.set("pageNumber", String(filters.pageNumber ?? 1));
  params.set("pageSize", String(filters.pageSize ?? 20));
  if (filters.search) params.set("search", filters.search);
  if (filters.status) params.set("status", filters.status);
  if (filters.uavId) params.set("uavId", filters.uavId);
  if (filters.fromDate) params.set("fromDate", postFlightUtcBoundary(filters.fromDate, false));
  if (filters.toDate) params.set("toDate", postFlightUtcBoundary(filters.toDate, true));
  return api<PagedResponse<FlightHistoryItem>>(`/flight-history?${params}`);
}

export function getOperationalAnalytics(filters: Pick<FlightHistoryFilters, "uavId" | "fromDate" | "toDate"> & { missionId?: string } = {}): Promise<OperationalAnalytics> {
  const params = new URLSearchParams();
  if (filters.uavId) params.set("uavId", filters.uavId);
  if (filters.missionId) params.set("missionId", filters.missionId);
  if (filters.fromDate) params.set("fromDate", postFlightUtcBoundary(filters.fromDate, false));
  if (filters.toDate) params.set("toDate", postFlightUtcBoundary(filters.toDate, true));
  const query = params.toString();
  return api<OperationalAnalytics>(`/flight-history/analytics${query ? `?${query}` : ""}`);
}

export function getFlightTelemetry(missionId: string): Promise<FlightTelemetryPoint[]> {
  return api<FlightTelemetryPoint[]>(`/flight-history/${encodeURIComponent(missionId)}/telemetry`);
}

export function getPostFlightReport(missionId: string): Promise<PostFlightReport> {
  return api<PostFlightReport>(`/flight-history/${encodeURIComponent(missionId)}/report`);
}

export function getPostFlightReportRevisions(missionId: string): Promise<PostFlightReport[]> {
  return api<PostFlightReport[]>(`/flight-history/${encodeURIComponent(missionId)}/report/revisions`);
}

export function getPostFlightIncidents(missionId: string): Promise<PostFlightIncident[]> {
  return api<PostFlightIncident[]>(`/flight-history/${encodeURIComponent(missionId)}/incidents`);
}

export function regeneratePostFlightReport(missionId: string, reason: string): Promise<{ success: boolean; message: string; data: PostFlightReport }> {
  return api<{ success: boolean; message: string; data: PostFlightReport }>(`/flight-history/${encodeURIComponent(missionId)}/regenerate`, {
    method: "POST",
    body: JSON.stringify({ reason: reason.trim() }),
  });
}

export function getGeofences(): Promise<Geofence[]> {
  return api<Geofence[]>("/geofences");
}

export function getProfile(): Promise<UserProfile> {
  return api<UserProfile>("/users/profile");
}

export function updateProfile(request: UpdateProfileRequest): Promise<UserProfile> {
  return api<UserProfile>("/users/profile", { method: "PUT", body: JSON.stringify(request) });
}

export function createAiAssessment(request: AiAssessmentRequest): Promise<AiAssessment> {
  return api<AiAssessment>("/ai/assessments", { method: "POST", body: JSON.stringify(request) });
}

export function getAiAssessments(missionRef: string): Promise<AiAssessment[]> {
  return api<AiAssessment[]>(`/ai/assessments/mission/${encodeURIComponent(missionRef)}`);
}

export function getGeofence(id: string): Promise<Geofence> {
  return api<Geofence>(`/geofences/${encodeURIComponent(id)}`);
}
export function createGeofence(request: GeofenceRequest): Promise<Geofence> {
  return api<Geofence>("/geofences", { method: "POST", body: JSON.stringify(request) });
}
export function updateGeofence(id: string, request: GeofenceRequest): Promise<Geofence> {
  return api<Geofence>(`/geofences/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(request) });
}
export function deleteGeofence(id: string): Promise<void> {
  return api<void>(`/geofences/${encodeURIComponent(id)}`, { method: "DELETE" });
}

export function getPilotProfile(): Promise<PilotProfile> {
  return api<PilotProfile>("/users/pilot-profile");
}

export function getMissionHistory(id: string) { return api<import("./types").MissionUpdateSummary[]>(`/missions/${encodeURIComponent(id)}/history`); }
export function getMissionValidation(id: string, updateRequestId?: string) { return api<MissionValidationResult | null>(`/missions/${encodeURIComponent(id)}/validation${updateRequestId ? `?updateRequestId=${encodeURIComponent(updateRequestId)}` : ""}`); }
export function getMissionValidationRun(id: string, runId: string) { return api<MissionValidationResult>(`/missions/${encodeURIComponent(id)}/validation/runs/${encodeURIComponent(runId)}`); }
export function getLatestMissionValidation(id: string) { return api<MissionValidationResult | null>(`/missions/${encodeURIComponent(id)}/validation?latest=true`); }
export function getMissionWeather(id: string, updateRequestId?: string, refresh = false) { return api<import("./types").MissionWeather>(`/missions/${encodeURIComponent(id)}/weather${refresh ? "/refresh" : ""}${updateRequestId ? `?updateRequestId=${encodeURIComponent(updateRequestId)}` : ""}`, refresh ? { method: "POST" } : {}); }

export function getGeofenceRevisions(id: string): Promise<Array<{ id: string; revision: number; action: string; recordedAt: string; snapshot: unknown }>> {
  return api(`/geofences/${encodeURIComponent(id)}/revisions`);
}
