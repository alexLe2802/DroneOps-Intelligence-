"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { getLatestMissionTelemetry } from "@/lib/portal/client";
import { assessmentBatteryState } from "@/lib/portal/assessment-battery";
import type { AiAssessment, MissionTelemetry } from "@/lib/portal/types";
import { BatteryMedium, Radio, ArrowUpRight, Info, RefreshCw } from "lucide-react";
import styles from "./assessment.module.css";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";

export default function LiveBattery({ missionId, uavId, assessment }: {
  missionId: string; uavId: string | null; assessment: AiAssessment | undefined;
}) {
  const [telemetry, setTelemetry] = useState<MissionTelemetry | null>(null);
  const [failed, setFailed] = useState(false);
  const [now, setNow] = useState(0);
  const [checkedAt, setCheckedAt] = useState(0);
  const [checking, setChecking] = useState(false);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [refreshToken, setRefreshToken] = useState(0);
  const card = useRef<HTMLElement>(null);
  const lastRead = useRef({ missionId: "", token: -1 });
  useEffect(() => {
    if (!uavId) return;
    let active = true;
    let pending = false;
    let inView = typeof IntersectionObserver === "undefined";
    let controller: AbortController | undefined;
    let timer: ReturnType<typeof setTimeout>;
    let retryDelay = 15000;
    const available = () => active && inView && document.visibilityState === "visible" && navigator.onLine;
    const needsRead = () => autoRefresh || lastRead.current.missionId !== missionId || lastRead.current.token !== refreshToken;
    async function poll() {
      if (!available() || pending || !needsRead()) return;
      pending = true;
      controller = new AbortController();
      const request = controller;
      setChecking(true);
      try {
        const sample = await getLatestMissionTelemetry(missionId, request.signal);
        if (!available() || request.signal.aborted) return;
        const time = Date.now();
        setTelemetry(sample); setFailed(false); setCheckedAt(time); setNow(time);
        lastRead.current = { missionId, token: refreshToken };
        retryDelay = assessmentBatteryState(sample, missionId, uavId, undefined, time).fresh ? 5000 : Math.min(retryDelay * 2, 120000);
      } catch {
        if (!available() || request.signal.aborted) return;
        setTelemetry(null); setFailed(true);
        lastRead.current = { missionId, token: refreshToken };
        retryDelay = Math.min(retryDelay * 2, 120000);
      } finally {
        pending = false;
        if (active) setChecking(false);
        if (available() && needsRead()) timer = setTimeout(() => void poll(), request.signal.aborted ? 0 : retryDelay);
      }
    }
    const resume = () => {
      clearTimeout(timer);
      if (!available()) { controller?.abort(); return; }
      setNow(Date.now());
      void poll();
    };
    const observer = typeof IntersectionObserver === "undefined" ? null : new IntersectionObserver(entries => {
      inView = entries.some(entry => entry.isIntersecting);
      resume();
    });
    if (card.current) observer?.observe(card.current);
    timer = setTimeout(resume, 0);
    const clock = setInterval(() => { if (available()) setNow(Date.now()); }, 1000);
    document.addEventListener("visibilitychange", resume);
    window.addEventListener("online", resume);
    window.addEventListener("offline", resume);
    return () => {
      active = false; controller?.abort(); observer?.disconnect(); clearTimeout(timer); clearInterval(clock);
      document.removeEventListener("visibilitychange", resume);
      window.removeEventListener("online", resume);
      window.removeEventListener("offline", resume);
    };
  }, [missionId, uavId, autoRefresh, refreshToken]);
  const state = assessmentBatteryState(telemetry, missionId, uavId, assessment, now);
  const captured = assessmentBatteryState(telemetry, missionId, uavId, undefined, checkedAt);
  const displayPercent = state.percent ?? captured.percent;
  const snapshotOnly = captured.fresh && !state.fresh;
  const battery = assessment?.result?.battery;
  const conclusion = state.invalidPower ? "Check battery power and monitor calibration"
    : state.required != null && state.required > 100 ? "Route requires more than a full battery"
    : state.margin == null ? "Battery sufficiency is not established"
    : state.margin < 0 ? "Battery is below the planning estimate" : "Reported charge covers the planning estimate";
  const urgent = state.invalidPower || (state.required ?? 0) > 100 || (state.margin != null && state.margin < 0);
  return <section className={styles.batteryPanel} ref={card}>
    <header className={styles.panelHeader}><div><p className={styles.eyebrow}>PREFLIGHT CHECK</p><h2>Battery check</h2></div><BatteryMedium size={21} aria-hidden="true" /></header>
    <div className={styles.batteryControls}><button className={styles.secondaryButton} disabled={checking || !uavId} onClick={() => setRefreshToken(value => value + 1)}><RefreshCw size={14} aria-hidden="true" />{checking ? "Checking…" : "Check battery"}</button><label><input type="checkbox" checked={autoRefresh} disabled={!uavId} onChange={event => setAutoRefresh(event.target.checked)} />Auto refresh</label></div>
    <div className={styles.liveState} data-online={state.fresh}><span />{snapshotOnly ? "Saved reading · refresh to recheck" : state.fresh ? "Fresh FC reading" : failed ? "Telemetry request failed" : "No fresh battery evidence"}<small>{autoRefresh ? "Auto refresh on" : "On demand"}</small></div>
    <div className={styles.chargeReading}><strong>{displayPercent == null ? "—" : displayPercent}<span>%</span></strong><span>FC-reported<br />{snapshotOnly ? "charge at last check" : "remaining charge"}</span></div>
    {displayPercent != null ? <meter className={styles.chargeMeter} min={0} max={100} value={displayPercent} aria-label={snapshotOnly ? "Battery percentage at last check, not current" : "FC-reported remaining battery percentage"} /> : <div className={styles.emptyMeter}><span /> <span /> <span /> <span /> <span /> <span /> <span /> <span /></div>}
    <dl className={styles.batteryComparison}><div><dt>Departure estimate</dt><dd>{state.required == null ? "Not established" : `${state.required.toFixed(1)}%`}</dd></div><div><dt>Margin above estimate</dt><dd className={urgent ? styles.highText : ""}>{state.margin == null ? "Not established" : `${state.margin >= 0 ? "+" : ""}${state.margin.toFixed(1)} pp`}</dd></div></dl>
    <p className={`${styles.verdict} ${urgent ? styles.verdictHigh : ""}`} role="status"><Info size={16} aria-hidden="true" />{conclusion}.</p>
    {!state.fresh && <p className={styles.cardNote}>{snapshotOnly ? "This saved reading is no longer fresh and is not used to establish battery sufficiency. Check again before departure." : "Connect the UAV assigned to this mission, then check battery. Old samples and readings from another UAV are excluded."}</p>}
    {checkedAt > 0 && captured.fresh && <p className={styles.bodyCopy}>Last checked: {formatVietnamDateTime(checkedAt)}. {autoRefresh ? "Updates pause when this card or tab is hidden, or the browser is offline." : "Automatic requests are off, including while the drone is disarmed."}</p>}
    {state.invalidPower && <p className={styles.cardNote}>FC voltage is below 1 V. Check flight battery power and monitor calibration; USB power does not establish available energy.</p>}
    {!state.estimateCurrent && <p className={styles.cardNote}>Run a current assessment to compare charge. Outdated plans and estimates older than 10 minutes are excluded.</p>}
    <dl className={styles.factList}><div><dt>{snapshotOnly ? "Pack voltage at last check" : "Pack voltage"}</dt><dd>{captured.fresh && telemetry?.batteryVoltageVolts != null ? `${telemetry.batteryVoltageVolts.toFixed(2)} V` : "Not reported"}</dd></div><div><dt>{snapshotOnly ? "Consumed charge at last check" : "Consumed charge"}</dt><dd>{captured.fresh && telemetry?.batteryConsumedMah != null ? `${telemetry.batteryConsumedMah} mAh` : "Not reported"}</dd></div>{state.estimateCurrent && battery?.configuredCapacityMah != null && <div><dt>Remaining charge estimate</dt><dd>{state.percent == null || state.invalidPower ? "Not established" : `${(battery.configuredCapacityMah * state.percent / 100).toFixed(0)} mAh`}</dd></div>}</dl>
    <details className={styles.disclosure}><summary>Telemetry source & freshness</summary>{state.fresh && telemetry ? <dl className={styles.factList}><div><dt>UAV</dt><dd>{telemetry.uavCode ?? telemetry.uavName ?? "Assigned UAV"}</dd></div><div><dt>Source</dt><dd>{telemetry.sourceId}</dd></div><div><dt>Battery observed</dt><dd>{formatVietnamDateTime(telemetry.batteryObservedAt ?? telemetry.sourceTimestamp ?? "")}</dd></div><div><dt>Age</dt><dd>{state.age?.toFixed(0)} s</dd></div></dl> : <p>No fresh, valid sample is available for this mission and UAV.</p>}<p>Charge in mAh is estimated from configured capacity, not measured full-charge capacity. This comparison does not replace preflight checks or approval.</p></details>
    <Link className={styles.connectButton} href="/portal/drone-connection"><Radio size={16} aria-hidden="true" />Connect / inspect UAV<ArrowUpRight size={15} aria-hidden="true" /></Link>
  </section>;
}
