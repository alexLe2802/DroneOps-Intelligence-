import { api, ApiError } from "@/lib/auth/client";
import { mockAssignedUavs, mockPilotMissions } from "./mock-data";
import type { AssignedUav, CreateMissionRequest, CreateUavRequest, MissionDetail, PilotMission, UpdateProfileRequest, UpdateUavRequest, UserProfile } from "./types";

const mockDelay = 350;
const wait = () => new Promise(resolve => window.setTimeout(resolve, mockDelay));

const missionStoreKey = "droneops.mock.pilot-missions.v1";
function readMissions(): PilotMission[] {
  const stored = window.localStorage.getItem(missionStoreKey);
  if (!stored) return structuredClone(mockPilotMissions);
  try { return JSON.parse(stored) as PilotMission[]; }
  catch { return structuredClone(mockPilotMissions); }
}
function writeMissions(items: PilotMission[]) { window.localStorage.setItem(missionStoreKey, JSON.stringify(items)); }

// Replace these functions with /missions calls when the backend contract is available.
export async function getPilotMissions(): Promise<PilotMission[]> {
  await wait();
  return readMissions();
}

export async function createMission(request: CreateMissionRequest): Promise<MissionDetail> {
  await wait();
  const aircraft = readUavs().find(item => item.id === request.uavId);
  if (!aircraft) throw new ApiError("The selected UAV was not found.", 404);
  if (aircraft.approvalStatus !== "Approved") throw new ApiError("The selected UAV is waiting for admin approval.", 409);
  const created: MissionDetail = {
    id: crypto.randomUUID(), name: request.name, description: request.description,
    status: "Draft", uavId: request.uavId, uavCode: aircraft.code,
    startTime: request.startTime, endTime: request.endTime,
    createdAt: new Date().toISOString(), latestVersion: 1,
    waypoints: request.waypoints,
  };
  writeMissions([created, ...readMissions()]);
  return created;
}

const uavStoreKey = "droneops.mock.pilot-uavs.v1";
function readUavs(): AssignedUav[] {
  const stored = window.localStorage.getItem(uavStoreKey);
  if (!stored) return structuredClone(mockAssignedUavs);
  try { return JSON.parse(stored) as AssignedUav[]; }
  catch { return structuredClone(mockAssignedUavs); }
}
function writeUavs(items: AssignedUav[]) { window.localStorage.setItem(uavStoreKey, JSON.stringify(items)); }

export async function getAssignedUavs(): Promise<AssignedUav[]> {
  await wait();
  return readUavs();
}

export async function getAssignedUav(id: string): Promise<AssignedUav | null> {
  await wait();
  return readUavs().find(item => item.id === id) ?? null;
}

export async function createUav(request: CreateUavRequest): Promise<AssignedUav> {
  await wait();
  const items = readUavs();
  if (items.some(item => item.code.toLowerCase() === request.code.toLowerCase())) throw new ApiError("A UAV with this code already exists.", 409);
  const created: AssignedUav = { id: crypto.randomUUID(), ...request, status: "Offline", batteryPercent: null, lastTelemetryAt: null, assignedMissionId: null, createdAt: new Date().toISOString(), approvalStatus: "PendingCreate" };
  writeUavs([created, ...items]);
  return created;
}

export async function updateUav(id: string, request: UpdateUavRequest): Promise<AssignedUav> {
  await wait();
  const items = readUavs();
  const index = items.findIndex(item => item.id === id);
  if (index < 0) throw new ApiError("The requested UAV was not found.", 404);
  if (items.some(item => item.id !== id && item.code.toLowerCase() === request.code.toLowerCase())) throw new ApiError("A UAV with this code already exists.", 409);
  const updated = { ...items[index], ...request, approvalStatus: "PendingUpdate" as const };
  items[index] = updated; writeUavs(items);
  return updated;
}

export async function deleteUav(id: string): Promise<void> {
  await wait();
  const items = readUavs();
  if (!items.some(item => item.id === id)) throw new ApiError("The requested UAV was not found.", 404);
  writeUavs(items.filter(item => item.id !== id));
}

export function getProfile(): Promise<UserProfile> {
  return api<UserProfile>("/users/profile");
}

export function updateProfile(request: UpdateProfileRequest): Promise<UserProfile> {
  return api<UserProfile>("/users/profile", { method: "PUT", body: JSON.stringify(request) });
}
