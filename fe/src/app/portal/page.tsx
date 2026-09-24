import Image from "next/image";
import Link from "next/link";
import { redirect } from "next/navigation";
import { readSession } from "@/lib/auth/server";
import { roleLabels } from "@/lib/auth/types";
import PortalControls from "./portal-controls";
import "../auth.css";

export const metadata = { title: "Your workspace | DroneOps Intelligence" };
export default async function PortalPage() {
  const session = await readSession();
  if (!session) redirect("/login");
  return <main className="portal-page shell"><header className="auth-header"><Link href="/" className="brand"><Image src="/droneops-logo.svg" width={40} height={40} alt="" /><div className="brand-name">DroneOps <span>INTELLIGENCE</span></div></Link><span className="eyebrow">{roleLabels[session.account.role]}</span></header><section className="portal-welcome"><span className="auth-kicker">YOUR WORKSPACE</span><h1>Welcome, <span>{session.account.displayName}.</span></h1><p>{session.account.email}</p><p className="portal-scope">{session.account.role === "operations_manager" ? "Manage operator access and keep your team's accounts up to date." : "Your account is ready for mission planning and monitoring. Mission features are under development."}</p></section><PortalControls account={session.account} /></main>;
}
