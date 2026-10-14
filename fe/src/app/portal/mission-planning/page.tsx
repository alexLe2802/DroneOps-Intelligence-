import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import MissionPlanningClient from "./mission-planning-client";

export const metadata = { title: "Mission Planning | DroneOps Intelligence" };

export default async function MissionPlanningPage() {
  const session = await requirePortalSession(["operation_manager"]);
  return <PortalShell account={session.account}><MissionPlanningClient /></PortalShell>;
}
