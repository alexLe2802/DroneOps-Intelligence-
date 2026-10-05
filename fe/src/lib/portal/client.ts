import { api } from "@/lib/auth/client";
import { mockAssignedUavs, mockPilotMissions } from "./mock-data";
import type { AssignedUav, PilotMission, UpdateProfileRequest, UserProfile } from "./types";

const mockDelay = 350;
const wait = () => new Promise(resolve => window.setTimeout(resolve, mockDelay));

// Replace these two functions with /missions and /uavs calls when those backend contracts are available.
export async function getPilotMissions(): Promise<PilotMission[]> {
  await wait();
  return mockPilotMissions;
}

export async function getAssignedUavs(): Promise<AssignedUav[]> {
  await wait();
  return mockAssignedUavs;
}

export function getProfile(): Promise<UserProfile> {
  return api<UserProfile>("/users/profile");
}

export function updateProfile(request: UpdateProfileRequest): Promise<UserProfile> {
  return api<UserProfile>("/users/profile", { method: "PUT", body: JSON.stringify(request) });
}
