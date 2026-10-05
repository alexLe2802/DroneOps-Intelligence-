import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import ProfileClient from "./profile-client";

export const metadata = { title: "Profile | DroneOps Intelligence" };
export default async function ProfilePage() {
  const session = await requirePortalSession();
  return <PortalShell account={session.account}><ProfileClient account={session.account} /></PortalShell>;
}
