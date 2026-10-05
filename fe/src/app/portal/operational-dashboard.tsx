"use client";

import Image from "next/image";
import { useEffect, useState, type ReactNode } from "react";
import { api } from "@/lib/auth/client";
import type { Account } from "@/lib/auth/types";

const navItems = [
  ["▦", "Dashboard", "02"], ["⌁", "Mission Planning", "ROUTE"],
  ["◈", "Validation & Approvals", "3 PENDING"], ["⌁", "Live Monitoring", "●"],
  ["△", "Incident Management", "2 ALERT"], ["⌁", "Flight History & Reports", "POST"],
  ["◎", "AI Risk Assessment", "EVAL"], ["⌘", "Fleet & Restrictions", "14 UAV"],
];

const flights = [
  { id: "#MS-8849", asset: "UAV-01", model: "DJI Matrice 350 RTK", pilot: "Nguyen Long", phase: "WAYPOINT 4/12", alt: "120m", sync: "Fresh (0.4s)", quality: 98, battery: 82, danger: false },
  { id: "#MS-8850", asset: "UAV-02", model: "DJI Inspire 3", pilot: "Pham Tuan", phase: "TAKEOFF CLIMB", alt: "85m", sync: "Fresh (0.7s)", quality: 94, battery: 74, danger: false },
  { id: "#MS-8848", asset: "UAV-Alpha-04", model: "DJI Matrice 30T", pilot: "Tran Hoang", phase: "HOLD / FAILSAFE", alt: "64m", sync: "TIMEOUT (4.2s)", quality: 41, battery: 58, danger: true },
];

function Icon({ children }: { children: ReactNode }) { return <span className="dash-icon" aria-hidden="true">{children}</span>; }

