import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../../portal-shell";
import FlightHistoryClient from "../flight-history-client";
export const metadata = { title: "Analytics Sources | DroneOps Intelligence" };
export default async function SourcesPage({searchParams}:{searchParams:Promise<Record<string,string|string[]|undefined>>}) {
  const session=await requirePortalSession();
  const query=new URLSearchParams();
  for(const [key,value] of Object.entries(await searchParams)) if(typeof value === "string") query.set(key,value);
  return <PortalShell account={session.account}><FlightHistoryClient key={query.toString()} initialQuery={query.toString()} role={session.account.role} sourceView /></PortalShell>;
}
