"use client";

import { formatVietnamTime } from "@/lib/portal/vietnam-time";
import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import {
  CheckCircle2,
  ClipboardCheck,
  Clock,
  History,
  LayoutGrid,
  ListChecks,
  Map,
  Plane,
  Radar,
  Radio,
  Sparkles,
  TriangleAlert,
  Users,
  Waypoints,
} from "lucide-react";
import type { Account } from "@/lib/auth/types";
import AccountMenu from "./account-menu";
import NotificationCenter from "./notification-center";
import FlightPreparationPresence from "./flight-preparation-presence";
import { managerNavGroups, operatorNavGroups } from "./portal-navigation";

function NavIcon({ name }: { name: string }) {
  const props = { size: 19, strokeWidth: 2, "aria-hidden": true as const };
  switch (name) {
    case "dashboard": return <LayoutGrid {...props} />;
    case "planning": return <Map {...props} />;
    case "missions": return <ListChecks {...props} />;
    case "approvals": return <CheckCircle2 {...props} />;
    case "validation": return <ClipboardCheck {...props} />;
    case "monitoring": return <Radar {...props} />;
    case "uav": return <Plane {...props} />;
    case "geofences": return <Waypoints {...props} />;
    case "operators": return <Users {...props} />;
    case "connection": return <Radio {...props} />;
    case "incidents": return <TriangleAlert {...props} />;
    case "history": return <History {...props} />;
    case "ai": return <Sparkles {...props} />;
    default: return <LayoutGrid {...props} />;
  }
}

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
  const manager = account.role === "operation_manager";
  const groups = manager ? managerNavGroups : operatorNavGroups;

  return (
    <div className={`ops-shell${dashboard ? "" : " module-shell"}`}>
      <aside className="ops-sidebar">
        <Link className="ops-brand" href="/portal">
          <Image src="/droneops-logo.svg" width={36} height={36} alt="" priority />
          <div>
            <b>DroneOps</b>
            <span>INTELLIGENCE</span>
          </div>
        </Link>
        <nav aria-label={`${manager ? "Manager" : "Operator"} navigation`}>
          {groups.map(group => (
            <div className="ops-nav-group" key={group.section}>
              <div className="ops-nav-section">{group.section}</div>
              {group.items.map(item => {
                const active = item.href === "/portal"
                  ? pathname === item.href && (!activeNav || activeNav === item.label || activeNav === "Dashboard")
                  : pathname.startsWith(item.href);
                return (
                  <Link
                    key={item.label}
                    aria-label={item.label}
                    href={item.href}
                    className={active ? "active" : ""}
                    onClick={() => { if (item.label === "Dashboard" || item.label === "My Dashboard") onNavigate?.(item.label); }}
                  >
                    <span className="dash-icon" aria-hidden="true"><NavIcon name={item.icon} /></span>
                    <span>{item.label}</span>
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>
      </aside>
      <div className="ops-main">
        <header className="ops-topbar">
          <time><Clock size={15} aria-hidden="true" /> {now ? formatVietnamTime(now) : "--:--:--"} GMT+7</time>
          <div className="ops-topbar-right">
            <NotificationCenter />
            <span className="ops-topbar-divider" aria-hidden="true" />
            <div className="ops-user-summary">
              <strong>{account.displayName}</strong>
              <span>{manager ? "Operations Manager" : "UAV Operator"}</span>
            </div>
            <AccountMenu account={account} />
          </div>
        </header>
        {!manager && <FlightPreparationPresence />}
        <main className={`ops-content${dashboard ? "" : " module-content"}`}>{children}</main>
      </div>
    </div>
  );
}
