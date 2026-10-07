"use client";

import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import type { Account } from "@/lib/auth/types";
import AccountMenu from "./account-menu";
import { managerNavItems, operatorNavItems } from "./portal-navigation";

export default function PortalShell({ account, children, dashboard = false, activeNav, onNavigate }: {
  account: Account; children: ReactNode; dashboard?: boolean; activeNav?: string; onNavigate?: (label: string) => void;
}) {
  const pathname = usePathname();
  const [now, setNow] = useState<Date | null>(null);
  useEffect(() => {
    const frame = requestAnimationFrame(() => setNow(new Date()));
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => { cancelAnimationFrame(frame); clearInterval(timer); };
  }, []);
  const manager = account.role === "operations_manager";
  const nav = manager ? managerNavItems : operatorNavItems;
  return <div className={`ops-shell${dashboard ? "" : " module-shell"}`}>
    <aside className="ops-sidebar">
      <Link className="ops-brand" href="/portal"><Image src="/droneops-logo.svg" width={34} height={34} alt="" priority /><div><b>DRONEOPS</b><span>INTELLIGENCE</span></div></Link>
      <div className="airspace-label"><span>ACTIVE AIRSPACE</span><b>UTM SECTOR 04</b></div>
      <nav aria-label={`${manager ? "Manager" : "Operator"} navigation`}>{nav.map(([icon, label, badge], index) => {
        const href = index === 0 ? "/portal" : !manager && label === "My Missions" ? "/portal/missions" : !manager && label === "Assigned UAV" ? "/portal/uav" : null;
        const content = <><span className="dash-icon" aria-hidden="true">{icon}</span><span>{label}</span><small className={badge.includes("ALERT") ? "red-badge" : badge.includes("PENDING") ? "green-badge" : badge === "EVAL" ? "purple-badge" : ""}>{badge}</small></>;
        const active = href === "/portal" ? pathname === href && (!activeNav || activeNav === label || activeNav === "Dashboard") : href && pathname.startsWith(href);
        return href ? <Link key={label} href={href} className={active ? "active" : ""} onClick={() => { if (index === 0) onNavigate?.(label); }}>{content}</Link> : <button key={label} type="button" className={activeNav === label ? "active" : ""} disabled={!onNavigate} onClick={() => onNavigate?.(label)}>{content}</button>;
      })}</nav>
      <div className="sidebar-status"><div><span>TELEMETRY LINK</span><b>ENCRYPTED (AES-256)</b></div><i /><div><span>BANDWIDTH</span><b>48.2 Mbps</b></div></div>
    </aside>
    <div className="ops-main">
      <header className="ops-topbar"><div className="sys-status"><i /> SYS STATUS: NOMINAL <span>(MAVLINK GATEWAY CONNECTED)</span></div><div className="latency">LATENCY <b>18ms</b></div><time>◷ {now ? now.toLocaleTimeString("en-GB", { timeZone: "UTC" }) : "--:--:--"} UTC</time><div className="account-role">{manager ? "OPERATIONS MANAGER" : "UAV OPERATOR"}</div><AccountMenu account={account} /></header>
      <main className={`ops-content${dashboard ? "" : " module-content"}`}>{children}</main>
    </div>
  </div>;
}
