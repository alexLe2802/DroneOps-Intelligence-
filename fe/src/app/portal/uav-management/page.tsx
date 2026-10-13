import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import UavManagementClient from "./uav-management-client";

export const metadata = { title: "UAV Management | DroneOps Intelligence" };

export default async function UavManagementPage() {
  const session = await requirePortalSession(["operation_manager"]);
  return <PortalShell account={session.account}><UavManagementClient /></PortalShell>;
}
