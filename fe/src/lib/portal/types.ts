import type { AccountRole } from "@/lib/auth/types";

export type MissionStatus = "Draft" | "Submitted" | "Pending" | "PendingApproval" | "Pending Review" | "Approved" | "InProgress" | "Completed" | "Rejected" | "Cancelled";

export type PilotMission = {
  id: string;
  createdBy?: string;
  name: string;
  description: string | null;
  status: MissionStatus;
  uavId: string | null;
  uavCode: string | null;
  startTime: string | null;
  endTime: string | null;
  createdAt: string;
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
  altitude: number | null;
  actionType: WaypointAction;
};
export type CreateMissionRequest = {
  operationId?: string;
  altitudeReference?: "AGL" | "MSL" | "Relative" | null;
  uavId: string;
  name: string;
  description: string | null;
  startTime: string;
  endTime: string;
  waypoints: MissionWaypoint[];
};
export type MissionUpdateSummary = { id: string; status: string; isInitial: boolean; createdAt: string; actorId: string; reviewedBy?: string | null; reviewedAt?: string | null; reason?: string | null; basePlan?: CreateMissionRequest | null; proposedPlan: CreateMissionRequest; validation?: MissionValidationResult | null };
export type MissionDetail = PilotMission & { altitudeReference?: string | null; pendingUpdate?: MissionUpdateSummary | null; waypoints: MissionWaypoint[]; detailAvailable?: boolean; validation?: MissionValidationResult | null; flightReadiness?: string; preflightValidation?: MissionValidationResult | null };
export type ManagerMission = MissionDetail & {
  createdBy: string;
  uavName: string | null;
  uavStatus: string | null;
  pilotName: string | null;
};
export type MissionValidationResult = {
  id?: string; updateRequestId?: string | null; restrictionRevision?: string; stale?: boolean; incomplete?: boolean; findings?: { geofenceId: string; revision: number; rule: string; waypointSequence?: number; segmentFrom?: number; segmentTo?: number; reason: string }[]; missionId?: string | null; stage?: string; checkedAt?: string;
  isValid: boolean; message: string; status?: string; issues?: string[]; warnings?: string[];
  weather?: MissionWeather | null;
};

export type MissionWeather = {
  missionId?: string; updateRequestId?: string | null; status: "Available" | "Partial" | "Unavailable" | "Stale"; provider: string; available: boolean; fetchedAt: string; generatedAt: string; expiresAt: string; flightStartTime?: string | null; flightEndTime?: string | null; overallRisk: string; warnings: string[];
  maxWindMetersPerSecond: number | null; maxGustMetersPerSecond: number | null; maxRainMillimetersPerHour: number | null; thunderstormForecast: boolean;
  waypoints: { sequenceOrder: number; latitude: number; longitude: number; altitudeMeters: number | null; estimatedArrivalTime: string | null; arrivalTimeReason: string | null; forecastTime: string | null; sourceFetchedAt: string | null; temperatureCelsius: number | null; relativeHumidityPercent: number | null; windSpeedMetersPerSecond: number | null; windDirectionDegrees: number | null; windGustMetersPerSecond: number | null; windReferenceHeightMeters: number; precipitationProbabilityPercent: number | null; precipitationMillimeters: number | null; visibilityMeters: number | null; cloudCoverPercent: number | null; weatherCode: number | null; status: string; riskLevel: string; warnings: string[] }[];
};

export type LiveMission = {
  missionId: string;
  missionName: string;
  missionStatus: string;
  startTime: string | null;
  uavId: string | null;
  uavCode: string | null;
  uavName: string | null;
  pilotId: string;
  pilotName: string | null;
  latitude: number | null;
  longitude: number | null;
  altitudeMeters: number | null;
  groundSpeedMetersPerSecond: number | null;
  batteryPercent: number | null;
  flightMode: string | null;
  armed: boolean;
  missionSequence: number | null;
  lastSeen: string | null;
  connectivityStatus: string;
  dataAgeSeconds: number | null;
  waypointTotal: number;
  progressPercent: number | null;
};

export type MissionTelemetry = {
  missionId: string;
  uavId: string | null;
  uavCode: string | null;
  uavName: string | null;
  sourceId: string | null;
  latitude: number | null;
  longitude: number | null;
  altitudeMeters: number | null;
  altitudeReference: string | null;
  groundSpeedMetersPerSecond: number | null;
  batteryPercent: number | null;
  flightMode: string | null;
  armed: boolean;
  missionSequence: number | null;
  sourceTimestamp: string | null;
  receivedAt: string | null;
  connectivityStatus: string;
  dataAgeSeconds: number | null;
  isStale: boolean;
  isValid: boolean;
  snapshotKind: string;
  waypointTotal: number;
  progressPercent: number | null;
};

export type IncidentItem = {
  id: string;
  missionId: string;
  missionName: string;
  uavId: string | null;
  uavCode: string | null;
  uavName: string | null;
  title: string;
  description: string | null;
  type: string;
  severity: string;
  status: string;
  latitude: number | null;
  longitude: number | null;
  createdAt: string;
  acknowledgedAt: string | null;
  resolvedAt: string | null;
};

