import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import PostFlightReportClient from "./post-flight-report-client";

export const metadata = { title: "Post-flight Report | DroneOps Intelligence" };

export default async function PostFlightReportPage({ params, searchParams }: { params: Promise<{ missionId: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const [session, { missionId }] = await Promise.all([requirePortalSession(), params]);
  const query = new URLSearchParams();
  for(const [key,value] of Object.entries(await searchParams)) if(typeof value === "string") query.set(key,value);
  return <PortalShell account={session.account}><PostFlightReportClient missionId={missionId} role={session.account.role} returnScope={query.toString()} /></PortalShell>;
}
