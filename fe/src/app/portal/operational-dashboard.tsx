"use client";

import Image from "next/image";
import { useEffect, useState, type ReactNode } from "react";
import { api } from "@/lib/auth/client";
import type { Account } from "@/lib/auth/types";

const managerNavItems = [
  ["▦", "Dashboard", "02"], ["⌁", "Mission Planning", "ROUTE"],
  ["◈", "Validation & Approvals", "3 PENDING"], ["⌁", "Live Monitoring", "●"],
  ["△", "Incident Management", "2 ALERT"], ["⌁", "Flight History & Reports", "POST"],
  ["◎", "AI Risk Assessment", "EVAL"], ["⌘", "Fleet & Restrictions", "14 UAV"],
];

const operatorNavItems = [
  ["MY", "My Dashboard", "02"], ["ROUTE", "My Missions", "ROUTE"],
  ["CHECK", "Validation Results", "1 UPDATE"], ["LIVE", "Live Monitoring", "LIVE"],
  ["ALERT", "My Incidents", "1 ALERT"], ["LOG", "Flight History", "POST"],
  ["AI", "AI Risk Assessment", "EVAL"], ["UAV", "Assigned UAV", "UAV-01"],
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
  const scopedFlights = manager ? flights : flights.slice(0, 1);
  const filtered = scopedFlights.filter(f => `${f.id} ${f.asset} ${f.pilot} ${f.phase}`.toLowerCase().includes(query.toLowerCase()));

  return <div className="ops-shell">
    <aside className="ops-sidebar">
      <div className="ops-brand"><Image src="/droneops-logo.svg" width={34} height={34} alt="" priority /><div><b>DRONEOPS</b><span>INTELLIGENCE</span></div></div>
      <div className="airspace-label"><span>ACTIVE AIRSPACE</span><b>UTM SECTOR 04</b></div>
      <nav aria-label={`${manager ? "Manager" : "Operator"} navigation`}>{navItems.map(([icon,label,badge], index) => <button key={label} className={activeNav === label ? "active" : ""} onClick={() => { setActiveNav(label); if (index !== 0) notify(`${label} module is ready for integration`); }}><Icon>{icon}</Icon><span>{label}</span><small className={badge.includes("ALERT") ? "red-badge" : badge.includes("PENDING") ? "green-badge" : badge === "EVAL" ? "purple-badge" : ""}>{badge}</small></button>)}</nav>
      <div className="sidebar-status"><div><span>TELEMETRY LINK</span><b>ENCRYPTED (AES-256)</b></div><i /><div><span>BANDWIDTH</span><b>48.2 Mbps</b></div></div>
    </aside>
    <div className="ops-main">
      <header className="ops-topbar"><div className="sys-status"><i /> SYS STATUS: NOMINAL <span>(MAVLINK GATEWAY CONNECTED)</span></div><div className="latency">LATENCY <b>18ms</b></div><time>◷ {now ? now.toLocaleTimeString("en-GB") : "--:--:--"} UTC</time><div className="account-role">{account.role === "operations_manager" ? "OPERATIONS MANAGER" : "UAV OPERATOR"} <span>(HQ-NORTH)</span></div><button className="avatar" title={`${account.displayName} — Sign out`} onClick={signOut}>{account.displayName.slice(0,1).toUpperCase()}</button></header>
      <main className="ops-content">
        {alertVisible && <section className="critical-banner"><Icon>△</Icon><div><b>CRITICAL AIRSPACE ADVISORY</b></div><p>{manager ? "COMM LOSS (DEGRADED) — UAV-Alpha-04 | Sector C-4 (Ping: 4.2s / Latency Spill)" : "ROUTE WEATHER ADVISORY — MS-8849 | Review conditions before waypoint 6"}</p><button onClick={() => setAlertVisible(false)}>ACKNOWLEDGE</button><button className="danger-button" onClick={() => notify("Incident SCR-12 opened")}>{manager ? "INVESTIGATE SCR-12 →" : "VIEW DETAILS →"}</button></section>}
        <section className="dashboard-heading"><div><p>◎ {manager ? "MANAGED SCOPE · SGN-04 TECH PARK" : "OWN / ASSIGNED SCOPE · MS-8849"}</p><h1>{manager ? <>Operations Manager<br />Command Center</> : <>My Mission Operations<br />Workspace</>}</h1><span className="scope-note">{manager ? "Fleet-wide approvals, alerts, readiness and team coordination" : "Only missions and UAV records assigned to you are shown"}</span></div><div className="feed-tabs">{(["live","gcs","simulation"] as const).map(item => <button key={item} className={feed === item ? "active" : ""} onClick={() => setFeed(item)}>{item === "live" ? "LIVE FEED" : item === "gcs" ? "GCS SYNC" : "SIMULATION"}</button>)}</div><button className="preset-button" onClick={() => notify(manager ? "Managed sector filters opened" : "Assigned mission filters opened")}>⌁ {manager ? "MANAGED SCOPE" : "MY SCOPE"}</button><button className="new-mission" onClick={() => notify(manager ? "Pending approval queue opened" : "New mission workflow started")}>{manager ? "◈ REVIEW 3 APPROVALS" : "⊕ NEW MISSION"}</button></section>
        <section className="metric-grid" aria-label="Operational summary">
          <article><span>{manager ? "ACTIVE FLIGHTS" : "MY ACTIVE FLIGHTS"}</span><Icon>⌁</Icon><strong>{manager ? "3" : "1"}<small>{manager ? "/ 8 IN AIR" : "ASSIGNED"}</small></strong><p className="ok">● MAVLink Streaming <em>{manager ? "37.5% LOAD" : "MS-8849"}</em></p><div className="progress"><i style={{width:manager?"38%":"82%"}} /></div></article>
          <article><span>{manager ? "MISSIONS TODAY" : "MY MISSIONS TODAY"}</span><Icon>◉</Icon><strong>{manager ? "14" : "3"}<small>TOTAL LOG</small></strong><div className="mini-stats"><b>{manager ? "9" : "2"}<small>DONE</small></b><b>{manager ? "2" : "1"}<small>PENDING</small></b><b>{manager ? "1" : "0"}<small>REVISE</small></b></div></article>
          <article><span>AIRSPACE SAFETY</span><Icon>♢</Icon><strong className="warn">1<small>GEO ALERT</small></strong><p>Collision Risk: <b className="ok">0 CONFLICTS</b><br />Geofence Status: <b className="warn">1 BREACH LOG</b></p></article>
          <article><span>{manager ? "FLEET READINESS" : "ASSIGNED UAV"}</span><Icon>⚑</Icon><strong className="ok">{manager ? "87.5%" : "READY"}<small>OPERATIONAL</small></strong><p>{manager ? "7 Drones Ready" : "UAV-01 · M350 RTK"} <em>{manager ? "1 Maint." : "82% BAT"}</em></p><div className="progress"><i style={{width:manager?"88%":"82%"}} /></div></article>
          <article><span>TELEMETRY INGEST</span><Icon>⌁</Icon><strong className="blue">100%<small>STABLE</small></strong><p>Avg Latency: <b>0.8s</b><br />Jitter Variance: <b className="ok">±12ms</b></p></article>
        </section>
        <div className="operations-grid">
          <section className="map-panel"><header><b>◎ TAC-CANVAS // SGN-09 HIGH TECH INDUSTRIAL PARK</b><span>10.8490° N, 106.7725° E</span><em>{manager ? "UTM FEED ACTIVE" : "ASSIGNED ROUTE"}</em></header><div className="tactical-map"><span className="street s1"/><span className="street s2"/><span className="street s3"/><span className="street s4"/><div className="geo-zone"><span>ZONE BRAVO GEOFENCE PERIMETER</span></div><div className="drone-marker m1"><i>➤</i><b>UAV-01 (M350 RTK)</b><span>ALT: 120m | 14.2 m/s<br/>BAT: 82% | LINK: 98%</span></div>{manager && <div className="drone-marker m2"><i>➤</i><b>UAV-02 (Inspire 3)</b><span>ALT: 85m | 8.5 m/s<br/>BAT: 74% | LINK: 94%</span></div>}<div className="restricted">RESTRICTED FACILITY</div><div className="map-alert">!</div><div className="map-legend">ZOOM: 14.5x&nbsp; HDOP: 0.82&nbsp; SAT: 28 GLONASS/GPS <span>● M350 RTK {manager && <>&nbsp; <b>● INSPIRE 3</b></>} &nbsp; ● JITTER ALERT</span></div></div><footer><span>GCS UPTIME: 19h 42m 11s &nbsp; ENCRYPTION: <b>AES-CTR-256</b></span><div><button>DOWNLOAD KML LOG</button><button className="cyan-button">EXPAND HUD FULLSCREEN</button></div></footer></section>
          <aside className="insight-column"><section className="ai-panel"><header><Icon>◉</Icon><div><h2>AI Decision Engine (FE-09)</h2><span>NEURAL RISK MITIGATION // ACTIVE</span></div><b>EVAL 99.4%</b></header><article><h3>MS-8849 [Transmission Line Audit] <small>CONFIDENCE: 92%</small></h3><p>Moderate micro-burst &amp; wind shear predicted at <b>Waypoint 6 (2,400m NE)</b> within 18 minutes. Recommend reducing cruise speed by <b>15%</b>.</p><div><span>≋ Vector: 284° @ 16.4 kts</span><button onClick={() => notify("AI speed recommendation applied")}>APPLY SPEED OFFSET</button></div></article><article><h3>⚐ SMART RTH WINDOW CALCULATION</h3><p>UAV-01 has 34 mins remaining. Optimal RTH initiation at 15:05 UTC to retain 20% buffer.</p></article></section>
          {manager ? <section className="incident-panel"><header><h2>♧ Critical Incidents &amp;<br/>Approvals</h2><button>VIEW ALL (4)</button></header><article><span className="red-badge">COMM LOST LINK</span><time>00:01:24 AGO</time><h3>UAV-Alpha-04 · Signal Timeout &gt; 4.2s</h3><p>Exceeded standard 3.0s heartbeat packet window. Vehicle currently hovering at 64m AGL.</p><div><button>SILENCE (60S)</button><button className="danger-button">DISPATCH SCR-12</button></div></article><article><span className="blue-badge">SUBMITTED FOR APPROVAL</span><time>14:22 UTC</time><h3>Mission #MS-8842 // Pilot: DucDDA</h3><p>BVLOS Solar Array thermography flight path validated against NFZ.</p><b className="ok">Pre-check Passed (100%)</b></article></section> : <section className="incident-panel"><header><h2>♧ My Mission Alerts &amp;<br/>Validation</h2><button>VIEW MY ACTIVITY</button></header><article><span className="blue-badge">VALIDATION AVAILABLE</span><time>14:22 UTC</time><h3>Mission #MS-8849 // Version 03</h3><p>Your submitted route passed configured spatial checks and is awaiting manager review.</p><b className="ok">Pre-check Passed (100%)</b></article><article><span className="purple-badge">AI ADVISORY</span><time>14:31 UTC</time><h3>Weather risk near Waypoint 6</h3><p>Review the advisory and mission context. AI advice does not approve or command the UAV.</p></article></section>}</aside>
        </div>
        <section className="missions-panel"><header><div><Icon>⌁</Icon><h2>Active Airborne Missions<span>Real-time synchronized telemetry stream from active transponders</span></h2></div><label>⌕ <input value={query} onChange={e=>setQuery(e.target.value)} placeholder="Filter by callsign, pilot, phase..."/></label></header><div className="table-wrap"><table><thead><tr><th>MISSION ID</th><th>UAV ASSET</th><th>ASSIGNED PILOT</th><th>FLIGHT PHASE</th><th>TELEMETRY SYNC</th><th>LINK QUALITY</th><th>BATTERY</th><th>ACTION</th></tr></thead><tbody>{filtered.map(f => <tr key={f.id} className={f.danger?"danger-row":""}><td><b className="blue">{f.id}</b></td><td><b>{f.asset}</b><span>● {f.model}</span></td><td>{f.pilot}</td><td><b className="phase">{f.phase}</b><span>Alt: {f.alt}</span></td><td><b className={f.danger?"warn":"ok"}>● {f.sync}</b></td><td>{f.quality}% <i className="quality"><em style={{width:`${f.quality}%`}}/></i></td><td>▮ {f.battery}%</td><td><button onClick={()=>notify(`${f.id} monitor opened`)}>{f.danger?"RESOLVE":"MONITOR"}</button></td></tr>)}</tbody></table></div></section>
        <section className="bottom-grid"><article><header>PAYLOAD CAM 01 // UAV-01 <b>EO/IR 4K</b></header><div className="camera-feed"><span>IR: 42.1°C SP</span><i/><b>GIMBAL: -45° PITCH</b></div><footer>Target: Substation B-9 <button>EXPAND STREAM</button></footer></article><article><header>SGN-04 LOCAL WEATHER RADAR <b>METAR VFR</b></header><div className="weather"><p>SURFACE WIND<strong>11 kts / Gust 18</strong><span>Direction: 080° ENE</span></p><p>VISIBILITY<strong>&gt; 10 km</strong><span>Cloud Base: 3,500ft</span></p><p>QNH ALTIMETER<strong>1012 hPa</strong><span>Stable Trend</span></p><p>KP GEOMAGNETIC<strong>Kp 1.8 (Quiet)</strong><span>GPS Lock Optimal</span></p></div><footer>Updated 3m ago via Tan Son Nhat AWOS</footer></article>{manager ? <article><header>GCS CREW ASSIGNMENTS <b>3 ON-DUTY</b></header><ul><li><i/>Nguyen Long (PIC)<b>UAV-01</b></li><li><i/>Pham Tuan (PIC)<b>UAV-02</b></li><li><i className="alert-dot"/>Tran Hoang (PIC)<b className="warn">UAV-04 [EMERGENCY]</b></li></ul><footer>Shift Handover in 03h 22m <button>DUTY ROSTER</button></footer></article> : <article><header>MY ASSIGNMENT <b>MS-8849</b></header><ul><li><i/>Pilot in command<b>{account.displayName}</b></li><li><i/>Assigned aircraft<b>UAV-01</b></li><li><i/>Approval state<b className="warn">PENDING REVIEW</b></li></ul><footer>Version 03 validated <button>OPEN MISSION</button></footer></article>}</section>
      </main>
    </div>{toast&&<div className="ops-toast" role="status">{toast}</div>}
  </div>;
}
