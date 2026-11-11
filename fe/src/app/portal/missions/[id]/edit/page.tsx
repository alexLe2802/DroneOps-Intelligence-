import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../../portal-shell";
import MissionEditClient from "./mission-edit-client";

export const metadata = { title: "Edit Mission | DroneOps Intelligence" };

export default async function EditMissionPage({ params }: { params: Promise<{ id: string }> }) {
  const [session, { id }] = await Promise.all([requirePortalSession(["uav_operator", "operation_manager"]), params]);
  return <PortalShell account={session.account}><MissionEditClient id={id} actorId={session.account.id} role={session.account.role} /></PortalShell>;
}
