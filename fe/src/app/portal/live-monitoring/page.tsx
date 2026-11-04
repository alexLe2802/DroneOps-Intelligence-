import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import LiveMonitoringClient from "./live-monitoring-client";

export const metadata = { title: "Live Monitoring | DroneOps Intelligence" };

export default async function LiveMonitoringPage({ searchParams }: { searchParams: Promise<{ missionId?: string }> }) {
  const session = await requirePortalSession(["operation_manager", "uav_operator"]);
  const query = await searchParams;
  return <PortalShell account={session.account}><LiveMonitoringClient initialMissionId={typeof query.missionId === "string" ? query.missionId : ""} canPrepare={session.account.role === "uav_operator"} /></PortalShell>;
}
