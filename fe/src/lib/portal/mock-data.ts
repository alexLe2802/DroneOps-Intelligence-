import type { AssignedUav, PilotMission } from "./types";

export const mockPilotMissions: PilotMission[] = [
  { id: "ms-8849", name: "Transmission Line Audit", description: "Thermal inspection along the SGN-04 transmission corridor.", status: "PendingApproval", uavId: "uav-01", uavCode: "UAV-01", startTime: "2026-10-06T02:00:00Z", endTime: "2026-10-06T03:15:00Z", createdAt: "2026-10-04T08:22:00Z", latestVersion: 3 },
  { id: "ms-8836", name: "Solar Array Survey", description: "Routine visual survey and hotspot capture.", status: "Completed", uavId: "uav-01", uavCode: "UAV-01", startTime: "2026-10-03T03:30:00Z", endTime: "2026-10-03T04:08:00Z", createdAt: "2026-10-02T09:10:00Z", latestVersion: 2 },
  { id: "ms-8861", name: "Perimeter Mapping", description: "Draft mapping route for the western perimeter.", status: "Draft", uavId: null, uavCode: null, startTime: null, endTime: null, createdAt: "2026-10-05T11:40:00Z", latestVersion: 1 },
];

export const mockAssignedUavs: AssignedUav[] = [
  { id: "uav-01", code: "UAV-01", name: "North Survey One", model: "DJI Matrice 350 RTK", status: "Assigned", batteryPercent: 82, lastTelemetryAt: "2026-10-05T14:40:12Z", assignedMissionId: "ms-8849" },
];
