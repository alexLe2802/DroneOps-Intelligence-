import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import DroneConnectionClient from "./drone-connection-client";
export const metadata = { title: "Connect Drone | DroneOps Intelligence" };
export default async function Page() {
  const session = await requirePortalSession(["uav_operator"]);
  return <PortalShell account={session.account}><DroneConnectionClient /></PortalShell>;
}
