export type AutomaticFlightLimits = {
  takeoffSpeedMetersPerSecond: number; climbSpeedMetersPerSecond: number;
  descentSpeedMetersPerSecond: number; landingSpeedMetersPerSecond: number;
  maxAltitudeMeters: number; maxWindMetersPerSecond: number; maxGustMetersPerSecond: number;
  policyVersion: string;
  recoveryEnergyProfile?: RecoveryEnergyProfile | null;
};
export type RecoveryEnergyProfile = {
  measurementReference: string; uavId: string; batteryIdentifier: string; airframeConfiguration: string; measuredAtUtc: string; validUntilUtc: string;
  payloadKg: number; maxWindMetersPerSecond: number; minTemperatureCelsius: number; maxTemperatureCelsius: number;
  usableCapacityWh: number; cruisePowerWatts: number; climbPowerWatts: number; descentPowerWatts: number;
  landingPowerWatts: number; hoverPowerWatts: number; minimumGroundSpeedMetersPerSecond: number;
  returnAltitudeMeters: number; landingDurationSeconds: number; holdDurationSeconds: number; energyMarginFactor: number;
  remainingEnergyCurve: {batteryPercent: number; lowerBoundWh: number}[];
};
export type LandingSiteSurvey = { surveyedAtUtc: string; validUntilUtc: string; groundClear: boolean; approachClear: boolean; evidenceReference: string };
import type { AccountRole } from "@/lib/auth/types";

export type MissionStatus = "Draft" | "Submitted" | "Pending" | "PendingApproval" | "Pending Review" | "Pending Confirm" | "Approved" | "InProgress" | "Completed" | "Rejected" | "Cancelled";

