import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import UavClient from "./uav-client";

export const metadata = { title: "Assigned UAV | DroneOps Intelligence" };
export default async function UavPage() {
  const session = await requirePortalSession(["uav_operator"]);
  return <PortalShell account={session.account}><UavClient /></PortalShell>;
}
