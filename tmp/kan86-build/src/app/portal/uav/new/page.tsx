import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import UavForm from "../uav-form";

export const metadata = { title: "Add UAV | DroneOps Intelligence" };
export default async function AddUavPage() {
  const session = await requirePortalSession(["uav_operator"]);
  return <PortalShell account={session.account}><UavForm /></PortalShell>;
}
