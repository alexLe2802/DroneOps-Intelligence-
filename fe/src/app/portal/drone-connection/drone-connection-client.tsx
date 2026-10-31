"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { getAssignedUavs, getPilotMissions } from "@/lib/portal/client";
import { createDroneConnection, getDroneConnections, getDroneBindings, type DroneBinding, revokeDroneConnection, type DroneConnection } from "@/lib/portal/drone-connections";
import type { AssignedUav, MissionDetail } from "@/lib/portal/types";
import styles from "./drone-connection.module.css";

const labels: Record<DroneConnection["status"],string> = {AwaitingGateway:"Waiting for gateway",PairingExpired:"Pairing code expired",Expired:"Session expired",Revoked:"Disconnected",Offline:"Gateway offline",GatewayConnected:"Gateway online · waiting for drone",ReceivingTelemetry:"Receiving drone telemetry"};
type Pairing = {connection:DroneConnection;pairingCode:string;gatewayPath:"/ws/drone-gateway"};
type BootstrapInfo = {backendOrigin:string;downloadOrigin:string;laptopHost:string};

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
  const [bootstrapInfo,setBootstrapInfo]=useState<BootstrapInfo|null>(null);
  const [error,setError]=useState(""); const [notice,setNotice]=useState("");
  const [now,setNow]=useState(()=>Date.now());
  useEffect(()=>{let active=true;void Promise.all([getAssignedUavs(),getPilotMissions(),getDroneConnections(),getDroneBindings()]).then(([a,m,c,b])=>{if(!active)return;setAircraft(a.filter(x=>x.status.toLowerCase()==="active"));setMissions(m.filter(x=>["approved","inprogress"].includes(x.status.toLowerCase().replaceAll(" ",""))));setConnections(c);setBindings(b);}).catch(e=>{if(active)setError(e.message);}).finally(()=>{if(active)setLoading(false);});return()=>{active=false;};},[]);
  useEffect(()=>{let active=true;void fetch("/api/gateway-bootstrap",{cache:"no-store"}).then(async response=>{if(!response.ok)throw new Error("Unable to determine the LAN backend address.");return response.json() as Promise<BootstrapInfo>;}).then(info=>{if(active)setBootstrapInfo(info);}).catch(e=>{if(active)setError(e instanceof Error?e.message:"Unable to prepare the Jetson command.");});return()=>{active=false;};},[]);
  useEffect(()=>{let active=true;const timer=setInterval(()=>{setNow(Date.now());void Promise.all([getDroneConnections(),getDroneBindings()]).then(([c,b])=>{if(active){setConnections(c);setBindings(b);setStatusError("");}}).catch(()=>{if(active)setStatusError("Connection status could not be refreshed. Check your network and reload.");});},5000);return()=>{active=false;clearInterval(timer);};},[]);
  const eligible=missions.filter(m=>m.uavId===uav);
  const pairedState=pairing?connections.find(c=>c.id===pairing.connection.id):null;
  const codeVisible=pairing && !pairedState?.pairedAt && !pairedState?.revokedAt && new Date(pairing.connection.codeExpiresAt).getTime()>now;
  const jetsonCommand=pairing&&bootstrapInfo?`curl -fsSL '${bootstrapInfo.downloadOrigin}/downloads/bootstrap-jetson.sh' -o /tmp/droneops-bootstrap.sh && sudo bash /tmp/droneops-bootstrap.sh --server '${bootstrapInfo.backendOrigin}' --package-url '${bootstrapInfo.downloadOrigin}/downloads/droneops-gateway.zip' --gcs-host '${bootstrapInfo.laptopHost}' --otp '${pairing.pairingCode}'`:"";
  const radioWindowsCommand=bootstrapInfo?`.venv\\Scripts\\python.exe gateway.py --server ${bootstrapInfo.backendOrigin} --device COM5 --baud 57600 --system-id 1 --new-pairing`:"Detecting backend address…";
  const radioUnixCommand=bootstrapInfo?`.venv/bin/python gateway.py --server ${bootstrapInfo.backendOrigin} --device /dev/ttyUSB0 --baud 57600 --system-id 1 --new-pairing`:"Detecting backend address…";
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
          <h3>Start the backend for LAN access</h3>
          <p>Before creating a pairing code, open a terminal at the DroneOps repository root—the folder containing <code>be</code> and <code>fe</code>—then start the backend with LAN binding. Binding only to <code>localhost</code> prevents the Jetson from connecting.</p>
          <pre>{`dotnet run --project ./be/DroneOps/DroneOps.API/DroneOps.API.csproj \\
  --no-launch-profile \\
  --urls http://0.0.0.0:5159 \\
  --environment Development`}</pre>
          <p>If MSBuild reports that the project file does not exist, the terminal is not at the repository root. Change to the cloned <code>SEP490</code> folder and run the command again.</p>
          <p><code>0.0.0.0</code> is the listen address, not the address stored on the Jetson. The generated command replaces it with this laptop&apos;s current LAN IP and port <code>5159</code>. Keep the backend running while receiving telemetry.</p>
          <h3>Automatic Jetson setup</h3>
          <p>Connect Pixhawk TELEM2 through one 3.3 V USB-UART adapter and save this LAN on the Jetson. After creating a pairing code, step 03 generates one complete SSH command with the current laptop IP and backend port.</p>
          <p>The command installs or updates the gateway, configures both services, pairs the drone and enables automatic startup. If the Jetson exposes multiple USB serial ports, choose the Pixhawk port by number in the terminal.</p>
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
    <section className={styles.card}><span className={styles.step}>03 · PAIR THIS MISSION</span><h2>{profile==="jetson"?"Copy one command to your Jetson":"Enter the code on your laptop"}</h2>
      {profile==="jetson"?<><p>Create a code below, SSH into the Jetson and paste the generated command. It uses the current LAN address, installs or updates the gateway, enables its services and pairs the selected mission. After setup, the same terminal continuously shows gateway and GPS logs; press Ctrl+C to stop viewing logs without stopping the background service.</p>
      {codeVisible&&jetsonCommand?<div className={styles.code}><pre>{jetsonCommand}</pre><button type="button" onClick={()=>void navigator.clipboard.writeText(jetsonCommand).then(()=>setNotice("Jetson setup command copied.")).catch(()=>setNotice("Select and copy the command manually."))}>COPY JETSON COMMAND</button></div>:<div className={styles.empty}>Create a pairing code to generate the Jetson command.</div>}
      <p>For later boots, just supply power and your saved network.</p></>:<><p>Run the command for your laptop operating system. Replace the serial port, baud and system ID with the real values, then enter the pairing code when prompted.</p>
      <h3>Windows PowerShell or Command Prompt</h3><pre>{radioWindowsCommand}</pre>
      <h3>Linux or macOS terminal</h3><pre>{radioUnixCommand}</pre>
      <p><code>COM5</code>, <code>/dev/ttyUSB0</code> and <code>57600</code> are examples. On macOS the device normally starts with <code>/dev/cu.</code>. Keep this process running to send telemetry. To restart the same session, omit <code>--new-pairing</code>.</p></>}
      <p>A new code is needed after the 12-hour session expires, after Disconnect, or when changing mission. Network recovery alone does not require pairing again. Switching this setup profile does not revoke or change an existing session.</p>
      {pairing&&<p>Gateway endpoint: <code>{pairing.gatewayPath}</code> · Transport: <code>{pairing.connection.gatewayTransport}</code></p>}
      {codeVisible?<div className={styles.code}><span>ONE-TIME CODE · EXPIRES IN {Math.max(0,Math.ceil((new Date(pairing.connection.codeExpiresAt).getTime()-now)/1000))}s</span><strong>{pairing.pairingCode.match(/.{1,4}/g)?.join("-")}</strong><button type="button" onClick={()=>void navigator.clipboard.writeText(pairing.pairingCode).then(()=>setNotice("Pairing code copied.")).catch(()=>setNotice("Select and copy the code manually."))}>Copy code</button></div>:<div className={styles.empty}>{pairing?"Code used or expired. Check the session below or create a new code.":"Prepare your chosen gateway, then select a UAV and mission to create a code."}</div>}
    </section>
    <section className={styles.card}><span className={styles.step}>04 · VERIFY CONNECTION</span><h2>Your connection sessions</h2><p>Gateway online means the Jetson or laptop has reached the backend. Receiving telemetry means fresh autopilot samples are arriving. Pairing never arms or starts the flight.</p>
      {loading?<p>Loading…</p>:!connections.length?<div className={styles.empty}>No sessions yet.</div>:<div className={styles.sessions}>{connections.map(c=><article key={c.id}><div><strong>{labels[c.status]}</strong><p>{aircraft.find(a=>a.id===c.uavId)?.code??c.uavId} · {missions.find(m=>m.id===c.missionId)?.name??c.missionId}</p><small>{c.gatewayTransport} · {c.hardwareIdentity??"Hardware not identified"} · {c.gatewayName??"Gateway not paired"}{c.pairedAt?` · MAVLink ${c.systemId}/${c.componentId}`:""}</small><small>Last gateway contact: {c.lastGatewayAt?new Date(c.lastGatewayAt).toLocaleString():"None"} · Last sample: {c.lastSampleAt?new Date(c.lastSampleAt).toLocaleString():"None"}</small><small>Session expires: {new Date(c.expiresAt).toLocaleString()}</small></div>{!c.revokedAt&&<button className="module-secondary" disabled={busy} onClick={()=>void disconnect(c.id)}>DISCONNECT</button>}</article>)}</div>}
    </section>
  </div>;
}