export default function OperationalDashboard({ account }: { account: Account }) {
  const [now, setNow] = useState<Date | null>(null);
  const [activeNav, setActiveNav] = useState("Dashboard");
  const [feed, setFeed] = useState<"live" | "gcs" | "simulation">("live");
  const [query, setQuery] = useState("");
  const [alertVisible, setAlertVisible] = useState(true);
  const [toast, setToast] = useState("");
  useEffect(() => {
    const frame = window.requestAnimationFrame(() => setNow(new Date()));
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => {
      window.cancelAnimationFrame(frame);
      window.clearInterval(timer);
    };
  }, []);
  function notify(message: string) { setToast(message); window.setTimeout(() => setToast(""), 2600); }
  async function signOut() { await api("/auth/logout", { method: "POST" }); window.location.replace("/login"); }
  const filtered = flights.filter(f => `${f.id} ${f.asset} ${f.pilot} ${f.phase}`.toLowerCase().includes(query.toLowerCase()));

  return <div className="ops-shell">
    <aside className="ops-sidebar">
      <div className="ops-brand"><Image src="/droneops-logo.svg" width={34} height={34} alt="" priority /><div><b>DRONEOPS</b><span>INTELLIGENCE</span></div></div>
      <div className="airspace-label"><span>ACTIVE AIRSPACE</span><b>UTM SECTOR 04</b></div>
      <nav aria-label="Operations navigation">{navItems.map(([icon,label,badge]) => <button key={label} className={activeNav === label ? "active" : ""} onClick={() => { setActiveNav(label); if (label !== "Dashboard") notify(`${label} module is ready for integration`); }}><Icon>{icon}</Icon><span>{label}</span><small className={badge.includes("ALERT") ? "red-badge" : badge.includes("PENDING") ? "green-badge" : badge === "EVAL" ? "purple-badge" : ""}>{badge}</small></button>)}</nav>
      <div className="sidebar-status"><div><span>TELEMETRY LINK</span><b>ENCRYPTED (AES-256)</b></div><i /><div><span>BANDWIDTH</span><b>48.2 Mbps</b></div></div>
    </aside>
    <div className="ops-main">
      <header className="ops-topbar"><div className="sys-status"><i /> SYS STATUS: NOMINAL <span>(MAVLINK GATEWAY CONNECTED)</span></div><div className="latency">LATENCY <b>18ms</b></div><time>◷ {now ? now.toLocaleTimeString("en-GB") : "--:--:--"} UTC</time><div className="account-role">{account.role === "operations_manager" ? "OPERATIONS MANAGER" : "UAV OPERATOR"} <span>(HQ-NORTH)</span></div><button className="avatar" title={`${account.displayName} — Sign out`} onClick={signOut}>{account.displayName.slice(0,1).toUpperCase()}</button></header>
      <main className="ops-content">
        {alertVisible && <section className="critical-banner"><Icon>△</Icon><div><b>CRITICAL AIRSPACE ADVISORY</b></div><p>COMM LOSS (DEGRADED) — UAV-Alpha-04 | Sector C-4 (Ping: 4.2s / Latency Spill)</p><button onClick={() => setAlertVisible(false)}>ACKNOWLEDGE</button><button className="danger-button" onClick={() => notify("Incident SCR-12 opened")}>INVESTIGATE SCR-12 →</button></section>}
        <section className="dashboard-heading"><div><p>◎ SECTOR SGN-04 TECH PARK OPERATIONAL THEATER</p><h1>Mission &amp; Fleet Operations<br />Hub</h1></div><div className="feed-tabs">{(["live","gcs","simulation"] as const).map(item => <button key={item} className={feed === item ? "active" : ""} onClick={() => setFeed(item)}>{item === "live" ? "LIVE FEED" : item === "gcs" ? "GCS SYNC" : "SIMULATION"}</button>)}</div><button className="preset-button" onClick={() => notify("Sector preset menu opened")}>⌁ SECTOR PRESETS</button><button className="new-mission" onClick={() => notify("New mission workflow started")}>⊕ NEW MISSION DISPATCH</button></section>
        <section className="metric-grid" aria-label="Operational summary">
          <article><span>ACTIVE FLIGHTS</span><Icon>⌁</Icon><strong>3<small>/ 8 IN AIR</small></strong><p className="ok">● MAVLink Streaming <em>37.5% LOAD</em></p><div className="progress"><i style={{width:"38%"}} /></div></article>
          <article><span>MISSIONS TODAY</span><Icon>◉</Icon><strong>14<small>TOTAL LOG</small></strong><div className="mini-stats"><b>9<small>DONE</small></b><b>2<small>PENDING</small></b><b>1<small>REVISE</small></b></div></article>
          <article><span>AIRSPACE SAFETY</span><Icon>♢</Icon><strong className="warn">1<small>GEO ALERT</small></strong><p>Collision Risk: <b className="ok">0 CONFLICTS</b><br />Geofence Status: <b className="warn">1 BREACH LOG</b></p></article>
          <article><span>FLEET READINESS</span><Icon>⚑</Icon><strong className="ok">87.5%<small>OPERATIONAL</small></strong><p>7 Drones Ready <em>1 Maint.</em></p><div className="progress"><i style={{width:"88%"}} /></div></article>
          <article><span>TELEMETRY INGEST</span><Icon>⌁</Icon><strong className="blue">100%<small>STABLE</small></strong><p>Avg Latency: <b>0.8s</b><br />Jitter Variance: <b className="ok">±12ms</b></p></article>
        </section>
        <div className="operations-grid">
          <section className="map-panel"><header><b>◎ TAC-CANVAS // SGN-09 HIGH TECH INDUSTRIAL PARK</b><span>10.8490° N, 106.7725° E</span><em>UTM FEED ACTIVE</em></header><div className="tactical-map"><span className="street s1"/><span className="street s2"/><span className="street s3"/><span className="street s4"/><div className="geo-zone"><span>ZONE BRAVO GEOFENCE PERIMETER</span></div><div className="drone-marker m1"><i>➤</i><b>UAV-01 (M350 RTK)</b><span>ALT: 120m | 14.2 m/s<br/>BAT: 82% | LINK: 98%</span></div><div className="drone-marker m2"><i>➤</i><b>UAV-02 (Inspire 3)</b><span>ALT: 85m | 8.5 m/s<br/>BAT: 74% | LINK: 94%</span></div><div className="restricted">RESTRICTED FACILITY</div><div className="map-alert">!</div><div className="map-legend">ZOOM: 14.5x&nbsp; HDOP: 0.82&nbsp; SAT: 28 GLONASS/GPS <span>● M350 RTK &nbsp; <b>● INSPIRE 3</b> &nbsp; ● JITTER ALERT</span></div></div><footer><span>GCS UPTIME: 19h 42m 11s &nbsp; ENCRYPTION: <b>AES-CTR-256</b></span><div><button>DOWNLOAD KML LOG</button><button className="cyan-button">EXPAND HUD FULLSCREEN</button></div></footer></section>
          <aside className="insight-column"><section className="ai-panel"><header><Icon>◉</Icon><div><h2>AI Decision Engine (FE-09)</h2><span>NEURAL RISK MITIGATION // ACTIVE</span></div><b>EVAL 99.4%</b></header><article><h3>MS-8849 [Transmission Line Audit] <small>CONFIDENCE: 92%</small></h3><p>Moderate micro-burst &amp; wind shear predicted at <b>Waypoint 6 (2,400m NE)</b> within 18 minutes. Recommend reducing cruise speed by <b>15%</b>.</p><div><span>≋ Vector: 284° @ 16.4 kts</span><button onClick={() => notify("AI speed recommendation applied")}>APPLY SPEED OFFSET</button></div></article><article><h3>⚐ SMART RTH WINDOW CALCULATION</h3><p>UAV-01 has 34 mins remaining. Optimal RTH initiation at 15:05 UTC to retain 20% buffer.</p></article></section>
          <section className="incident-panel"><header><h2>♧ Critical Incidents &amp;<br/>Approvals</h2><button>VIEW ALL (4)</button></header><article><span className="red-badge">COMM LOST LINK</span><time>00:01:24 AGO</time><h3>UAV-Alpha-04 · Signal Timeout &gt; 4.2s</h3><p>Exceeded standard 3.0s heartbeat packet window. Vehicle currently hovering at 64m AGL.</p><div><button>SILENCE (60S)</button><button className="danger-button">DISPATCH SCR-12</button></div></article><article><span className="blue-badge">SUBMITTED FOR APPROVAL</span><time>14:22 UTC</time><h3>Mission #MS-8842 // Pilot: DucDDA</h3><p>BVLOS Solar Array thermography flight path validated against NFZ.</p><b className="ok">Pre-check Passed (100%)</b></article></section></aside>
        </div>
        <section className="missions-panel"><header><div><Icon>⌁</Icon><h2>Active Airborne Missions<span>Real-time synchronized telemetry stream from active transponders</span></h2></div><label>⌕ <input value={query} onChange={e=>setQuery(e.target.value)} placeholder="Filter by callsign, pilot, phase..."/></label></header><div className="table-wrap"><table><thead><tr><th>MISSION ID</th><th>UAV ASSET</th><th>ASSIGNED PILOT</th><th>FLIGHT PHASE</th><th>TELEMETRY SYNC</th><th>LINK QUALITY</th><th>BATTERY</th><th>ACTION</th></tr></thead><tbody>{filtered.map(f => <tr key={f.id} className={f.danger?"danger-row":""}><td><b className="blue">{f.id}</b></td><td><b>{f.asset}</b><span>● {f.model}</span></td><td>{f.pilot}</td><td><b className="phase">{f.phase}</b><span>Alt: {f.alt}</span></td><td><b className={f.danger?"warn":"ok"}>● {f.sync}</b></td><td>{f.quality}% <i className="quality"><em style={{width:`${f.quality}%`}}/></i></td><td>▮ {f.battery}%</td><td><button onClick={()=>notify(`${f.id} monitor opened`)}>{f.danger?"RESOLVE":"MONITOR"}</button></td></tr>)}</tbody></table></div></section>
        <section className="bottom-grid"><article><header>PAYLOAD CAM 01 // UAV-01 <b>EO/IR 4K</b></header><div className="camera-feed"><span>IR: 42.1°C SP</span><i/><b>GIMBAL: -45° PITCH</b></div><footer>Target: Substation B-9 <button>EXPAND STREAM</button></footer></article><article><header>SGN-04 LOCAL WEATHER RADAR <b>METAR VFR</b></header><div className="weather"><p>SURFACE WIND<strong>11 kts / Gust 18</strong><span>Direction: 080° ENE</span></p><p>VISIBILITY<strong>&gt; 10 km</strong><span>Cloud Base: 3,500ft</span></p><p>QNH ALTIMETER<strong>1012 hPa</strong><span>Stable Trend</span></p><p>KP GEOMAGNETIC<strong>Kp 1.8 (Quiet)</strong><span>GPS Lock Optimal</span></p></div><footer>Updated 3m ago via Tan Son Nhat AWOS</footer></article><article><header>GCS CREW ASSIGNMENTS <b>3 ON-DUTY</b></header><ul><li><i/>Nguyen Long (PIC)<b>UAV-01</b></li><li><i/>Pham Tuan (PIC)<b>UAV-02</b></li><li><i className="alert-dot"/>Tran Hoang (PIC)<b className="warn">UAV-04 [EMERGENCY]</b></li></ul><footer>Shift Handover in 03h 22m <button>DUTY ROSTER</button></footer></article></section>
      </main>
    </div>{toast&&<div className="ops-toast" role="status">{toast}</div>}
  </div>;
}
