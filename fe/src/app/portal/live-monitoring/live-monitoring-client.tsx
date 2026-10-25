"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getLatestMissionTelemetry, getLiveMissions, getMission, getMissionIncidents, liveMissionStreamUrl } from "@/lib/portal/client";
import type { IncidentItem, LiveMission, MissionDetail, MissionTelemetry } from "@/lib/portal/types";
import { hasTelemetryPosition } from "@/lib/portal/telemetry-position";
import LiveMap from "./live-map";
import styles from "./live-monitoring.module.css";

type StreamState = "connecting" | "live" | "reconnecting" | "closed";

const valueOr = (value: number | null | undefined, suffix = "", digits = 1) => Number.isFinite(value) ? `${Number(value).toFixed(digits)}${suffix}` : "Unavailable";
const shortDate = (value: string | null | undefined) => value ? new Date(value).toLocaleString() : "No packet received";
const statusKey = (value: string | null | undefined) => (value || "offline").toLowerCase().replaceAll(" ", "");

function telemetryFromMission(mission: LiveMission): MissionTelemetry {
  return {
    missionId: mission.missionId, uavId: mission.uavId, uavCode: mission.uavCode, uavName: mission.uavName, sourceId: null,
    latitude: mission.latitude, longitude: mission.longitude, altitudeMeters: mission.altitudeMeters, altitudeReference: "Relative",
    groundSpeedMetersPerSecond: mission.groundSpeedMetersPerSecond, batteryPercent: mission.batteryPercent, flightMode: mission.flightMode,
    armed: mission.armed, missionSequence: mission.missionSequence, sourceTimestamp: mission.lastSeen, receivedAt: mission.lastSeen,
    connectivityStatus: mission.connectivityStatus, dataAgeSeconds: mission.dataAgeSeconds, isStale: statusKey(mission.connectivityStatus) !== "online",
    isValid: mission.latitude !== null && mission.longitude !== null, snapshotKind: mission.lastSeen ? "Latest" : "Missing",
    waypointTotal: mission.waypointTotal, progressPercent: mission.progressPercent,
  };
}

