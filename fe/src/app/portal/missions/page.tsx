import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import MissionsClient from "./missions-client";

export const metadata = { title: "My Missions | DroneOps Intelligence" };
export default async function MissionsPage() {
  const session = await requirePortalSession(["uav_operator"]);
  return <PortalShell account={session.account}><MissionsClient /></PortalShell>;
}
