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

export type WaypointAction = "FlyThrough" | "Hover" | "Photo" | "Land";
export type MissionWaypoint = {
  sequenceOrder: number;
  latitude: number;
  longitude: number;
  altitude: number;
  actionType: WaypointAction;
};
export type CreateMissionRequest = {
  uavId: string;
  name: string;
  description: string | null;
  startTime: string;
  endTime: string;
  waypoints: MissionWaypoint[];
};
export type MissionDetail = PilotMission & { waypoints: MissionWaypoint[] };

export type UavStatus = "Available" | "Assigned" | "InFlight" | "Maintenance" | "Offline";
export type UavApprovalStatus = "Approved" | "PendingCreate" | "PendingUpdate";

export type AssignedUav = {
  id: string;
  code: string;
  name: string;
  model: string | null;
  status: UavStatus;
  batteryPercent: number | null;
  lastTelemetryAt: string | null;
  assignedMissionId: string | null;
  createdAt: string;
  approvalStatus: UavApprovalStatus;
};

// Mirrors the writable fields on DroneOps.Domain.Entities.UAV.
export type CreateUavRequest = { code: string; name: string; model: string | null };
export type UpdateUavRequest = CreateUavRequest;

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

export type AiAssessmentRequest = {
  missionRef: string;
  missionName: string;
  missionVersion: number;
  missionStatus: string;
  uavCode: string | null;
  startTime: string | null;
  endTime: string | null;
  waypoints: Array<{ latitude: number; longitude: number; altitude: number; action: string }>;
  validationSummary: string;
  incidents: string[];
  telemetrySummary: string | null;
};
export type AiRisk = { severity: "Low" | "Moderate" | "High" | "Critical"; title: string; evidence: string; precaution: string };
export type AiAssessment = {
  id: string; missionRef: string; missionVersion: number; provider: string; model: string;
  status: "Pending" | "Available" | "Failed";
  result: { summary: string; risks: AiRisk[]; precautions: string[]; missingData: string[]; disclaimer: string } | null;
  errorCode: string | null; createdAt: string; completedAt: string | null;
};

export type PortalRole = AccountRole;
