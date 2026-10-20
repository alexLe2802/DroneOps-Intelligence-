import { api, ApiError } from "@/lib/auth/client";
import type { AiAssessment, AiAssessmentRequest, AssignedUav, CreateMissionRequest, CreateUavRequest, FlightHistoryItem, FlightTelemetryPoint, Geofence, GeofenceRequest, LiveMission, MissionDecision, MissionDetail, MissionTelemetry, MissionValidationResult, OperationalAnalytics, PagedIncidents, PagedPendingMissionApprovals, PagedResponse, PilotProfile, UpdateProfileRequest, UserProfile } from "./types";

type MissionApiRecord = Omit<MissionDetail, "uavCode" | "latestVersion" | "detailAvailable">;

export async function getPilotMissions(): Promise<MissionDetail[]> {
  const [missions, aircraft] = await Promise.all([
    api<MissionApiRecord[]>("/missions/my"),
    getAssignedUavs().catch(() => [] as AssignedUav[]),
  ]);
  const uavCodes = new Map(aircraft.map(item => [item.id, item.code]));
  return missions.map(mission => ({
    ...mission,
    uavCode: mission.uavId ? uavCodes.get(mission.uavId) ?? null : null,
    latestVersion: 1,
    detailAvailable: true,
    waypoints: Array.isArray(mission.waypoints) ? mission.waypoints.map((point, index) => ({
      sequenceOrder: point.sequenceOrder ?? index + 1,
      latitude: Number(point.latitude),
      longitude: Number(point.longitude),
      altitude: Number(point.altitude ?? 0),
      actionType: point.actionType ?? "FlyThrough",
    })) : [],
  }));
}

export async function createMission(request: CreateMissionRequest): Promise<MissionDetail> {
  const result = await api<Omit<MissionDetail, "uavCode" | "latestVersion">>("/missions", { method: "POST", body: JSON.stringify(request) });
  return { ...result, uavCode: null, latestVersion: 1 };
}

export async function updateMission(id: string, request: CreateMissionRequest): Promise<MissionDetail> {
  const result = await api<Omit<MissionDetail, "uavCode" | "latestVersion">>(`/missions/${encodeURIComponent(id)}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
  return { ...result, uavCode: null, latestVersion: 1, detailAvailable: true };
}

export function validateMission(mission: MissionDetail): Promise<MissionValidationResult> {
  return api<MissionValidationResult>("/missions/validate", {
    method: "POST",
    body: JSON.stringify({
      missionId: mission.id,
      waypoints: mission.waypoints.map(point => ({
        latitude: point.latitude,
        longitude: point.longitude,
        altitude: point.altitude,
      })),
    }),
  });
}

export function submitMission(id: string): Promise<{ message: string }> {
  return api<{ message: string }>(`/missions/${encodeURIComponent(id)}/submit`, { method: "PUT" });
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

export function getLatestMissionTelemetry(id: string): Promise<MissionTelemetry> {
  return api<MissionTelemetry>(`/missions/${encodeURIComponent(id)}/telemetry/latest`);
}

export function getMissionIncidents(missionId: string, pageSize = 8): Promise<PagedIncidents> {
  const params = new URLSearchParams({ missionId, pageNumber: "1", pageSize: String(pageSize) });
  return api<PagedIncidents>(`/incidents?${params}`);
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

export function approveMission(missionId: string, comment: string): Promise<MissionDecision> {
  return api<MissionDecision>(`/missions/${encodeURIComponent(missionId)}/approve`, {
    method: "POST",
    body: JSON.stringify({ comment: comment.trim() || null, expectedStatus: "Pending Review" }),
  });
}

export function rejectMission(missionId: string, reason: string): Promise<MissionDecision> {
  return api<MissionDecision>(`/missions/${encodeURIComponent(missionId)}/reject`, {
    method: "POST",
    body: JSON.stringify({ reason: reason.trim(), expectedStatus: "Pending Review" }),
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
    id: result.id,
    name: result.name,
    description: result.description ?? null,
    status: result.status,
    uavId: result.uavId ?? null,
    uavCode: result.uavCode ?? null,
    startTime: result.startTime ?? null,
    endTime: result.endTime ?? null,
    createdAt: result.createdAt ?? "",
    latestVersion: result.latestVersion ?? 1,
    detailAvailable: true,
    waypoints: result.waypoints.map((point, index) => ({
      sequenceOrder: point.sequenceOrder ?? index + 1,
      latitude: Number(point.latitude),
      longitude: Number(point.longitude),
      altitude: Number(point.altitude ?? 0),
      actionType: point.actionType ?? "FlyThrough",
    })),
  };
}

export async function getAssignedUavs(): Promise<AssignedUav[]> {
  return api<AssignedUav[]>("/uavs/myUAV");
}

export async function getAssignedUav(id: string): Promise<AssignedUav | null> {
  const aircraft = await getAssignedUavs();
  return aircraft.find(item => item.id === id) ?? null;
}

export async function createUav(request: CreateUavRequest): Promise<AssignedUav> {
  return api<AssignedUav>("/uavs", { method: "POST", body: JSON.stringify(request) });
}

// PUT uses the backend's update DTO, which differs from the create DTO.
export async function updateAssignedUav(id: string, request: CreateUavRequest): Promise<void> {
  await api(`/uavs/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify({ uavCode: request.code, uavName: request.name, description: request.model }) });
}

export async function deleteAssignedUav(id: string): Promise<void> {
  await api(`/uavs/${encodeURIComponent(id)}`, { method: "DELETE" });
}

export function getManagedUavs(filters: { pageNumber?: number; keyword?: string; status?: string } = {}): Promise<PagedResponse<AssignedUav>> {
  const params = new URLSearchParams({ PageNumber: String(filters.pageNumber ?? 1), PageSize: "20" });
  if (filters.keyword?.trim()) params.set("Keyword", filters.keyword.trim());
  if (filters.status) params.set("Status", filters.status);
  return api<PagedResponse<AssignedUav>>(`/admin/uavs?${params}`);
}

export async function getPendingUavs(keyword = ""): Promise<AssignedUav[]> {
  const params = new URLSearchParams({ page: "1", pageSize: "100" });
  if (keyword.trim()) params.set("keyword", keyword.trim());
  return api<AssignedUav[]>(`/admin/uavs/pending?${params}`);
}

export function getManagedUav(id: string): Promise<AssignedUav> {
  return api<AssignedUav>(`/admin/uavs/${encodeURIComponent(id)}`);
}

export function setManagedUavStatus(id: string, action: "approve" | "activate" | "reject" | "deactivate"): Promise<AssignedUav> {
  return api<AssignedUav>(`/admin/uavs/${encodeURIComponent(id)}/${action}`, { method: "PUT" });
}

export function getFlightHistory(filters: { pageNumber?: number; pageSize?: number; search?: string; status?: string } = {}): Promise<PagedResponse<FlightHistoryItem>> {
  const params = new URLSearchParams();
  params.set("pageNumber", String(filters.pageNumber ?? 1));
  params.set("pageSize", String(filters.pageSize ?? 20));
  if (filters.search) params.set("search", filters.search);
  if (filters.status) params.set("status", filters.status);
  return api<PagedResponse<FlightHistoryItem>>(`/flight-history?${params}`);
}

export function getOperationalAnalytics(): Promise<OperationalAnalytics> {
  return api<OperationalAnalytics>("/flight-history/analytics");
}

export function getFlightTelemetry(missionId: string): Promise<FlightTelemetryPoint[]> {
  return api<FlightTelemetryPoint[]>(`/flight-history/${encodeURIComponent(missionId)}/telemetry`);
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
