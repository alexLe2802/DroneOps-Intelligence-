import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import LiveMonitoringClient from "./live-monitoring-client";

export const metadata = { title: "Live Monitoring | DroneOps Intelligence" };

export default async function LiveMonitoringPage() {
  const session = await requirePortalSession(["operation_manager", "uav_operator"]);
  return <PortalShell account={session.account}><LiveMonitoringClient /></PortalShell>;
}
