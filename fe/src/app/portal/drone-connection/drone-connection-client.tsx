"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { getAssignedUavs, getPilotMissions } from "@/lib/portal/client";
import { createDroneConnection, getDroneConnections, getDroneBindings, type DroneBinding, revokeDroneConnection, type DroneConnection } from "@/lib/portal/drone-connections";
import type { AssignedUav, MissionDetail } from "@/lib/portal/types";
import styles from "./drone-connection.module.css";

const labels: Record<DroneConnection["status"],string> = {AwaitingGateway:"Waiting for gateway",PairingExpired:"Pairing code expired",Expired:"Session expired",Revoked:"Disconnected",Offline:"Gateway offline",GatewayConnected:"Gateway online · waiting for drone",ReceivingTelemetry:"Receiving drone telemetry"};
type Pairing = {connection:DroneConnection;pairingCode:string;gatewayPath:"/ws/drone-gateway"};

export default function DroneConnectionClient() {
  const [bindings,setBindings]=useState<DroneBinding[]>([]);
  const [bindingConfirmed,setBindingConfirmed]=useState(false);
  const [aircraft,setAircraft]=useState<AssignedUav[]>([]);
  const [missions,setMissions]=useState<MissionDetail[]>([]);
  const [connections,setConnections]=useState<DroneConnection[]>([]);
  const [uav,setUav]=useState(""); const [mission,setMission]=useState("");
  const [profile,setProfile]=useState<"jetson"|"radio">("jetson");
  const [pairing,setPairing]=useState<Pairing|null>(null);
  const [busy,setBusy]=useState(false); const [loading,setLoading]=useState(true);
  const [statusError,setStatusError]=useState("");
  const [error,setError]=useState(""); const [notice,setNotice]=useState("");
  const [now,setNow]=useState(()=>Date.now());
  useEffect(()=>{let active=true;void Promise.all([getAssignedUavs(),getPilotMissions(),getDroneConnections(),getDroneBindings()]).then(([a,m,c,b])=>{if(!active)return;setAircraft(a.filter(x=>x.status.toLowerCase()==="active"));setMissions(m.filter(x=>["approved","inprogress"].includes(x.status.toLowerCase().replaceAll(" ",""))));setConnections(c);setBindings(b);}).catch(e=>{if(active)setError(e.message);}).finally(()=>{if(active)setLoading(false);});return()=>{active=false;};},[]);
  useEffect(()=>{let active=true;const timer=setInterval(()=>{setNow(Date.now());void Promise.all([getDroneConnections(),getDroneBindings()]).then(([c,b])=>{if(active){setConnections(c);setBindings(b);setStatusError("");}}).catch(()=>{if(active)setStatusError("Connection status could not be refreshed. Check your network and reload.");});},5000);return()=>{active=false;clearInterval(timer);};},[]);
  const eligible=missions.filter(m=>m.uavId===uav);
  const pairedState=pairing?connections.find(c=>c.id===pairing.connection.id):null;
  const codeVisible=pairing && !pairedState?.pairedAt && !pairedState?.revokedAt && new Date(pairing.connection.codeExpiresAt).getTime()>now;
  async function connect(){setBusy(true);setError("");setNotice("");try{const result=await createDroneConnection(uav,mission);setPairing(result);setConnections(await getDroneConnections());}catch(e){setError(e instanceof Error?e.message:"Unable to create pairing.");}finally{setBusy(false);}}
  async function disconnect(id:string){setBusy(true);setError("");try{await revokeDroneConnection(id);if(pairing?.connection.id===id)setPairing(null);setConnections(await getDroneConnections());setNotice("Connection revoked. The gateway can no longer send telemetry through this session.");}catch(e){setError(e instanceof Error?e.message:"Unable to disconnect.");}finally{setBusy(false);}}
  return <div className={styles.page}>
    <header className="module-heading"><div><p className="module-kicker">PILOT / DRONE CONNECTION</p><h1>Connect your drone</h1><p>Choose an onboard Jetson or a telemetry radio connected to your laptop. Both send drone telemetry to Live Monitoring. No camera required.</p></div><Link className="module-secondary module-link" href="/portal/live-monitoring">OPEN LIVE MONITORING →</Link></header>
    {error&&<p className={styles.error} role="alert">{error}</p>}{statusError&&<p className={styles.error} role="status">{statusError}</p>}{notice&&<p className={styles.notice} role="status">{notice}</p>}
    <div className={styles.grid}>
      <section className={styles.card}><span className={styles.step}>01 · CONNECTION PROFILE</span><h2>Choose your connection</h2>
        <div className={styles.choice} role="group" aria-label="Connection profile">
          <button type="button" aria-pressed={profile==="jetson"} onClick={()=>setProfile("jetson")}><strong>JETSON ONBOARD</strong><span>Gateway runs on the drone and reconnects automatically.</span></button>
          <button type="button" aria-pressed={profile==="radio"} onClick={()=>setProfile("radio")}><strong>RADIO TELEMETRY</strong><span>No Jetson: a telemetry radio sends data to your laptop.</span></button>
        </div>
        <a className="module-secondary module-link" href="/downloads/droneops-gateway.zip" download>DOWNLOAD GATEWAY + SETUP GUIDE</a>
        {profile==="jetson"?<>
          <h3>One-time Jetson setup</h3>
          <p>Connect Pixhawk TELEM2 through a 3.3 V USB-UART adapter. Save your Wi-Fi connection on the Jetson, then copy and extract the download there. Open CONNECTION_SETUP.md for wiring and firmware settings.</p>
          <pre>{`unzip droneops-gateway.zip -d ~/droneops-package
cd ~/droneops-package/droneops-gateway
sudo bash install-jetson.sh`}</pre>
          <p>The installer asks for the Pixhawk port, matching baud rate, system ID and backend address. Optionally enter your laptop IP for QGroundControl. Internet is needed during installation.</p>
          <p>Both background services start automatically. Before pairing, the Jetson waits for setup; no terminal needs to remain open afterward.</p>
        </>:<>
          <h3>Telemetry radio via laptop</h3><p>Connect the air radio to the flight controller TELEM2 port. Plug its paired ground radio into your laptop by USB. Match the radio serial baud and configure the flight controller port for MAVLink.</p>
          <p>Extract the download on your laptop and install Python 3.9+. Run these commands inside the extracted droneops-gateway folder:</p>
          <pre>{`python -m venv .venv
# Windows:
.venv\\Scripts\\python.exe -m pip install -r requirements.txt
# Linux/macOS:
.venv/bin/python -m pip install -r requirements.txt`}</pre>
          <p>Use the gateway command in step 03. Keep the laptop awake, its Internet connection available, both radios powered and the gateway running. This profile does not install Jetson services.</p>
          <p>For QGroundControl or Mission Planner at the same time, use the forwarding instructions in the included guide. Only one program can open the radio serial port.</p>
        </>}
      </section>
      <section className={styles.card}><span className={styles.step}>02 · SELECT AIRCRAFT</span><h2>Choose the UAV and mission</h2><p>Choose an active UAV and an approved or in-progress mission. Create the code only when your gateway is ready because it expires in five minutes.</p>
        <label>UAV<select value={uav} disabled={busy||loading} onChange={e=>{setUav(e.target.value);setMission("");setBindingConfirmed(false);}}><option value="">Select UAV</option>{aircraft.map(a=><option key={a.id} value={a.id}>{a.code} · {a.name} · {bindings.some(b=>b.uavId===a.id)?"Hardware bound":"Not yet bound"}</option>)}</select></label>
        <label>Mission<select value={mission} disabled={!uav||busy||loading} onChange={e=>setMission(e.target.value)}><option value="">Select mission</option>{eligible.map(m=><option key={m.id} value={m.id}>{m.name} · {m.status}</option>)}</select></label>
        {uav&&!eligible.length&&<p>No eligible mission for this UAV. <Link href="/portal/missions">View your missions</Link></p>}
        {uav&&<div className={styles.empty}><strong>{bindings.find(b=>b.uavId===uav)?.hardwareIdentity??"First hardware binding"}</strong><p>{bindings.some(b=>b.uavId===uav)?"Only the physical drone with this autopilot UID can connect to this UAV record.":"The first connected autopilot UID will be permanently assigned to this UAV. That drone cannot be paired to another registered UAV."}</p>{!bindings.some(b=>b.uavId===uav)&&<label><input type="checkbox" checked={bindingConfirmed} onChange={e=>setBindingConfirmed(e.target.checked)}/> I confirm this registry entry is for the physical drone I am connecting.</label>}</div>}
        <p>Creating a new code revokes any existing connection for this UAV.</p><button className="module-primary" disabled={busy||loading||!uav||!mission||(!bindings.some(b=>b.uavId===uav)&&!bindingConfirmed)} onClick={()=>void connect()}>{busy?"PLEASE WAIT…":"CREATE PAIRING CODE"}</button>
      </section>
    </div>
    <section className={styles.card}><span className={styles.step}>03 · PAIR THIS MISSION</span><h2>{profile==="jetson"?"Enter the code on your Jetson":"Enter the code on your laptop"}</h2>
      {profile==="jetson"?<><p>Run this helper on the Jetson and enter the code below. It verifies the Pixhawk identity, saves the session and returns to the background service. You can close the terminal once it succeeds.</p>
      <pre>sudo /opt/droneops-gateway/pair-jetson.sh</pre>
      <p>For later boots, just supply power and your saved network.</p></>:<><p>Replace the backend address, radio serial port, baud and system ID with your values. Enter the code when prompted. Windows example (COM5 and 57600 are placeholders):</p>
      <pre>{`.venv\\Scripts\\python.exe gateway.py --server https://YOUR-BACKEND --device COM5 --baud 57600 --system-id 1 --new-pairing`}</pre>
      <p>On Linux/macOS use <code>.venv/bin/python</code> and the radio device path, such as <code>/dev/ttyUSB0</code>. Keep this process running to send telemetry. To restart the same session, omit <code>--new-pairing</code>.</p></>}
      <p>A new code is needed after the 12-hour session expires, after Disconnect, or when changing mission. Network recovery alone does not require pairing again. Switching this setup profile does not revoke or change an existing session.</p>
      {pairing&&<p>Gateway endpoint: <code>{pairing.gatewayPath}</code> · Transport: <code>{pairing.connection.gatewayTransport}</code></p>}
      {codeVisible?<div className={styles.code}><span>ONE-TIME CODE · EXPIRES IN {Math.max(0,Math.ceil((new Date(pairing.connection.codeExpiresAt).getTime()-now)/1000))}s</span><strong>{pairing.pairingCode.match(/.{1,4}/g)?.join("-")}</strong><button type="button" onClick={()=>void navigator.clipboard.writeText(pairing.pairingCode).then(()=>setNotice("Pairing code copied.")).catch(()=>setNotice("Select and copy the code manually."))}>Copy code</button></div>:<div className={styles.empty}>{pairing?"Code used or expired. Check the session below or create a new code.":"Prepare your chosen gateway, then select a UAV and mission to create a code."}</div>}
    </section>
    <section className={styles.card}><span className={styles.step}>04 · VERIFY CONNECTION</span><h2>Your connection sessions</h2><p>Gateway online means the Jetson or laptop has reached the backend. Receiving telemetry means fresh autopilot samples are arriving. Pairing never arms or starts the flight.</p>
      {loading?<p>Loading…</p>:!connections.length?<div className={styles.empty}>No sessions yet.</div>:<div className={styles.sessions}>{connections.map(c=><article key={c.id}><div><strong>{labels[c.status]}</strong><p>{aircraft.find(a=>a.id===c.uavId)?.code??c.uavId} · {missions.find(m=>m.id===c.missionId)?.name??c.missionId}</p><small>{c.gatewayTransport} · {c.hardwareIdentity??"Hardware not identified"} · {c.gatewayName??"Gateway not paired"}{c.pairedAt?` · MAVLink ${c.systemId}/${c.componentId}`:""}</small><small>Last gateway contact: {c.lastGatewayAt?new Date(c.lastGatewayAt).toLocaleString():"None"} · Last sample: {c.lastSampleAt?new Date(c.lastSampleAt).toLocaleString():"None"}</small><small>Session expires: {new Date(c.expiresAt).toLocaleString()}</small></div>{!c.revokedAt&&<button className="module-secondary" disabled={busy} onClick={()=>void disconnect(c.id)}>DISCONNECT</button>}</article>)}</div>}
    </section>
  </div>;
}
