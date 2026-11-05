export type NotificationRouteInput = {
  type: string;
  referenceId: string | null;
  actionUrl: string | null;
};

export function notificationDestination(notification: NotificationRouteInput): string {
  if (notification.referenceId && notification.type === "NewPendingMission") {
    return `/portal/mission-approvals?missionId=${encodeURIComponent(notification.referenceId)}`;
  }
  if (notification.referenceId && ["MissionApproved", "MissionRejected"].includes(notification.type)) {
    return `/portal/missions/${encodeURIComponent(notification.referenceId)}`;
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
    if (target.pathname.startsWith("/portal/")) {
      return `${target.pathname}${target.search}`;
    }
  } catch {
    return "/portal";
  }
  return "/portal";
}
