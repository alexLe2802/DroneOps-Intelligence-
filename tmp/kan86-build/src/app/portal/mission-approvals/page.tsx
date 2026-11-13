import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import MissionApprovalsClient from "./mission-approvals-client";

export const metadata = { title: "Validation & Approvals | DroneOps Intelligence" };

export default async function MissionApprovalsPage() {
  const session = await requirePortalSession(["operation_manager"]);
  return <PortalShell account={session.account}><MissionApprovalsClient account={session.account} /></PortalShell>;
}
