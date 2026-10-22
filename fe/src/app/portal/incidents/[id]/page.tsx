import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import IncidentDetailClient from "./incident-detail-client";

export const metadata = { title: "Incident Details | DroneOps Intelligence" };

export default async function IncidentDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const [session, { id }] = await Promise.all([
    requirePortalSession(["operation_manager", "uav_operator"]),
    params,
  ]);
  return <PortalShell account={session.account}><IncidentDetailClient id={id} account={session.account} /></PortalShell>;
}
