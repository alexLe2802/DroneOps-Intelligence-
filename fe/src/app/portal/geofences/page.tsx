import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import GeofencesClient from "./geofences-client";

export const metadata = { title: "Airspace & Geofences | DroneOps Intelligence" };

export default async function GeofencesPage() {
  const session = await requirePortalSession();
  return <PortalShell account={session.account}><GeofencesClient canManage={session.account.role === "operations_manager"} /></PortalShell>;
}