export default function LiveMonitoringClient() {
  const [missions, setMissions] = useState<LiveMission[] | null>(null);
  const [selectedId, setSelectedId] = useState("");
  const [mission, setMission] = useState<MissionDetail | null>(null);
  const [telemetry, setTelemetry] = useState<MissionTelemetry | null>(null);
  const [incidents, setIncidents] = useState<IncidentItem[]>([]);
  const [loadingMission, setLoadingMission] = useState(false);
  const [error, setError] = useState("");
  const [streamState, setStreamState] = useState<StreamState>("closed");
  const [recenterToken, setRecenterToken] = useState(0);
  const [clock, setClock] = useState(() => Date.now());

  const loadMissions = useCallback(async () => {
    setError("");
    try {
      const result = await getLiveMissions();
      setMissions(result);
      setSelectedId(current => result.some(item => item.missionId === current) ? current : result[0]?.missionId ?? "");
    } catch (reason) {
      setMissions([]);
      setError(reason instanceof Error ? reason.message : "Unable to load live missions.");
    }
  }, []);

  useEffect(() => {
    let active = true;
    void getLiveMissions().then(result => {
      if (!active) return;
      setMissions(result);
      setSelectedId(result[0]?.missionId ?? "");
    }).catch(reason => {
      if (!active) return;
      setMissions([]);
      setError(reason instanceof Error ? reason.message : "Unable to load live missions.");
    });
    return () => { active = false; };
  }, []);
  useEffect(() => {
    const timer = window.setInterval(() => setClock(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const selected = useMemo(() => missions?.find(item => item.missionId === selectedId) ?? null, [missions, selectedId]);

  const loadSelected = useCallback(async () => {
    if (!selectedId || !selected) return;
    setLoadingMission(true);
    setError("");
    setMission(null);
    setTelemetry(telemetryFromMission(selected));
    const [missionResult, telemetryResult, incidentResult] = await Promise.allSettled([
      getMission(selectedId),
      getLatestMissionTelemetry(selectedId),
      getMissionIncidents(selectedId),
    ]);
    if (missionResult.status === "fulfilled") setMission(missionResult.value);
    if (telemetryResult.status === "fulfilled") setTelemetry(telemetryResult.value);
    if (incidentResult.status === "fulfilled") setIncidents(incidentResult.value.items);
    else setIncidents([]);
    const failures = [missionResult, telemetryResult].filter(result => result.status === "rejected");
    if (failures.length === 2) setError("Mission route and telemetry snapshot are currently unavailable. Live stream reconnection will continue.");
    setLoadingMission(false);
  }, [selected, selectedId]);

  useEffect(() => {
    if (!selectedId || !selected) return;
    let active = true;
    void Promise.allSettled([
      getMission(selectedId),
      getLatestMissionTelemetry(selectedId),
      getMissionIncidents(selectedId),
    ]).then(([missionResult, telemetryResult, incidentResult]) => {
      if (!active) return;
      setTelemetry(telemetryResult.status === "fulfilled" ? telemetryResult.value : telemetryFromMission(selected));
      setMission(missionResult.status === "fulfilled" ? missionResult.value : null);
      setIncidents(incidentResult.status === "fulfilled" ? incidentResult.value.items : []);
      if (missionResult.status === "rejected" && telemetryResult.status === "rejected") {
        setError("Mission route and telemetry snapshot are currently unavailable. Live stream reconnection will continue.");
      }
    });
    return () => { active = false; };
  }, [selected, selectedId]);

  useEffect(() => {
    if (!selectedId) return;
    let active = true;
    const source = new EventSource(liveMissionStreamUrl(selectedId));
    queueMicrotask(() => { if (active) setStreamState("connecting"); });
    source.onopen = () => setStreamState("live");
    source.onerror = () => setStreamState("reconnecting");
    const updateTelemetry = (event: MessageEvent<string>) => {
      try { setTelemetry(JSON.parse(event.data) as MissionTelemetry); setStreamState("live"); }
      catch { setError("A telemetry packet could not be decoded."); }
    };
    const updateIncident = (event: MessageEvent<string>) => {
      try {
        const item = JSON.parse(event.data) as IncidentItem;
        setIncidents(current => [item, ...current.filter(existing => existing.id !== item.id)].slice(0, 8));
      } catch { setError("An incident event could not be decoded."); }
    };
    source.addEventListener("telemetryUpdated", updateTelemetry as EventListener);
    source.addEventListener("incidentRaised", updateIncident as EventListener);
    source.addEventListener("incidentUpdated", updateIncident as EventListener);
    return () => { active = false; source.close(); };
  }, [selectedId]);

  const packetAge = useMemo(() => {
    if (!telemetry) return null;
    const sourceTime = telemetry.sourceTimestamp ? new Date(telemetry.sourceTimestamp).getTime() : null;
    if (sourceTime && Number.isFinite(sourceTime)) return Math.max(0, (clock - sourceTime) / 1000);
    return telemetry.dataAgeSeconds;
  }, [clock, telemetry]);
  const connection = statusKey(telemetry?.connectivityStatus ?? selected?.connectivityStatus);
  const sequence = Math.max(0, telemetry?.missionSequence ?? selected?.missionSequence ?? 0);
  const waypointTotal = Math.max(0, telemetry?.waypointTotal ?? selected?.waypointTotal ?? mission?.waypoints.length ?? 0);
  const progress = Math.min(100, Math.max(0, telemetry?.progressPercent ?? selected?.progressPercent ?? (waypointTotal ? sequence / waypointTotal * 100 : 0)));
  const activeIncidents = incidents.filter(item => statusKey(item.status) !== "resolved");
  const latestIncident = activeIncidents[0];

  if (missions === null) return <section className={styles.state}><div className={styles.spinner} /><p>ESTABLISHING LIVE OPERATIONS LINK</p></section>;

  return <div className={styles.monitor}>
    <header className={styles.commandHeader}>
      <div className={styles.missionIdentity}>
        <span className={styles.eyebrow}>LIVE OPERATIONS / MISSION CHANNEL</span>
        {missions.length ? <select aria-label="Active mission" value={selectedId} onChange={event => setSelectedId(event.target.value)}>{missions.map(item => <option key={item.missionId} value={item.missionId}>{item.missionName} / {item.uavCode ?? "Unassigned UAV"}</option>)}</select> : <h1>No active mission</h1>}
        <p>{selected ? `${selected.uavCode ?? "UNASSIGNED"} / PIC: ${selected.pilotName ?? "Unavailable"}` : "Live tracking begins when an active mission publishes telemetry."}</p>
      </div>
      <div className={styles.commandTools}>
        <div className={`${styles.streamState} ${styles[streamState]}`}><i /> STREAM {streamState.toUpperCase()}</div>
        <div className={styles.freshness}>FRESHNESS <b>{packetAge === null ? "--" : packetAge < 1 ? "<1s" : `${Math.round(packetAge)}s`}</b></div>
        <button type="button" onClick={() => setRecenterToken(value => value + 1)} disabled={!selectedId}>CENTER UAV</button>
        <button type="button" onClick={() => { void loadMissions(); void loadSelected(); }}>REFRESH</button>
      </div>
    </header>

    {error && <div className={styles.errorBanner} role="alert"><span>{error}</span><button type="button" onClick={() => setError("")}>DISMISS</button></div>}

    {!missions.length ? <section className={styles.emptyState}><span>NO LIVE CHANNEL</span><h2>No active missions</h2><p>The backend returned no missions currently eligible for live tracking.</p><button type="button" onClick={() => void loadMissions()}>RETRY CONNECTION</button></section> : <>
      <section className={styles.signalStrip}>
        <div className={connection === "online" ? styles.good : connection === "stale" ? styles.warning : styles.danger}><b>TELEMETRY LINK</b><span>{(telemetry?.connectivityStatus ?? selected?.connectivityStatus ?? "Offline").toUpperCase()} / {telemetry?.snapshotKind?.toUpperCase() ?? "SNAPSHOT"}</span></div>
        <div className={latestIncident ? styles.danger : styles.good}><b>{latestIncident ? `${latestIncident.severity} INCIDENT` : "INCIDENT CHANNEL"}</b><span>{latestIncident?.title ?? "No open incident received for this mission."}</span></div>
      </section>

      <div className={styles.operationsGrid} aria-busy={loadingMission}>
        <section className={styles.mapPanel}>
          <header><div><span>SPATIAL TELEMETRY</span><h2>{selected?.missionName}</h2></div><div><b>{telemetry?.flightMode ?? "MODE UNAVAILABLE"}</b><small>{telemetry?.armed ? "ARMED" : "DISARMED"}</small></div></header>
          <LiveMap key={selectedId} missionId={selectedId} telemetry={telemetry?.missionId === selectedId ? telemetry : null} waypoints={mission?.id === selectedId ? mission.waypoints : []} sequence={sequence} recenterToken={recenterToken} streamLive={streamState === "live"} />
          <footer><span>LAST PACKET <b>{shortDate(telemetry?.sourceTimestamp ?? selected?.lastSeen)}</b></span><span>{connection === "online" && !telemetry?.isStale && streamState === "live" ? "POSITION" : "LAST KNOWN POSITION"} <b>{hasTelemetryPosition(telemetry) ? `${Number(telemetry.latitude).toFixed(5)}, ${Number(telemetry.longitude).toFixed(5)}` : "UNAVAILABLE"}</b></span></footer>
        </section>

        <aside className={styles.liveRail}>
          <section className={styles.linkPanel}>
            <header><span>LIVE LINK & FLIGHT STATE</span><b className={styles[connection]}>{connection.toUpperCase()}</b></header>
            <div className={styles.linkReadout}><strong>{telemetry?.uavCode ?? selected?.uavCode ?? "UAV"}</strong><span>{telemetry?.uavName ?? selected?.uavName ?? "Platform name unavailable"}</span></div>
            <dl><div><dt>SOURCE</dt><dd>{telemetry?.sourceId ?? "Unavailable"}</dd></div><div><dt>FLIGHT MODE</dt><dd>{telemetry?.flightMode ?? "Unavailable"}</dd></div><div><dt>PACKET AGE</dt><dd>{packetAge === null ? "Unavailable" : `${packetAge.toFixed(1)} s`}</dd></div><div><dt>STREAM</dt><dd>{streamState}</dd></div></dl>
          </section>

          <section className={styles.progressPanel}>
            <header><span>FLIGHT CORRIDOR PROGRESSION</span><b>WP {String(sequence).padStart(2, "0")} / {String(waypointTotal).padStart(2, "0")}</b></header>
            <div className={styles.progressValue}><strong>{progress.toFixed(1)}%</strong><span>Overall completion</span></div>
            <div className={styles.progressTrack}><i style={{ width: `${progress}%` }} /></div>
            <div className={styles.progressMeta}><span>NEXT WAYPOINT<b>{waypointTotal ? Math.min(sequence + 1, waypointTotal) : "--"}</b></span><span>MISSION STATUS<b>{selected?.missionStatus ?? "Unavailable"}</b></span></div>
          </section>

          <section className={styles.incidentPanel}>
            <header><span>LIVE INCIDENT FEED</span><b>{activeIncidents.length} OPEN</b></header>
            <div className={styles.incidentList}>{incidents.length ? incidents.slice(0, 5).map(item => <article key={item.id}><div><b className={styles[`severity${statusKey(item.severity)}`]}>{item.severity}</b><time>{new Date(item.createdAt).toLocaleTimeString()}</time></div><h3>{item.title}</h3><p>{item.description ?? `${item.type} event`}</p><small>{item.status.toUpperCase()}</small></article>) : <div className={styles.incidentEmpty}><b>CHANNEL CLEAR</b><span>No incidents recorded for this mission.</span></div>}</div>
          </section>
        </aside>
      </div>

      <section className={styles.telemetrySection}>
        <header><div><span>CORE UAV TELEMETRY</span><h2>Telemetric Matrix</h2></div><b>MAVLINK DATA / {streamState.toUpperCase()}</b></header>
        <div className={styles.telemetryGrid}>
          <article className={styles.telemetryCard}>
            <span>01. GEOLOCATION & GPS</span>
            {hasTelemetryPosition(telemetry) ? <dl className={styles.coordinates}>
              <div><dt>Vĩ độ <span>Latitude</span></dt><dd>{telemetry.latitude.toFixed(6)}°</dd></div>
              <div><dt>Kinh độ <span>Longitude</span></dt><dd>{telemetry.longitude.toFixed(6)}°</dd></div>
            </dl> : <strong>Position unavailable</strong>}
            <div><small>{hasTelemetryPosition(telemetry) ? "Đơn vị: độ thập phân (WGS84)" : "Waiting for valid coordinates"}</small><small>Snapshot: {telemetry?.snapshotKind ?? "Missing"}</small></div>
          </article>
          <TelemetryCard index="02" label="ALTITUDE" value={valueOr(telemetry?.altitudeMeters, telemetry?.altitudeReference === "AGL" ? " m AGL" : telemetry?.altitudeReference === "MSL" ? " m MSL" : " m")} lines={[`Reference: ${telemetry?.altitudeReference === "Relative" ? "Relative to Home / So với Home" : telemetry?.altitudeReference ?? "Unavailable"}`, `Validity: ${telemetry?.isValid ? "Valid" : "Unavailable"}`]} />
          <TelemetryCard index="03" label="VELOCITY" value={valueOr(telemetry?.groundSpeedMetersPerSecond, " m/s")} lines={[telemetry?.groundSpeedMetersPerSecond == null ? "Ground speed unavailable" : `${(Number(telemetry.groundSpeedMetersPerSecond) * 3.6).toFixed(1)} km/h`, "Wind data unavailable"]} />
          <TelemetryCard index="04" label="POWER & CELLS" value={valueOr(telemetry?.batteryPercent, "%", 0)} tone={telemetry?.batteryPercent != null && telemetry.batteryPercent < 25 ? "danger" : "good"} lines={["Voltage unavailable", `State: ${telemetry?.armed ? "Armed" : "Disarmed"}`]} />
          <TelemetryCard index="05" label="LINK & TELEMETRY" value={(telemetry?.connectivityStatus ?? "Offline").toUpperCase()} tone={connection === "online" ? "good" : connection === "stale" ? "warning" : "danger"} lines={[`Age: ${packetAge === null ? "Unavailable" : `${packetAge.toFixed(1)} s`}`, `Stream: ${streamState}`]} />
          <TelemetryCard index="06" label="FLIGHT NAVIGATION" value={waypointTotal ? `WP ${sequence}/${waypointTotal}` : "Unavailable"} lines={[`Mode: ${telemetry?.flightMode ?? "Unavailable"}`, `Progress: ${progress.toFixed(1)}%`]} />
        </div>
      </section>

      <section className={styles.rawPanel}>
        <header><span>RAW TELEMETRY SNAPSHOT</span><b>{telemetry?.sourceTimestamp ? new Date(telemetry.sourceTimestamp).toISOString() : "NO SOURCE TIMESTAMP"}</b></header>
        <div><code>MISSION_ID: {selectedId}</code><code>UAV_ID: {telemetry?.uavId ?? "UNAVAILABLE"}</code><code>RECEIVED_AT: {telemetry?.receivedAt ?? "UNAVAILABLE"}</code><code>VALID: {String(telemetry?.isValid ?? false).toUpperCase()}</code></div>
      </section>
    </>}
  </div>;
}

function TelemetryCard({ index, label, value, lines, tone = "default" }: { index: string; label: string; value: string; lines: string[]; tone?: "default" | "good" | "warning" | "danger" }) {
  return <article className={styles.telemetryCard}><span>{index}. {label}</span><strong className={tone === "default" ? undefined : styles[tone]}>{value}</strong><div>{lines.map(line => <small key={line}>{line}</small>)}</div></article>;
}
