import type { AccountRole } from "@/lib/auth/types";

export type MissionStatus = "Draft" | "PendingApproval" | "Approved" | "InProgress" | "Completed" | "Rejected";

export type PilotMission = {
  id: string;
  name: string;
  description: string | null;
  status: MissionStatus;
  uavId: string | null;
  uavCode: string | null;
  startTime: string | null;
  endTime: string | null;
  createdAt: string;
  latestVersion: number;
};

export type UavStatus = "Available" | "Assigned" | "InFlight" | "Maintenance" | "Offline";

export type AssignedUav = {
  id: string;
  code: string;
  name: string;
  model: string | null;
  status: UavStatus;
  batteryPercent: number | null;
  lastTelemetryAt: string | null;
  assignedMissionId: string | null;
};

// Matches DroneOps.Application.DTOs.Users.UserProfileResponse.
export type UserProfile = {
  id: string;
  fullName: string;
  email: string;
  role: string;
  createdAt: string;
};

// Matches DroneOps.Application.DTOs.Users.UpdateProfileRequest.
export type UpdateProfileRequest = { fullName: string };

export type PortalRole = AccountRole;
