import { redirect } from "next/navigation";
import { readSession } from "@/lib/auth/server";
import OperationalDashboard from "./operational-dashboard";
import "./dashboard.css";

export const metadata = { title: "Mission Operations | DroneOps Intelligence" };
export default async function PortalPage() {
  const session = await readSession();
  if (!session) redirect("/login");
  return <OperationalDashboard account={session.account} />;
}
