import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import MissionForm from "./mission-form";

export const metadata = { title: "Add Flight Route | DroneOps Intelligence" };
export default async function NewMissionPage() {
  const session = await requirePortalSession(["uav_operator"]);
  return <PortalShell account={session.account}><MissionForm /></PortalShell>;
}
