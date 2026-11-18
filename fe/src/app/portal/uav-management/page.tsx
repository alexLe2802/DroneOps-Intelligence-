import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import UavManagementClient from "./uav-management-client";

export const metadata = { title: "UAV Management | DroneOps Intelligence" };

export default async function UavManagementPage({ searchParams }: { searchParams: Promise<{ status?: string | string[] }> }) {
  const session = await requirePortalSession(["operation_manager"]);
  const query = await searchParams;
  const requestedUavStatus = Array.isArray(query.status) ? query.status[0] : query.status;
  const status = requestedUavStatus === "Pending" ? "Pending" : "";
  return <PortalShell account={session.account}><UavManagementClient key={status || "all"} initialStatus={status} /></PortalShell>;
}
