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
  const radioWindowsCommand = "Mở connect-drone.cmd trong thư mục droneops-gateway đã giải nén";
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
    {!embedded && <header className="module-heading"><div><p className="module-kicker">PILOT / DRONE CONNECTION</p><h1>Kết nối drone với DroneOps</h1><p>Chọn Jetson trên drone hoặc radio telemetry qua laptop để gửi dữ liệu tới DroneOps online. Thiết bị chạy gateway cần có Internet; việc ghép đôi sử dụng mã tạo ngay trên website.</p></div><Link className="module-secondary module-link" href="/portal/live-monitoring">MỞ LIVE MONITORING →</Link></header>}
    {error&&<p className={styles.error} role="alert">{error}</p>}{statusError&&<p className={styles.error} role="status">{statusError}</p>}{notice&&<p className={styles.notice} role="status">{notice}</p>}
    <div className={styles.grid}>
      <section className={styles.card}><span className={styles.step}>01 · CHUẨN BỊ KẾT NỐI</span><h2>Drone của bạn sử dụng thiết bị nào?</h2>
        <div className={styles.choice} role="group" aria-label="Connection profile">
          <button type="button" aria-pressed={profile==="jetson"} onClick={()=>setProfile("jetson")}><strong>JETSON ONBOARD</strong><span>Cài một lần trên Jetson, tự kết nối lại khi bật nguồn.</span></button>
          <button type="button" aria-pressed={profile==="radio"} onClick={()=>setProfile("radio")}><strong>RADIO TELEMETRY</strong><span>Dùng radio và laptop để chuyển dữ liệu drone lên website.</span></button>
        </div>
        <a className="module-secondary module-link" href="/downloads/droneops-gateway.zip" download>TẢI GATEWAY VÀ HƯỚNG DẪN</a>
        {profile==="jetson"?<>
          <h3>Có Jetson: cài đặt một lần trên drone</h3>
          <ol className={styles.instructions}>
            <li>Cấp nguồn cho flight controller và Jetson. Nối hai thiết bị bằng USB truyền dữ liệu hoặc TELEM2 qua adapter USB–UART 3,3 V; cổng flight controller cần được cấu hình MAVLink.</li>
            <li>Kết nối Jetson với Internet qua Wi-Fi hoặc Ethernet và lưu mạng để tự kết nối khi bật lại.</li>
            <li>Chọn UAV và nhiệm vụ ở bước 02. Trong lần đăng ký đầu tiên, dán lệnh ở bước 03 vào terminal trên Jetson hoặc phiên SSH tới Jetson.</li>
          </ol>
          <p>Khi cài đặt và ghép đôi thành công, bạn có thể đóng terminal/SSH. Những lần sau chỉ cần bật nguồn, chờ Jetson có Internet và chọn nhiệm vụ trên website; gateway tự kết nối lại.</p>
        </>:<>
          <h3>Không có Jetson: radio telemetry và laptop</h3>
          <ol className={styles.instructions}>
            <li>Nối air radio vào cổng MAVLink của flight controller và cắm ground radio tương ứng vào USB laptop. Bật nguồn drone và kết nối Internet cho laptop.</li>
            <li>Cài Python 3.9 trở lên, tải gateway bằng nút phía trên và giải nén. Windows: mở <code>connect-drone.cmd</code>. macOS/Linux: mở terminal trong thư mục đã giải nén và chạy <code>bash connect-drone.sh</code>.</li>
            <li>Launcher tự dò cổng radio và ArduPilot/PX4. Nếu có nhiều cổng USB, chọn số của ground radio. Chọn UAV/nhiệm vụ trên website, tạo mã và nhập mã khi launcher yêu cầu trong lần ghép đầu.</li>
          </ol>
          <p>Giữ cửa sổ gateway mở, laptop có Internet và không chuyển sang chế độ ngủ. Các lần sau chỉ cần mở lại launcher và chọn nhiệm vụ trên website.</p>
          <p>Nếu không tìm thấy drone, kiểm tra nguồn, cáp USB và cặp radio. Đóng Mission Planner/QGroundControl nếu ứng dụng đang sử dụng cổng radio đó.</p>
        </>}
      </section>
      <section className={styles.card}><span className={styles.step}>02 · CHỌN DRONE VÀ NHIỆM VỤ</span><h2>Chọn UAV đã đăng ký của bạn</h2><p>Chọn UAV đang hoạt động và nhiệm vụ đã duyệt hoặc đang thực hiện, còn trong thời gian cho phép. Chuẩn bị Jetson hoặc launcher radio trước khi tạo mã ghép đôi; mã chỉ có hiệu lực 5 phút.</p>
        <label>UAV<select value={uav} disabled={busy||loading} onChange={e=>{setUav(e.target.value);setMission("");setBindingConfirmed(false);}}><option value="">Select UAV</option>{aircraft.map(a=><option key={a.id} value={a.id}>{a.code} · {a.name} · {bindings.some(b=>b.uavId===a.id)?"Hardware bound":"Not yet bound"}</option>)}</select></label>
        <label>Mission<select value={mission} disabled={!uav||busy||loading} onChange={e=>setMission(e.target.value)}><option value="">Select mission</option>{eligible.map(m=><option key={m.id} value={m.id}>{m.name} · {m.status}</option>)}</select></label>
        {uav&&!eligible.length&&<p>UAV này chưa có nhiệm vụ phù hợp. <Link href="/portal/missions">Xem nhiệm vụ của bạn</Link></p>}
        {uav&&<div className={styles.empty}><strong>{bindings.find(b=>b.uavId===uav)?.hardwareIdentity??"Ghép phần cứng lần đầu"}</strong><p>{bindings.some(b=>b.uavId===uav)?"Chỉ flight controller có UID này mới được kết nối với UAV đã chọn.":"UID của flight controller kết nối lần đầu sẽ được khóa với UAV này. Cùng một flight controller không được ghép sang UAV đăng ký khác."}</p>{!bindings.some(b=>b.uavId===uav)&&<label><input type="checkbox" checked={bindingConfirmed} onChange={e=>setBindingConfirmed(e.target.checked)}/> Tôi xác nhận UAV đã chọn đúng là drone đang được kết nối.</label>}</div>}
        {binding?.gatewayRegistered && <div className={styles.notice}><strong>Gateway đã được đăng ký</strong><p>Bật Jetson hoặc mở launcher radio, sau đó chọn nhiệm vụ và bấm CONNECT SELECTED MISSION. Gateway đã lưu đăng ký sẽ tự nhận kết nối mới; bạn không cần nhập lại mã ghép.</p><button type="button" className="module-secondary" disabled={busy} onClick={() => void forgetGateway()}>FORGET TRUSTED GATEWAY</button></div>}
        <p>Kết nối mới gắn với UAV và nhiệm vụ đã chọn, đồng thời thay thế phiên nhiệm vụ trước đó. Kết nối không tự ARM hoặc cất cánh.</p><button className="module-primary" disabled={busy||loading||!uav||!mission||(!bindings.some(b=>b.uavId===uav)&&!bindingConfirmed)} onClick={()=>void connect()}>{busy?"PLEASE WAIT…":binding?.gatewayRegistered ? "CONNECT SELECTED MISSION" : "CREATE PAIRING CODE"}</button>
      </section>
    </div>
    <section className={styles.card}><span className={styles.step}>03 · GHÉP ĐÔI LẦN ĐẦU</span><h2>{binding?.gatewayRegistered?"Gateway đã sẵn sàng tự kết nối":profile==="jetson"?"Sao chép lệnh cài đặt vào Jetson":"Mở launcher và nhập mã ghép đôi"}</h2>
      {binding?.gatewayRegistered && <p>Gateway sẽ tự kết nối lại với nhiệm vụ bạn vừa chọn. Chỉ thực hiện ghép đôi lần đầu nếu bạn cài lại thiết bị hoặc đã quên gateway.</p>}
      {profile==="jetson"?<><p>Lần cài đầu: bấm COPY JETSON COMMAND và dán vào terminal trên Jetson hoặc phiên SSH. Lệnh sử dụng địa chỉ DroneOps online đã cấu hình sẵn, tự tải gateway và bật dịch vụ khởi động cùng Jetson.</p>
      {codeVisible&&jetsonCommand?<div className={styles.code}><pre>{jetsonCommand}</pre><button type="button" onClick={()=>void navigator.clipboard.writeText(jetsonCommand).then(()=>setNotice("Đã sao chép lệnh cài Jetson.")).catch(()=>setNotice("Hãy chọn và sao chép lệnh thủ công."))}>COPY JETSON COMMAND</button></div>:<div className={styles.empty}>{binding?.gatewayRegistered?"Chọn nhiệm vụ ở bước 02 rồi theo dõi trạng thái kết nối bên dưới.":"Tạo mã ghép đôi ở bước 02 để nhận lệnh cài đặt Jetson."}</div>}
      <p>Sau khi báo ghép đôi thành công, đóng terminal/SSH. Mỗi lần sử dụng sau: bật flight controller và Jetson, chờ Internet, rồi chọn nhiệm vụ trên website.</p></>:<><p>Trong lần ghép đầu, mở launcher trong thư mục gateway đã giải nén và nhập mã bên dưới khi được hỏi. Các lần sau mở lại launcher để dùng đăng ký đã lưu.</p>
      <h3>Windows</h3><pre>{radioWindowsCommand}</pre>
      <h3>macOS / Linux</h3><pre>{radioUnixCommand}</pre>
      <p>Chọn cổng ground radio bằng số nếu launcher yêu cầu. Đợi ghép đôi thành công và giữ cửa sổ gateway mở trong suốt quá trình theo dõi drone.</p></>}
      <p>Mã ghép chỉ dùng một lần và hết hạn sau 5 phút. Nếu mã hết hạn, tạo mã mới ở bước 02. DISCONNECT ngắt phiên nhiệm vụ; FORGET TRUSTED GATEWAY xóa đăng ký gateway nhưng vẫn giữ khóa UID của UAV.</p>
      {codeVisible?<div className={styles.code}><span>ONE-TIME CODE · EXPIRES IN {Math.max(0,Math.ceil((new Date(pairing.connection.codeExpiresAt).getTime()-now)/1000))}s</span><strong>{pairing.pairingCode.match(/.{1,4}/g)?.join("-")}</strong><button type="button" onClick={()=>void navigator.clipboard.writeText(pairing.pairingCode).then(()=>setNotice("Pairing code copied.")).catch(()=>setNotice("Select and copy the code manually."))}>Copy code</button></div>:<div className={styles.empty}>{pairing?"Code used or expired. Check the session below or create a new code.":"Prepare your chosen gateway, then select a UAV and mission to create a code."}</div>}
    </section>
    <section className={styles.card}><span className={styles.step}>04 · KIỂM TRA KẾT NỐI</span><h2>Trạng thái kết nối của bạn</h2><p><strong>Gateway online</strong>: Jetson hoặc laptop đã kết nối tới DroneOps online. <strong>Receiving drone telemetry</strong>: hệ thống đang nhận dữ liệu mới từ flight controller. Khi đã nhận telemetry, mở Live Monitoring để xem trạng thái drone và xác nhận chuẩn bị bay nếu nhiệm vụ yêu cầu.</p><p>Nếu gateway online nhưng chưa có telemetry, kiểm tra nguồn flight controller, cáp dữ liệu và cấu hình MAVLink. Nếu gateway offline, kiểm tra Internet trên Jetson hoặc laptop và mở lại launcher radio khi dùng radio.</p>
      {loading?<p>Loading…</p>:!connections.length?<div className={styles.empty}>No sessions yet.</div>:<div className={styles.sessions}>{connections.map(c=><article key={c.id}><div><strong>{labels[c.status]}</strong><p>{aircraft.find(a=>a.id===c.uavId)?.code??c.uavId} · {missions.find(m=>m.id===c.missionId)?.name??c.missionId}</p><small>{c.gatewayTransport} · {c.hardwareIdentity??"Hardware not identified"} · {c.gatewayName??"Gateway not paired"}{c.pairedAt?` · MAVLink ${c.systemId}/${c.componentId}`:""}</small><small>Last gateway contact: {c.lastGatewayAt?formatVietnamDateTime(c.lastGatewayAt):"None"} · Last sample: {c.lastSampleAt?formatVietnamDateTime(c.lastSampleAt):"None"}</small><small>Session expires: {formatVietnamDateTime(c.expiresAt)}</small></div>{!c.revokedAt&&<button className="module-secondary" disabled={busy} onClick={()=>void disconnect(c.id)}>DISCONNECT</button>}</article>)}</div>}
    </section>
  </div>;
}
