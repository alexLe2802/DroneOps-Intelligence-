import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../../portal-shell";
import MissionEditClient from "./mission-edit-client";

export const metadata = { title: "Edit Mission | DroneOps Intelligence" };

export default async function EditMissionPage({ params }: { params: Promise<{ id: string }> }) {
  const [session, { id }] = await Promise.all([requirePortalSession(["uav_operator"]), params]);
  return <PortalShell account={session.account}><MissionEditClient id={id} /></PortalShell>;
}
