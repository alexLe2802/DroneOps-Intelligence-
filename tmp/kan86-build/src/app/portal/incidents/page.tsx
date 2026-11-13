import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import IncidentManagementClient from "./incident-management-client";

export const metadata = { title: "Incident Management | DroneOps Intelligence" };

export default async function IncidentManagementPage() {
  const session = await requirePortalSession(["operation_manager", "uav_operator"]);
  return <PortalShell account={session.account}><IncidentManagementClient account={session.account} /></PortalShell>;
}
