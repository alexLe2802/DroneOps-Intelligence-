import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import MissionDetailClient from "./mission-detail-client";

export const metadata = { title: "Mission Details | DroneOps Intelligence" };

export default async function MissionDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const [session, { id }] = await Promise.all([requirePortalSession(["uav_operator"]), params]);
  return <PortalShell account={session.account}><MissionDetailClient id={id} /></PortalShell>;
}
