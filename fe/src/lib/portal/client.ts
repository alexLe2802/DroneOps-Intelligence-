import { api } from "@/lib/auth/client";
import type { AiAssessment, AiAssessmentRequest, AssignedUav, CreateMissionRequest, CreateUavRequest, FlightHistoryItem, FlightTelemetryPoint, Geofence, GeofenceRequest, MissionDetail, OperationalAnalytics, PagedResponse, PilotMission, UpdateProfileRequest, UserProfile } from "./types";

export async function getPilotMissions(): Promise<PilotMission[]> {
  const page = await getFlightHistory({ pageSize: 100 });
  return page.items.map(item => ({
    id: item.missionId, name: item.missionName, description: null,
    status: item.status as PilotMission["status"], uavId: item.uavId,
    uavCode: item.uavCode || null, startTime: item.startTime, endTime: item.endTime,
    createdAt: item.startTime ?? item.endTime ?? "", latestVersion: 1,
  }));
}

export async function createMission(request: CreateMissionRequest): Promise<MissionDetail> {
  const result = await api<Omit<MissionDetail, "uavCode" | "latestVersion">>("/missions", { method: "POST", body: JSON.stringify(request) });
  return { ...result, uavCode: null, latestVersion: 1 };
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

export async function getPendingUavs(keyword = ""): Promise<AssignedUav[]> {
  const params = new URLSearchParams({ page: "1", pageSize: "100" });
  if (keyword.trim()) params.set("keyword", keyword.trim());
  return api<AssignedUav[]>(`/admin/uavs/pending?${params}`);
}

export function getManagedUav(id: string): Promise<AssignedUav> {
  return api<AssignedUav>(`/admin/uavs/${encodeURIComponent(id)}`);
}

export function setManagedUavStatus(id: string, action: "activate" | "reject" | "deactivate"): Promise<AssignedUav> {
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
