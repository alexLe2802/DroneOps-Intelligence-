import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import PostFlightReportClient from "./post-flight-report-client";

export const metadata = { title: "Post-flight Report | DroneOps Intelligence" };

export default async function PostFlightReportPage({ params }: { params: Promise<{ missionId: string }> }) {
  const [session, { missionId }] = await Promise.all([requirePortalSession(), params]);
  return <PortalShell account={session.account}><PostFlightReportClient missionId={missionId} role={session.account.role} /></PortalShell>;
}
