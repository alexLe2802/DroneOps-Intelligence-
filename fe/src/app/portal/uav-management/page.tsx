import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import UavManagementClient from "./uav-management-client";

export const metadata = { title: "UAV Management | DroneOps Intelligence" };

export default async function UavManagementPage({ searchParams }: { searchParams: Promise<{ imageModerationStatus?: string | string[] }> }) {
  const session = await requirePortalSession(["operation_manager"]);
  const query = await searchParams;
  const requestedStatus = Array.isArray(query.imageModerationStatus) ? query.imageModerationStatus[0] : query.imageModerationStatus;
  const imageModerationStatus = requestedStatus === "ManualReview" ? "ManualReview" : "";
  return <PortalShell account={session.account}><UavManagementClient key={imageModerationStatus || "all"} initialImageModerationStatus={imageModerationStatus} /></PortalShell>;
}
