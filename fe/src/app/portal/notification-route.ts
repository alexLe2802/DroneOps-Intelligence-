export type NotificationRouteInput = {
  type: string;
  referenceId: string | null;
  actionUrl: string | null;
};

export function notificationDestination(notification: NotificationRouteInput): string {
  if (notification.type === "UavManualReview") {
    return "/portal/uav-management?imageModerationStatus=ManualReview";
  }
  if (notification.referenceId && notification.type === "NewPilotRegistration") {
    return `/portal/operators?registrationId=${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && notification.type === "NewPilotUpdateRequest") {
    return `/portal/operators?requestId=${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && ["PilotUpdateApproved", "PilotUpdateRejected"].includes(notification.type)) {
    return `/portal/profile?requestId=${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && ["FlightConnectionRequired", "FlightConfirmationRequired"].includes(notification.type)) {
    return `/portal/live-monitoring?missionId=${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && notification.type === "NewPendingMission") {
    return `/portal/mission-approvals?missionId=${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && ["MissionApproved", "MissionRejected"].includes(notification.type)) {
    return `/portal/missions/${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && notification.type === "IncidentRaised") {
    return `/portal/incidents/${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && ["UavApproved", "UavRejected"].includes(notification.type)) {
    return `/portal/uav/${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && notification.type === "UavImageRejected") {
    return `/portal/uav/${encodeURIComponent(notification.referenceId)}/edit`;
  }
  if (notification.referenceId && ["NewPendingUav", "UavUpdatePending"].includes(notification.type)) {
    return `/portal/uav-management?uavId=${encodeURIComponent(notification.referenceId)}`;
  }
  if (!notification.actionUrl || !notification.actionUrl.startsWith("/") || notification.actionUrl.startsWith("//")) {
    return "/portal";
  }

  try {
    const target = new URL(notification.actionUrl, "https://droneops.local");
    if (target.pathname === "/validation-approvals") {
      return `/portal/mission-approvals${target.search}`;
    }
    if (target.pathname.startsWith("/missions/")) {
      return `/portal${target.pathname}${target.search}`;
    }
    if (target.pathname === "/missions") {
      return `/portal/missions${target.search}`;
    }
    if (target.pathname === "/uav-management") {
      return `/portal/uav-management${target.search}`;
    }
    if (target.pathname === "/operators") {
      return `/portal/operators${target.search}`;
    }
    if (target.pathname === "/profile") {
      return `/portal/profile${target.search}`;
    }
    if (target.pathname === "/incident-management") {
      const incidentId = target.searchParams.get("incidentId");
      return incidentId ? `/portal/incidents/${encodeURIComponent(incidentId)}` : "/portal/incidents";
    }
    if (target.pathname === "/live-monitoring") {
      return `/portal/live-monitoring${target.search}`;
    }
    if (target.pathname === "/mission-planning") {
      const missionId = target.searchParams.get("missionId");
      return missionId ? `/portal/missions/${encodeURIComponent(missionId)}` : "/portal/mission-planning";
    }
    if (target.pathname.startsWith("/portal/")) {
      return `${target.pathname}${target.search}`;
    }
  } catch {
    return "/portal";
  }
  return "/portal";
}
