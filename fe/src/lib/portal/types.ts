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

export type PagedResponse<T> = {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
};

export type WaypointAction = "TakeOff" | "FlyThrough" | "Hover" | "CapturePhoto" | "Land";
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

export type UavStatus = "Pending" | "Active" | "Rejected" | "Inactive" | string;

export type AssignedUav = {
  id: string;
  code: string;
  name: string;
  model: string | null;
  status: UavStatus;
  pilotId: string;
  pilotName: string;
  createdAt: string;
};

// Mirrors the writable fields on DroneOps.Domain.Entities.UAV.
export type CreateUavRequest = { code: string; name: string; model: string | null };
export type UpdateUavRequest = CreateUavRequest;

export type FlightHistoryItem = {
  missionId: string; missionName: string; status: string; uavId: string | null;
  uavName: string; uavCode: string; pilotName: string; startTime: string | null;
  endTime: string | null; durationMinutes: number; totalIncidents: number; hasReport: boolean;
};
export type OperationalAnalytics = {
  totalFlights: number; completedFlights: number; abortedOrFailedFlights: number;
  totalFlightHours: number; totalDistanceKilometers: number; totalIncidentsRecorded: number;
  successRatePercentage: number;
};
export type FlightTelemetryPoint = {
  latitude: number | null; longitude: number | null; altitude: number | null;
  speed: number | null; batteryLevel: number | null; recordedAt: string;
};
export type GeofenceCoordinate = { latitude: number; longitude: number };
export type Geofence = { id: string; name: string; description: string | null; coordinates: GeofenceCoordinate[]; createdAt: string };
export type SaveGeofenceRequest = { name: string; description: string | null; coordinates: GeofenceCoordinate[] };

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
