"use client";

import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { api } from "@/lib/auth/client";
import type { Account } from "@/lib/auth/types";

const pilotNav = [
  ["MY", "Dashboard", "/portal"],
  ["ROUTE", "My Missions", "/portal/missions"],
  ["UAV", "Assigned UAV", "/portal/uav"],
  ["ID", "Profile", "/portal/profile"],
] as const;

const managerNav = [
  ["▦", "Dashboard", "/portal"],
  ["ID", "Profile", "/portal/profile"],
] as const;

export default function PortalShell({ account, children }: { account: Account; children: ReactNode }) {
  const pathname = usePathname();
  const nav = account.role === "uav_operator" ? pilotNav : managerNav;
  async function signOut() {
    await api("/auth/logout", { method: "POST" });
    window.location.replace("/login");
  }
  return <div className="ops-shell module-shell">
    <aside className="ops-sidebar">
      <Link className="ops-brand" href="/portal"><Image src="/droneops-logo.svg" width={34} height={34} alt="" priority /><div><b>DRONEOPS</b><span>INTELLIGENCE</span></div></Link>
      <div className="airspace-label"><span>WORKSPACE</span><b>{account.role === "uav_operator" ? "PILOT" : "MANAGER"}</b></div>
      <nav aria-label="Portal navigation">{nav.map(([icon, label, href]) => <Link key={href} href={href} className={pathname === href ? "active" : ""}><span className="dash-icon">{icon}</span><span>{label}</span><small>→</small></Link>)}</nav>
      <div className="sidebar-status"><div><span>SESSION</span><b>SECURE</b></div><i /><div><span>ACCOUNT</span><b>{account.isActive ? "ACTIVE" : "DISABLED"}</b></div></div>
    </aside>
    <div className="ops-main">
      <header className="ops-topbar"><div className="sys-status"><i /> SYS STATUS: NOMINAL</div><div className="account-role">{account.role === "operations_manager" ? "OPERATIONS MANAGER" : "UAV OPERATOR"}</div><button className="avatar" title={`${account.displayName} — Sign out`} onClick={signOut}>{account.displayName.slice(0, 1).toUpperCase()}</button></header>
      <main className="ops-content module-content">{children}</main>
    </div>
  </div>;
}
