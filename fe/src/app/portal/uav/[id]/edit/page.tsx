import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../../portal-shell";
import UavEditClient from "./uav-edit-client";

export const metadata = { title: "Update UAV | DroneOps Intelligence" };
export default async function EditUavPage({ params }: { params: Promise<{ id: string }> }) {
  const [session, { id }] = await Promise.all([requirePortalSession(["uav_operator"]), params]);
  return <PortalShell account={session.account}><UavEditClient id={id} /></PortalShell>;
}
