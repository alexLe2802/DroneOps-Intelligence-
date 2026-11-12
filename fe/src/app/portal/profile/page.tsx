import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import ProfileClient from "./profile-client";

export const metadata = { title: "Profile | DroneOps Intelligence" };
export default async function ProfilePage({ searchParams }: { searchParams: Promise<{ requestId?: string | string[] }> }) {
  const session = await requirePortalSession();
  const query = await searchParams;
  const requestId = Array.isArray(query.requestId) ? query.requestId[0] : query.requestId;
  return <PortalShell account={session.account}><ProfileClient account={session.account} initialLicenseRequestId={requestId} /></PortalShell>;
}
