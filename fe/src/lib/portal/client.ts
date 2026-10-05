import { api, ApiError } from "@/lib/auth/client";
import { mockAssignedUavs, mockPilotMissions } from "./mock-data";
import type { AssignedUav, CreateUavRequest, PilotMission, UpdateProfileRequest, UpdateUavRequest, UserProfile } from "./types";

const mockDelay = 350;
const wait = () => new Promise(resolve => window.setTimeout(resolve, mockDelay));

// Replace these two functions with /missions and /uavs calls when those backend contracts are available.
export async function getPilotMissions(): Promise<PilotMission[]> {
  await wait();
  return mockPilotMissions;
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