export type IncidentDetail = IncidentItem & {
  evidence: unknown;
  acknowledgedBy: string | null;
  acknowledgedByName: string | null;
  resolvedBy: string | null;
  resolvedByName: string | null;
  resolutionNotes: string | null;
  sourceTimestamp: string | null;
  receivedAt: string | null;
  appliedRule: string | null;
  threshold: number | null;
};

export type PagedIncidents = {
  items: IncidentItem[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
};

export type PendingMissionApproval = {
  updateRequestId: string;
  currentPlan: CreateMissionRequest;
  proposedPlan: CreateMissionRequest;
  missionId: string;
  missionName: string;
  description: string | null;
  status: string;
  createdAt: string;
  startTime: string | null;
  endTime: string | null;
  pilotId: string;
  pilotName: string | null;
  pilotEmail: string | null;
  uavId: string | null;
  uavCode: string | null;
  uavName: string | null;
  waypointCount: number;
  validationStatus: string;
  validation?: MissionValidationResult | null;
  validationIssues?: string[];
};

export type PagedPendingMissionApprovals = {
  items: PendingMissionApproval[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
};

export type MissionDecision = {
  missionId: string;
  decision: "Approved" | "Rejected" | string;
  previousStatus: string;
  newStatus: string;
  comment: string | null;
  decidedAt: string;
  decidedBy: string;
};

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
  reportStatus?: string; reportRevision: number | null; telemetryCoverageComplete?: boolean;
};
export type OperationalAnalytics = {
  totalFlights: number; completedFlights: number; abortedOrFailedFlights: number;
  totalFlightHours: number; totalDistanceKilometers: number; totalIncidentsRecorded: number;
  successRatePercentage: number | null; reportsAvailable: number; reportsExcluded: number;
  reportCoveragePercentage: number | null; fromDate: string | null; toDate: string | null;
  uavId: string | null; missionId: string | null;
  completionTrend: Array<{ date: string; totalFlights: number; completedFlights: number; failedOrAbortedFlights: number }>;
  incidentSeverityBreakdown: Record<string, number>;
  excludedReportReasons: Record<string, number>;
};
export type FlightTelemetryPoint = {
  latitude: number | null; longitude: number | null; altitude: number | null;
  speed: number | null; batteryLevel: number | null; recordedAt: string;
};
export type PostFlightReport = {
  reportId: string; missionId: string; missionName: string; missionDescription: string | null;
  status: string; generationStatus: string; revision: number; isCurrent: boolean;
  coveredTelemetryThrough: string | null; generatedAt: string | null; failureReason: string | null;
  generationReason: string | null; uavName: string; uavCode: string; pilotName: string;
  startTime: string | null; endTime: string | null; totalFlightDurationMinutes: number;
  maxAltitude: number | null; avgAltitude: number | null; maxSpeed: number | null; avgSpeed: number | null;
  initialBatteryLevel: number | null; finalBatteryLevel: number | null; batteryConsumedPercentage: number | null;
  totalDistanceMeters: number | null; totalTelemetryRecords: number; totalIncidents: number;
  safetyEvaluation: string | null; summary: string | null; notes: string | null; createdAt: string;
};
export type PostFlightIncident = {
  id: string; missionId: string; title: string; description: string | null; severity: string | null;
  type: string; status: string; latitude: number | null; longitude: number | null;
  createdAt: string; acknowledgedAt: string | null; resolvedAt: string | null; resolutionNotes: string | null;
};
export type GeofenceCoordinate = { latitude: number; longitude: number };
export type GeofenceRequest = {
  altitudeReference?: "AGL" | "MSL" | "Relative" | null; minAltitudeMeters?: number | null; maxAltitudeMeters?: number | null; name: string; description: string | null; coordinates: GeofenceCoordinate[] };
export type Geofence = {
  verticalRule?: "Unspecified" | "AllAltitudes" | "Band";
  altitudeReference?: string | null; minAltitudeMeters?: number | null; maxAltitudeMeters?: number | null; revision?: number; id: string; name: string; description: string | null; coordinates: GeofenceCoordinate[]; createdAt: string };
export type SaveGeofenceRequest = { verticalRule?: "Unspecified" | "AllAltitudes" | "Band"; altitudeReference?: "AGL" | "MSL" | "Relative" | null; minAltitudeMeters?: number | null; maxAltitudeMeters?: number | null; name: string; description: string | null; coordinates: GeofenceCoordinate[] };

// Matches DroneOps.Application.DTOs.Users.UserProfileResponse.
export type UserProfile = {
  avatarUrl: string | null;
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

export type PilotProfile = {
  userId: string; fullName: string; email: string; createdAt: string;
  pilotId: string; phoneNumber: string | null; dateOfBirth: string | null;
  pilotLicenseNo: string; licenseIssuedDate: string | null;
  licenseExpiredDate: string | null; experienceYears: number;
  droneType: string | null; droneModel: string | null;
};
