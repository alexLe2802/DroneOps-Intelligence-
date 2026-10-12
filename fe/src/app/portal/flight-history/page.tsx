import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import FlightHistoryClient from "./flight-history-client";

export const metadata = { title: "Flight History & Reports | DroneOps Intelligence" };

export default async function FlightHistoryPage() {
  const session = await requirePortalSession();
  return <PortalShell account={session.account}><FlightHistoryClient /></PortalShell>;
}
