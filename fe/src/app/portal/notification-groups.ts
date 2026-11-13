import type { PortalNotification } from "@/lib/portal/types";
import { notificationDestination } from "./notification-route";

export type NotificationDisplayItem = {
  key: string;
  notifications: PortalNotification[];
  title: string;
  message: string;
  createdAt: string;
  destination: string;
  isRead: boolean;
};

type PendingGroup = "missions" | "uavs";

function pendingGroup(type: string): PendingGroup | null {
  if (type === "NewPendingMission") return "missions";
  if (["NewPendingUav", "UavUpdatePending"].includes(type)) return "uavs";
  return null;
}

function buildPendingItem(group: PendingGroup, notifications: PortalNotification[]): NotificationDisplayItem {
  const sorted = [...notifications].sort((left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt));
  const unreadCount = sorted.filter(notification => !notification.isRead).length;
  const count = unreadCount || sorted.length;
  const noun = group === "missions" ? "mission" : "UAV";
  const destination = group === "missions" ? "/portal/mission-approvals" : "/portal/uav-management?status=Pending";

  return {
    key: `pending-${group}`,
    notifications: sorted,
    title: group === "missions" ? "Mission approvals pending" : "UAV reviews pending",
    message: unreadCount > 0
      ? `${count} new ${noun}${count === 1 ? "" : "s"} waiting for manager review.`
      : `${sorted.length} recent ${noun}${sorted.length === 1 ? " request" : " requests"}. Open the review queue.`,
    createdAt: sorted[0].createdAt,
    destination,
    isRead: unreadCount === 0,
  };
}

export function groupManagerNotifications(notifications: PortalNotification[]): NotificationDisplayItem[] {
  const pending: Record<PendingGroup, PortalNotification[]> = { missions: [], uavs: [] };
  const displayItems: NotificationDisplayItem[] = [];

  for (const notification of notifications) {
    const group = pendingGroup(notification.type);
    if (group) {
      pending[group].push(notification);
      continue;
    }

    displayItems.push({
      key: notification.id,
      notifications: [notification],
      title: notification.title,
      message: notification.message,
      createdAt: notification.createdAt,
      destination: notificationDestination(notification),
      isRead: notification.isRead,
    });
  }

  for (const group of ["missions", "uavs"] as const) {
    if (pending[group].length > 0) displayItems.push(buildPendingItem(group, pending[group]));
  }

  return displayItems.sort((left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt));
}
