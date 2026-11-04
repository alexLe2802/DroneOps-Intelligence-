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
type BootstrapInfo = {backendOrigin:string;downloadOrigin:string};

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
    {!embedded && <header className="module-heading"><div><p className="module-kicker">PILOT / DRONE CONNECTION</p><h1>Connect your drone to DroneOps</h1><p>Use an onboard Jetson or a telemetry radio connected to your laptop to send drone data to the hosted DroneOps system. The gateway device needs Internet access. Generate a pairing code on this website to register it.</p></div><Link className="module-secondary module-link" href="/portal/live-monitoring">OPEN LIVE MONITORING →</Link></header>}
    {error&&<p className={styles.error} role="alert">{error}</p>}{statusError&&<p className={styles.error} role="status">{statusError}</p>}{notice&&<p className={styles.notice} role="status">{notice}</p>}
    <div className={styles.grid}>
      <section className={styles.card}><span className={styles.step}>01 · PREPARE YOUR CONNECTION</span><h2>How will your drone connect?</h2>
        <div className={styles.choice} role="group" aria-label="Connection profile">
          <button type="button" aria-pressed={profile==="jetson"} onClick={()=>setProfile("jetson")}><strong>JETSON ONBOARD</strong><span>Install once on the Jetson; reconnect automatically after power-on.</span></button>
          <button type="button" aria-pressed={profile==="radio"} onClick={()=>setProfile("radio")}><strong>RADIO TELEMETRY</strong><span>Use a telemetry radio and laptop to send drone data to DroneOps.</span></button>
        </div>
        <a className="module-secondary module-link" href="/downloads/droneops-gateway.zip" download>DOWNLOAD GATEWAY + SETUP GUIDE</a>
        {profile==="jetson"?<>
          <h3>With Jetson: one-time onboard setup</h3>
          <ol className={styles.instructions}>
            <li>Power the flight controller and Jetson. Connect them with a data USB cable, or connect TELEM2 through a 3.3 V USB–UART adapter. Configure the flight controller port for MAVLink.</li>
            <li>Connect the Jetson to the Internet over Wi-Fi or Ethernet. Save the network so it reconnects after the next power-on.</li>
            <li>Select your UAV and mission in step 02. For the first registration, paste the command from step 03 into a terminal on the Jetson or an SSH session connected to it.</li>
          </ol>
          <p>Once installation and pairing succeed, you can close the terminal or SSH session. On later boots, power the devices, wait for the Jetson to connect to the Internet, and select the mission on this website. The gateway reconnects automatically.</p>
        </>:<>
          <h3>Without Jetson: telemetry radio and laptop</h3>
          <ol className={styles.instructions}>
            <li>Connect the air radio to the flight controller MAVLink port and plug the matching ground radio into your laptop by USB. Power the drone and connect the laptop to the Internet.</li>
            <li>Install Python 3.9 or later, download the gateway above, and extract it. On Windows, open <code>connect-drone.cmd</code>. On macOS/Linux, open a terminal in the extracted gateway folder and run <code>bash connect-drone.sh</code>.</li>
            <li>The launcher detects the radio port and ArduPilot/PX4 autopilot. If several USB ports are listed, select the ground radio by number. Choose your UAV and mission on this website, generate a code, and enter it when the launcher requests it during first-time pairing.</li>
          </ol>
          <p>Keep the gateway window open, the laptop connected to the Internet, and sleep mode disabled. For later sessions, reopen the launcher and select the mission on this website.</p>
          <p>If the drone is not detected, check its power, USB cable, and radio pairing. Close Mission Planner or QGroundControl if it is using the same radio port.</p>
        </>}
      </section>
      <section className={styles.card}><span className={styles.step}>02 · SELECT YOUR DRONE AND MISSION</span><h2>Choose your registered UAV</h2><p>Select an active UAV and an approved or in-progress mission within its flight window. Prepare the Jetson or radio launcher before generating a pairing code. The code expires after five minutes.</p>
        <label>UAV<select value={uav} disabled={busy||loading} onChange={e=>{setUav(e.target.value);setMission("");setBindingConfirmed(false);}}><option value="">Select UAV</option>{aircraft.map(a=><option key={a.id} value={a.id}>{a.code} · {a.name} · {bindings.some(b=>b.uavId===a.id)?"Hardware bound":"Not yet bound"}</option>)}</select></label>
        <label>Mission<select value={mission} disabled={!uav||busy||loading} onChange={e=>setMission(e.target.value)}><option value="">Select mission</option>{eligible.map(m=><option key={m.id} value={m.id}>{m.name} · {m.status}</option>)}</select></label>
        {uav&&!eligible.length&&<p>No eligible mission is available for this UAV. <Link href="/portal/missions">View your missions</Link></p>}
        {uav&&<div className={styles.empty}><strong>{bindings.find(b=>b.uavId===uav)?.hardwareIdentity??"First hardware binding"}</strong><p>{bindings.some(b=>b.uavId===uav)?"Only the flight controller with this UID can connect to the selected UAV.":"The first connected flight controller UID is permanently bound to this UAV. The same flight controller cannot be paired with another registered UAV."}</p>{!bindings.some(b=>b.uavId===uav)&&<label><input type="checkbox" checked={bindingConfirmed} onChange={e=>setBindingConfirmed(e.target.checked)}/> I confirm that the selected UAV is the physical drone I am connecting.</label>}</div>}
        {binding?.gatewayRegistered && <div className={styles.notice}><strong>Trusted gateway registered</strong><p>Power on the Jetson or open the radio launcher, then select the mission and click CONNECT SELECTED MISSION. The registered gateway claims the new connection automatically without another pairing code.</p><button type="button" className="module-secondary" disabled={busy} onClick={() => void forgetGateway()}>FORGET TRUSTED GATEWAY</button></div>}
        <p>The new connection is scoped to the selected UAV and mission and replaces the previous mission session. Connecting does not arm or launch the drone.</p><button className="module-primary" disabled={busy||loading||!uav||!mission||(!bindings.some(b=>b.uavId===uav)&&!bindingConfirmed)} onClick={()=>void connect()}>{busy?"PLEASE WAIT…":binding?.gatewayRegistered ? "CONNECT SELECTED MISSION" : "CREATE PAIRING CODE"}</button>
      </section>
    </div>
    <section className={styles.card}><span className={styles.step}>03 · FIRST-TIME PAIRING</span><h2>{binding?.gatewayRegistered?"Your gateway is ready to reconnect":profile==="jetson"?"Copy the setup command to your Jetson":"Open the launcher and enter the pairing code"}</h2>
      {binding?.gatewayRegistered && <p>The registered gateway reconnects to the mission you selected. Pair again only after reinstalling the device or forgetting the gateway.</p>}
      {profile==="jetson"?<><p>For the first installation, click COPY JETSON COMMAND and paste it into a terminal on the Jetson or an SSH session. The command uses the configured DroneOps online address, downloads the gateway, and enables automatic startup services.</p>
      {codeVisible&&jetsonCommand?<div className={styles.code}><pre>{jetsonCommand}</pre><button type="button" onClick={()=>void navigator.clipboard.writeText(jetsonCommand).then(()=>setNotice("Jetson setup command copied.")).catch(()=>setNotice("Select and copy the command manually."))}>COPY JETSON COMMAND</button></div>:<div className={styles.empty}>{binding?.gatewayRegistered?"Select the mission in step 02, then check the connection status below.":"Generate a pairing code in step 02 to prepare the Jetson setup command."}</div>}
      <p>After pairing succeeds, close the terminal or SSH session. For later sessions, power the flight controller and Jetson, wait for Internet access, and select the mission on this website.</p></>:<><p>For first-time pairing, open the launcher in the extracted gateway folder and enter the code below when prompted. For later sessions, reopen the launcher to use the saved registration.</p>
      <h3>Windows</h3><pre>{radioWindowsCommand}</pre>
      <h3>macOS / Linux</h3><pre>{radioUnixCommand}</pre>
      <p>Select the ground radio port by number if prompted. Wait for pairing to succeed, and keep the gateway window open while monitoring the drone.</p></>}
      <p>Each pairing code can be used once and expires after five minutes. If it expires, generate a new code in step 02. DISCONNECT ends the mission connection. FORGET TRUSTED GATEWAY revokes the gateway registration while preserving the UAV hardware UID binding.</p>
      {codeVisible?<div className={styles.code}><span>ONE-TIME CODE · EXPIRES IN {Math.max(0,Math.ceil((new Date(pairing.connection.codeExpiresAt).getTime()-now)/1000))}s</span><strong>{pairing.pairingCode.match(/.{1,4}/g)?.join("-")}</strong><button type="button" onClick={()=>void navigator.clipboard.writeText(pairing.pairingCode).then(()=>setNotice("Pairing code copied.")).catch(()=>setNotice("Select and copy the code manually."))}>Copy code</button></div>:<div className={styles.empty}>{pairing?"Code used or expired. Check the session below or create a new code.":"Prepare your chosen gateway, then select a UAV and mission to create a code."}</div>}
    </section>
    <section className={styles.card}><span className={styles.step}>04 · VERIFY YOUR CONNECTION</span><h2>Your connection status</h2><p><strong>Gateway online</strong>: the Jetson or laptop has reached DroneOps online. <strong>Receiving drone telemetry</strong>: the system is receiving fresh flight controller data. Open Live Monitoring to view the drone status and confirm flight preparation when requested by the mission.</p><p>If the gateway is online but telemetry is missing, check the flight controller power, data cable, and MAVLink configuration. If the gateway is offline, check Internet access on the Jetson or laptop and reopen the radio launcher when using a radio.</p>
      {loading?<p>Loading…</p>:!connections.length?<div className={styles.empty}>No sessions yet.</div>:<div className={styles.sessions}>{connections.map(c=><article key={c.id}><div><strong>{labels[c.status]}</strong><p>{aircraft.find(a=>a.id===c.uavId)?.code??c.uavId} · {missions.find(m=>m.id===c.missionId)?.name??c.missionId}</p><small>{c.gatewayTransport} · {c.hardwareIdentity??"Hardware not identified"} · {c.gatewayName??"Gateway not paired"}{c.pairedAt?` · MAVLink ${c.systemId}/${c.componentId}`:""}</small><small>Last gateway contact: {c.lastGatewayAt?formatVietnamDateTime(c.lastGatewayAt):"None"} · Last sample: {c.lastSampleAt?formatVietnamDateTime(c.lastSampleAt):"None"}</small><small>Session expires: {formatVietnamDateTime(c.expiresAt)}</small></div>{!c.revokedAt&&<button className="module-secondary" disabled={busy} onClick={()=>void disconnect(c.id)}>DISCONNECT</button>}</article>)}</div>}
    </section>
  </div>;
}
