import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import UavDetailClient from "./uav-detail-client";

export const metadata = { title: "UAV Details | DroneOps Intelligence" };
export default async function UavDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const [session, { id }] = await Promise.all([requirePortalSession(["uav_operator"]), params]);
  return <PortalShell account={session.account}><UavDetailClient id={id} /></PortalShell>;
}
