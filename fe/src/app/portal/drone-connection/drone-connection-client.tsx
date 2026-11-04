"use client";

import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import Link from "next/link";
import { useEffect, useState } from "react";
import { getAssignedUavs, getPilotMissions } from "@/lib/portal/client";
import { forgetDroneGateway, createDroneConnection, getDroneConnections, getDroneBindings, type DroneBinding, revokeDroneConnection, type DroneConnection } from "@/lib/portal/drone-connections";
import type { AssignedUav, MissionDetail } from "@/lib/portal/types";
import styles from "./drone-connection.module.css";

const labels: Record<DroneConnection["status"],string> = {AwaitingGateway:"Waiting for gateway",PairingExpired:"Pairing code expired",Expired:"Session expired",Revoked:"Disconnected",Offline:"Gateway offline",GatewayConnected:"Gateway online · waiting for drone",ReceivingTelemetry:"Receiving drone telemetry"};
type Pairing = {connection:DroneConnection;pairingCode:string;gatewayPath:"/ws/drone-gateway"};
type BootstrapInfo = {backendOrigin:string;downloadOrigin:string;laptopHost:string};

export default function DroneConnectionClient({ initialMissionId, embedded = false }: { initialMissionId?: string; embedded?: boolean } = {}) {
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
  const [bootstrapInfo,setBootstrapInfo]=useState<BootstrapInfo|null>(null);
  const [error,setError]=useState(""); const [notice,setNotice]=useState("");
  const [now,setNow]=useState(()=>Date.now());
  useEffect(()=>{let active=true;void Promise.all([getAssignedUavs(),getPilotMissions(),getDroneConnections(),getDroneBindings()]).then(([a,m,c,b])=>{if(!active)return;setAircraft(a.filter(x=>x.status.toLowerCase()==="active"));setMissions(m.filter(x=>["approved","inprogress"].includes(x.status.toLowerCase().replaceAll(" ",""))));setConnections(c);setBindings(b);const chosen=m.find(x=>x.id===initialMissionId);if(chosen){setMission(chosen.id);setUav(chosen.uavId??"");}}).catch(e=>{if(active)setError(e.message);}).finally(()=>{if(active)setLoading(false);});return()=>{active=false;};},[initialMissionId]);
  useEffect(()=>{let active=true;void fetch("/api/gateway-bootstrap",{cache:"no-store"}).then(async response=>{if(!response.ok)throw new Error("Unable to determine the hosted backend address.");return response.json() as Promise<BootstrapInfo>;}).then(info=>{if(active)setBootstrapInfo(info);}).catch(e=>{if(active)setError(e instanceof Error?e.message:"Unable to prepare the Jetson command.");});return()=>{active=false;};},[]);
  useEffect(()=>{let active=true;const timer=setInterval(()=>{setNow(Date.now());void Promise.all([getDroneConnections(),getDroneBindings()]).then(([c,b])=>{if(active){setConnections(c);setBindings(b);setStatusError("");}}).catch(()=>{if(active)setStatusError("Connection status could not be refreshed. Check your network and reload.");});},5000);return()=>{active=false;clearInterval(timer);};},[]);
  const eligible=missions.filter(m=>m.uavId===uav);
  const pairedState=pairing?connections.find(c=>c.id===pairing.connection.id):null;
  const codeVisible=pairing && !pairedState?.pairedAt && !pairedState?.revokedAt && new Date(pairing.connection.codeExpiresAt).getTime()>now;
  const jetsonCommand=pairing&&bootstrapInfo?`curl -fsSL '${bootstrapInfo.downloadOrigin}/downloads/bootstrap-jetson.sh' -o /tmp/droneops-bootstrap.sh && sudo bash /tmp/droneops-bootstrap.sh --server '${bootstrapInfo.backendOrigin}' --package-url '${bootstrapInfo.downloadOrigin}/downloads/droneops-gateway.zip' --otp '${pairing.pairingCode}'`:"";
  const radioWindowsCommand = "Open connect-drone.cmd in the extracted droneops-gateway folder";
  const radioUnixCommand = "bash connect-drone.sh";
  const binding = bindings.find(item => item.uavId === uav);
  async function forgetGateway() {
    if (!uav || !window.confirm("Forget this trusted gateway and disconnect its sessions? The flight-controller UID binding will remain.")) return;
    setBusy(true); setError("");
    try { await forgetDroneGateway(uav); setBindings(await getDroneBindings()); setConnections(await getDroneConnections()); setNotice("Trusted gateway forgotten. Pair once again on the device to register it."); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to forget gateway."); }
    finally { setBusy(false); }
  }
  async function connect(){setBusy(true);setError("");setNotice("");try{const result=await createDroneConnection(uav,mission);setPairing(result);setConnections(await getDroneConnections());}catch(e){setError(e instanceof Error?e.message:"Unable to create pairing.");}finally{setBusy(false);}}
  async function disconnect(id:string){setBusy(true);setError("");try{await revokeDroneConnection(id);if(pairing?.connection.id===id)setPairing(null);setConnections(await getDroneConnections());setNotice("Connection revoked. The gateway can no longer send telemetry through this session.");}catch(e){setError(e instanceof Error?e.message:"Unable to disconnect.");}finally{setBusy(false);}}
  return <div className={styles.page}>
    {!embedded && <header className="module-heading"><div><p className="module-kicker">PILOT / DRONE CONNECTION</p><h1>Connect your drone</h1><p>Choose an onboard Jetson or a telemetry radio connected to your laptop. Both use the flight controller hardware UID and send telemetry to Live Monitoring. No camera or IP configuration required.</p></div><Link className="module-secondary module-link" href="/portal/live-monitoring">OPEN LIVE MONITORING →</Link></header>}
    {error&&<p className={styles.error} role="alert">{error}</p>}{statusError&&<p className={styles.error} role="status">{statusError}</p>}{notice&&<p className={styles.notice} role="status">{notice}</p>}
    <div className={styles.grid}>
      <section className={styles.card}><span className={styles.step}>01 · CONNECTION PROFILE</span><h2>Choose your connection</h2>
        <div className={styles.choice} role="group" aria-label="Connection profile">
          <button type="button" aria-pressed={profile==="jetson"} onClick={()=>setProfile("jetson")}><strong>JETSON ONBOARD</strong><span>Gateway runs on the drone and reconnects after power-on.</span></button>
          <button type="button" aria-pressed={profile==="radio"} onClick={()=>setProfile("radio")}><strong>RADIO TELEMETRY</strong><span>No Jetson: a telemetry radio sends data to your laptop.</span></button>
        </div>
        <a className="module-secondary module-link" href="/downloads/droneops-gateway.zip" download>DOWNLOAD GATEWAY + SETUP GUIDE</a>
        {profile==="jetson"?<>
          <h3>Install once · reconnect on every boot</h3>
          <p>Connect the flight controller to the Jetson using a data USB cable or a 3.3 V USB–UART adapter on TELEM2. Configure the controller for MAVLink and save an Internet connection on the Jetson.</p>
          <p>Create a connection in step 02, then run the generated setup command once on the Jetson. It detects USB serial devices, installs the router and gateway, and registers this trusted gateway. If several adapters are connected, choose the correct one by number.</p>
          <p>After setup, close SSH and the terminal. The router and gateway start automatically when the Jetson powers on and retry after network or serial interruptions. The backend is hosted online; no laptop server or IP entry is needed.</p>
        </>:<>
          <h3>USB telemetry radio · no IP configuration</h3>
          <p>Connect the air radio to the flight controller MAVLink port and plug its matching ground radio into the laptop by USB. Power the drone and ensure the laptop has Internet.</p>
          <p>Install Python 3.9 or later, download and extract the gateway ZIP, then open <code>connect-drone.cmd</code> on Windows or run <code>bash connect-drone.sh</code> on macOS/Linux.</p>
          <p>The launcher creates its Python environment, lists USB ports, detects an ArduPilot/PX4 heartbeat and common baud rates, then asks for the web pairing code on first use. It uses the fixed backend hostname automatically. Keep its window open and the laptop awake while using the radio.</p>
          <p>Close Mission Planner/QGroundControl if it owns the serial port. To use a GCS simultaneously, configure a local MAVLink router as described in the download guide.</p>
        </>}
      </section>
      <section className={styles.card}><span className={styles.step}>02 · SELECT AIRCRAFT</span><h2>Choose the UAV and mission</h2><p>Choose an active UAV and an approved or in-progress mission. Create the code only when your gateway is ready because it expires in five minutes.</p>
        <label>UAV<select value={uav} disabled={busy||loading} onChange={e=>{setUav(e.target.value);setMission("");setBindingConfirmed(false);}}><option value="">Select UAV</option>{aircraft.map(a=><option key={a.id} value={a.id}>{a.code} · {a.name} · {bindings.some(b=>b.uavId===a.id)?"Hardware bound":"Not yet bound"}</option>)}</select></label>
        <label>Mission<select value={mission} disabled={!uav||busy||loading} onChange={e=>setMission(e.target.value)}><option value="">Select mission</option>{eligible.map(m=><option key={m.id} value={m.id}>{m.name} · {m.status}</option>)}</select></label>
        {uav&&!eligible.length&&<p>No eligible mission for this UAV. <Link href="/portal/missions">View your missions</Link></p>}
        {uav&&<div className={styles.empty}><strong>{bindings.find(b=>b.uavId===uav)?.hardwareIdentity??"First hardware binding"}</strong><p>{bindings.some(b=>b.uavId===uav)?"Only the physical drone with this autopilot UID can connect to this UAV record.":"The first connected autopilot UID will be permanently assigned to this UAV. That drone cannot be paired to another registered UAV."}</p>{!bindings.some(b=>b.uavId===uav)&&<label><input type="checkbox" checked={bindingConfirmed} onChange={e=>setBindingConfirmed(e.target.checked)}/> I confirm this registry entry is for the physical drone I am connecting.</label>}</div>}
        {binding?.gatewayRegistered && <div className={styles.notice}><strong>Trusted gateway registered</strong><p>Power on the Jetson or open the radio launcher, then select this mission and create its connection. The saved gateway can claim it automatically without entering another code.</p><button type="button" className="module-secondary" disabled={busy} onClick={() => void forgetGateway()}>FORGET TRUSTED GATEWAY</button></div>}
        <p>A connection is scoped to this UAV and mission. Creating another connection revokes the previous mission session; it never starts a flight.</p><button className="module-primary" disabled={busy||loading||!uav||!mission||(!bindings.some(b=>b.uavId===uav)&&!bindingConfirmed)} onClick={()=>void connect()}>{busy?"PLEASE WAIT…":binding?.gatewayRegistered ? "CONNECT SELECTED MISSION" : "CREATE PAIRING CODE"}</button>
      </section>
    </div>
    <section className={styles.card}><span className={styles.step}>03 · PAIR THIS MISSION</span><h2>{profile==="jetson"?"Copy one command to your Jetson":"Enter the code on your laptop"}</h2>
      {binding?.gatewayRegistered && <p>A registered gateway reconnects automatically to the connection you selected on the web. Reinstallation or forgetting the gateway requires a new first-time pairing.</p>}
      {profile==="jetson"?<><p>First setup only: copy the command to the Jetson terminal (directly or by SSH). It uses the hosted backend hostname, registers the gateway and enables startup services. When it finishes, you may close the terminal.</p>
      {codeVisible&&jetsonCommand?<div className={styles.code}><pre>{jetsonCommand}</pre><button type="button" onClick={()=>void navigator.clipboard.writeText(jetsonCommand).then(()=>setNotice("Jetson setup command copied.")).catch(()=>setNotice("Select and copy the command manually."))}>COPY JETSON COMMAND</button></div>:<div className={styles.empty}>For first setup, create a pairing code to generate the Jetson command.</div>}
      <p>Later boots: power the flight controller and Jetson, provide Internet, and select the approved mission on the web. No SSH or terminal is needed.</p></>:<><p>Run the launcher inside the extracted gateway folder. Select your USB radio if prompted; enter the one-time web code on first use.</p>
      <h3>Windows</h3><pre>{radioWindowsCommand}</pre>
      <h3>macOS / Linux</h3><pre>{radioUnixCommand}</pre>
      <p>The launcher finds the port and common baud rates. If detection fails, check radio pairing, MAVLink port configuration and power. No IP entry is needed.</p></>}
      <p>The pairing code expires in five minutes. Mission credentials are short-lived; trusted gateways request a fresh mission lease automatically after reboot or expiry. Disconnect stops the selected connection. Forget trusted gateway revokes unattended access while preserving the permanent hardware binding.</p>
      {pairing&&<p>Gateway endpoint: <code>{pairing.gatewayPath}</code> · Transport: <code>{pairing.connection.gatewayTransport}</code></p>}
      {codeVisible?<div className={styles.code}><span>ONE-TIME CODE · EXPIRES IN {Math.max(0,Math.ceil((new Date(pairing.connection.codeExpiresAt).getTime()-now)/1000))}s</span><strong>{pairing.pairingCode.match(/.{1,4}/g)?.join("-")}</strong><button type="button" onClick={()=>void navigator.clipboard.writeText(pairing.pairingCode).then(()=>setNotice("Pairing code copied.")).catch(()=>setNotice("Select and copy the code manually."))}>Copy code</button></div>:<div className={styles.empty}>{pairing?"Code used or expired. Check the session below or create a new code.":"Prepare your chosen gateway, then select a UAV and mission to create a code."}</div>}
    </section>
    <section className={styles.card}><span className={styles.step}>04 · VERIFY CONNECTION</span><h2>Your connection sessions</h2><p>Gateway online means the Jetson or laptop has reached the backend. Receiving telemetry means fresh autopilot samples are arriving. Pairing never arms or starts the flight.</p>
      {loading?<p>Loading…</p>:!connections.length?<div className={styles.empty}>No sessions yet.</div>:<div className={styles.sessions}>{connections.map(c=><article key={c.id}><div><strong>{labels[c.status]}</strong><p>{aircraft.find(a=>a.id===c.uavId)?.code??c.uavId} · {missions.find(m=>m.id===c.missionId)?.name??c.missionId}</p><small>{c.gatewayTransport} · {c.hardwareIdentity??"Hardware not identified"} · {c.gatewayName??"Gateway not paired"}{c.pairedAt?` · MAVLink ${c.systemId}/${c.componentId}`:""}</small><small>Last gateway contact: {c.lastGatewayAt?formatVietnamDateTime(c.lastGatewayAt):"None"} · Last sample: {c.lastSampleAt?formatVietnamDateTime(c.lastSampleAt):"None"}</small><small>Session expires: {formatVietnamDateTime(c.expiresAt)}</small></div>{!c.revokedAt&&<button className="module-secondary" disabled={busy} onClick={()=>void disconnect(c.id)}>DISCONNECT</button>}</article>)}</div>}
    </section>
  </div>;
}