export type PilotMission = {
  automaticFlightRequested?: boolean;
  automaticFlightLimits?: AutomaticFlightLimits | null;
  id: string;
  createdBy?: string;
  name: string;
  description: string | null;
  status: MissionStatus;
  uavId: string | null;
  uavCode: string | null;
  averageSpeedMetersPerSecond: number | null;
  maxSpeedMetersPerSecond: number | null;
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

// CapturePhoto is retained only to read historical records; new camera actions are deferred.
export type WaypointAction = "TakeOff" | "FlyThrough" | "Hover" | "CapturePhoto" | "Land" | "Return";
export type MissionFence = { id: string; shape: "Polygon" | "Circle"; vertices?: GeofenceCoordinate[] | null; center?: GeofenceCoordinate | null; radiusMeters?: number | null };
export type PlannedHome = { latitude: number; longitude: number; altitudeMslMeters?: number | null };
export type WaypointNavigation = { missionFences?: MissionFence[] | null; plannedHome?: PlannedHome | null; speedMetersPerSecond?: number | null; holdTimeSeconds?: number | null; acceptanceRadiusMeters?: number | null; passRadiusMeters?: number | null; yawDegrees?: number | null; emergencyLandingCandidate?: boolean; landingGroundAltitudeMeters?: number | null; landingSiteNotes?: string | null; landingSiteSurvey?: LandingSiteSurvey | null };
export type FirmwareParameterSnapshot = { firmwareVersion: string; values: Record<string, number>; observedAtUtc: string; hardwareIdentity?: string | null; source: string };
export type MissionWaypoint = {
  navigation?: WaypointNavigation | null;
  sequenceOrder: number;
  latitude: number;
  longitude: number;
  altitude: number | null;
  actionType: WaypointAction;
};
export type CreateMissionRequest = {
  automaticFlightRequested?: boolean;
  automaticFlightLimits?: AutomaticFlightLimits | null;
  operationId?: string;
  expectedPlanHash?: string;
  expectedPendingRequestId?: string | null;
  altitudeReference?: "AGL" | "MSL" | "Relative" | null;
  uavId: string;
  name: string;
  description: string | null;
  averageSpeedMetersPerSecond: number;
  maxSpeedMetersPerSecond: number;
  startTime: string;
  endTime: string;
  waypoints: MissionWaypoint[];
};
export type MissionUpdateSummary = { requiresPilotConfirmation?: boolean; pilotConfirmedBy?: string | null; pilotConfirmedAt?: string | null; id: string; status: string; isInitial: boolean; createdAt: string; actorId: string; reviewedBy?: string | null; reviewedAt?: string | null; reason?: string | null; basePlan?: CreateMissionRequest | null; proposedPlan: CreateMissionRequest; validation?: MissionValidationResult | null };
export type MissionDetail = PilotMission & { planHash?: string; altitudeReference?: string | null; pendingUpdate?: MissionUpdateSummary | null; waypoints: MissionWaypoint[]; detailAvailable?: boolean; validation?: MissionValidationResult | null; latestValidation?: MissionValidationResult | null; flightReadiness?: string; preflightValidation?: MissionValidationResult | null };
export type ManagerMission = MissionDetail & {
  createdBy: string;
  uavName: string | null;
  uavStatus: string | null;
  pilotName: string | null;
};
export type MissionValidationResult = {
  id?: string; updateRequestId?: string | null; restrictionRevision?: string; stale?: boolean; incomplete?: boolean; error?: boolean; outcome?: "Pass" | "Fail" | "Incomplete" | "Error"; findings?: { geofenceId: string; revision: number; rule: string; waypointSequence?: number; segmentFrom?: number; segmentTo?: number; reason: string }[]; restrictionEvidence?: { geofenceId: string; revision: number; name: string; restrictionType: "OperationalBoundary" | "Restricted" | "NoFly"; coordinatesJson: string; verticalRule: string; altitudeReference?: string | null; minAltitudeMeters?: number | null; maxAltitudeMeters?: number | null; text: string }[]; missionId?: string | null; stage?: string; checkedAt?: string;
  isValid: boolean; message: string; status?: "Pass" | "Fail" | "Incomplete" | "Error" | "Stale"; issues?: string[]; warnings?: string[];
  weather?: MissionWeather | null;
  battery?: MissionBatteryRisk | null;
};

export type MissionBatteryRisk = {
  status: string;
  riskLevel: string;
  requiresConfirmation: boolean;
  routeDistanceMeters: number;
  estimatedFlightMinutes: number;
  uavMaxFlightTimeMinutes: number | null;
  batteryCapacityMah: number | null;
  batteryVoltage: number | null;
  batteryEnergyWh: number | null;
  baseBatteryPercent: number;
  weatherMultiplier: number;
  weatherAdjustmentPercent: number;
  safetyReservePercent: number;
  requiredBatteryPercent: number;
  message: string;
  reasons: string[];
  warnings: string[];
};

export type MissionWeather = {
  missionId?: string; updateRequestId?: string | null; status: "Available" | "Partial" | "Unavailable" | "Stale"; provider: string; available: boolean; fetchedAt: string; generatedAt: string; expiresAt: string; flightStartTime?: string | null; flightEndTime?: string | null; overallRisk: string; warnings: string[];
  maxWindMetersPerSecond: number | null; maxGustMetersPerSecond: number | null; maxRainMillimetersPerHour: number | null; thunderstormForecast: boolean;
  waypoints: { isRouteSample?: boolean; sampleOrder?: number; segmentFrom?: number | null; segmentTo?: number | null; sequenceOrder: number; latitude: number; longitude: number; altitudeMeters: number | null; estimatedArrivalTime: string | null; arrivalTimeReason: string | null; forecastTime: string | null; sourceFetchedAt: string | null; temperatureCelsius: number | null; relativeHumidityPercent: number | null; windSpeedMetersPerSecond: number | null; windDirectionDegrees: number | null; windGustMetersPerSecond: number | null; windReferenceHeightMeters: number; precipitationProbabilityPercent: number | null; precipitationMillimeters: number | null; visibilityMeters: number | null; cloudCoverPercent: number | null; weatherCode: number | null; status: string; riskLevel: string; warnings: string[] }[];
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
  batteryObservedAt?: string | null;
  staleAfterSeconds?: number;
  offlineAfterSeconds?: number;
  detectionDiagnostics?: string[];
  batteryVoltageVolts?: number | null;
  batteryCurrentAmps?: number | null;
  batteryTemperatureCelsius?: number | null;
  batteryConsumedMah?: number | null;
  batteryCellVoltagesVolts?: number[] | null;
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
  revision: number;
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

export type PortalNotification = {
  id: string;
  recipientUserId: string;
  actorUserId: string | null;
  type: string;
  title: string;
  message: string;
  referenceId: string | null;
  referenceType: string | null;
  actionUrl: string | null;
  isRead: boolean;
  readAt: string | null;
  createdAt: string;
};

export type NotificationFeed = {
  unreadCount: number;
  items: PortalNotification[];
};

export type PendingMissionApproval = {
  requiresPilotConfirmation?: boolean;
  proposalAuthorId?: string;
  isInitial: boolean;
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
  firmwareNavigationDefaults?: FirmwareParameterSnapshot | null;
  id: string;
  code: string;
  name: string;
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  status: UavStatus;
  pilotId: string;
  pilotName: string;
  emptyWeightKg: number | null;
  maxTakeoffWeightKg: number | null;
  maxPayloadKg: number | null;
  batteryType: string | null;
  batteryCapacityMah: number | null;
  batteryVoltage: number | null;
  batteryMaxVoltage?: number | null;
  batteryCellCount: number | null;
  maxFlightTimeMinutes: number | null;
  maxRangeKm: number | null;
  maxSpeedKmh: number | null;
  maxWindSpeedMps: number | null;
  maxOperatingAltitudeMeters: number | null;
  purchaseDate: string | null;
  lastMaintenanceAt: string | null;
  nextMaintenanceAt: string | null;
  totalFlightHours: number;
  totalFlights: number;
  imageUrl: string | null;
  imageModerationStatus?: "Pending" | "Processing" | "RetryScheduled" | "Approved" | "Rejected" | "ManualReview" | null;
  imageModeratedAt?: string | null;
  imageModerationReason?: string | null;
  createdAt: string;
};

export type CreateUavRequest = {
  code: string;
  name: string;
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  emptyWeightKg: number | null;
  maxTakeoffWeightKg: number | null;
  maxPayloadKg: number | null;
  batteryType: string | null;
  batteryCapacityMah: number | null;
  batteryVoltage: number | null;
  batteryMaxVoltage?: number | null;
  batteryCellCount: number | null;
  maxFlightTimeMinutes: number | null;
  maxRangeKm: number | null;
  maxSpeedKmh: number | null;
  maxWindSpeedMps: number | null;
  maxOperatingAltitudeMeters: number | null;
  purchaseDate: string | null;
};
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
  successRatePercentage: number | null; completionRateStatus: "Available" | "Unavailable";
  reportsAvailable: number; reportsExcluded: number;
  reportCoveragePercentage: number | null; fromDate: string | null; toDate: string | null;
  uavId: string | null; missionId: string | null;
  completionTrend: Array<{ date: string; totalFlights: number; completedFlights: number; failedOrAbortedFlights: number }>;
  incidentTypeBreakdown: Record<string, number>;
  incidentSeverityBreakdown: Record<string, number>;
  excludedReportReasons: Record<string, number>;
  contract: {
    windowBasis: string; windowTimeZone: string; units: Record<string, string>;
    sourceLinks: Record<string, string>; communicationLossThresholdSeconds: number;
  };
  missingCoverage: {
    validDistanceSegments: number; excludedDistanceSegments: number; excludedGapSeconds: number;
    missingGpsSamples: number; invalidTelemetrySamples: number;
    missionsWithoutValidGpsCoverage: number; missionsWithoutAvailableReport: number;
  };
};
export type FlightTelemetryPoint = {
  latitude: number | null; longitude: number | null; altitude: number | null;
  speed: number | null; batteryLevel: number | null; recordedAt: string;
};
export type PostFlightReport = {
  reportId: string; missionId: string; missionName: string; missionDescription: string | null;
  status: string; generationStatus: string; revision: number; isCurrent: boolean;
  coveredTelemetryFrom: string | null; coveredTelemetryThrough: string | null;
  generatedAt: string | null; failureReason: string | null;
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
  restrictionType?: "OperationalBoundary" | "Restricted" | "NoFly"; altitudeReference?: "AGL" | "MSL" | "Relative" | null; minAltitudeMeters?: number | null; maxAltitudeMeters?: number | null; name: string; description: string | null; coordinates: GeofenceCoordinate[] };
export type Geofence = {
  restrictionType?: "OperationalBoundary" | "Restricted" | "NoFly"; verticalRule?: "Unspecified" | "AllAltitudes" | "Band";
  altitudeReference?: string | null; minAltitudeMeters?: number | null; maxAltitudeMeters?: number | null; revision?: number; id: string; name: string; description: string | null; coordinates: GeofenceCoordinate[]; createdAt: string };
export type SaveGeofenceRequest = { restrictionType?: "OperationalBoundary" | "Restricted" | "NoFly"; verticalRule?: "Unspecified" | "AllAltitudes" | "Band"; altitudeReference?: "AGL" | "MSL" | "Relative" | null; minAltitudeMeters?: number | null; maxAltitudeMeters?: number | null; name: string; description: string | null; coordinates: GeofenceCoordinate[] };

// Matches DroneOps.Application.DTOs.Users.UserProfileResponse.
export type UserProfile = {
  profileVersion: number;
  avatarUrl: string | null;
  id: string;
  fullName: string;
  email: string;
  role: string;
  createdAt: string;
};

// Matches DroneOps.Application.DTOs.Users.UpdateProfileRequest.
export type UpdateProfileRequest = { fullName: string; expectedVersion: number };

export type AiAssessmentRequest = { missionRef: string; missionVersion: number };
export type BatteryAssessment = {
  status: "MissingData" | "EstimateOnly"; model: string; capacitySource: string;
  configuredCapacityMah: number | null; currentBatteryPercent: number | null; consumedMah: number | null;
  telemetrySourceTimestamp: string | null; estimatedRemainingChargeMah: number | null;
  requiredBatteryPercent: number | null; estimatedRequiredChargeMah: number | null;
  meetsEstimatedRequirement: boolean | null; missingData: string[]; warnings: string[];
  routeDistanceMeters?: number | null; estimatedFlightMinutes?: number | null;
  baseBatteryPercent?: number | null; weatherAdjustmentPercent?: number | null;
  safetyReservePercent?: number | null; reasons?: string[];
};
export type AiRisk = { category?: string; sourceIncidentIds?: string[]; severity: "Low" | "Moderate" | "High" | "Critical"; title: string; evidence: string; precaution: string };
export type AiAssessment = {
  id: string; missionRef: string; missionVersion: number; provider: string; model: string;
  status: "Pending" | "Available" | "Outdated" | "Failed" | "ProviderUnavailable";
  contextHash: string; isOutdated: boolean; outdatedReason: string | null;
  result: { automaticFlightLimits?: AutomaticFlightLimits | null; cruiseSpeedMetersPerSecond?: number | null; maxSpeedMetersPerSecond?: number | null; battery?: BatteryAssessment | null; summary: string; risks: AiRisk[]; precautions: string[]; missingData: string[]; disclaimer: string;
    retrieval?: { method: string; featureSchema: string; maximumResults: number };
    historicalFlights?: Array<{ missionId: string; missionName: string; similarityScore: number; features: Record<string, number | null>; incidentIds: string[] }> } | null;
  errorCode: string | null; createdAt: string; completedAt: string | null;
};

export type PortalRole = AccountRole;

export type PilotProfile = {
  profileVersion: number;
  userId: string; fullName: string; email: string; createdAt: string;
  pilotId: string; phoneNumber: string | null; dateOfBirth: string | null;
  pilotLicenseNo: string; licenseIssuedDate: string | null;
  licenseExpiredDate: string | null; experienceYears: number;
  droneType: string | null; droneModel: string | null;
};
