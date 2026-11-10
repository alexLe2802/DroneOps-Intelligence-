"use client";
import { useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { api } from "@/lib/auth/client";
type Settings = { lowBatteryCriticalPercent: number | null; lowBatteryPercent: number; lowBatteryRecoveryMarginPercent: number; minimumAltitudeMslMeters: number | null; maximumAltitudeMslMeters: number | null; communicationLossSeconds: number; scanIntervalSeconds: number; staleAfterSeconds: number; offlineAfterSeconds: number };
type Revision = { id: string; changedBy: string; createdAt: string };
type Rules = { settings: Settings; revisions: Revision[] };
type Alert = { id: string; incidentId: string; incidentTitle: string; eventType: string; createdAt: string };
export default function MonitoringSettings({ manager }: { manager: boolean }) {
  const [rules, setRules] = useState<Rules | null>(null);
  const [alerts, setAlerts] = useState<Alert[]>([]);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let active = true;
    async function load() {
      try { const list = await api<Alert[]>("/incidents/alerts/unread"); if (active) setAlerts(list); }
      catch (reason) { if (active) setError(reason instanceof Error ? reason.message : "Alerts unavailable."); }
    }
    void load();
    if (manager) void api<Rules>("/incidents/rules").then(value => { if (active) setRules(value); }).catch(reason => { if (active) setError(String(reason)); });
    const timer = setInterval(() => { if (document.visibilityState === "visible") void load(); }, 10000);
    return () => { active = false; clearInterval(timer); };
  }, [manager]);
  async function read(id: string) {
    try { await api(`/incidents/alerts/${id}/read`, { method: "POST" }); setAlerts(list => list.filter(item => item.id !== id)); }
    catch (reason) { setError(String(reason)); }
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!rules || busy) return;
    setBusy(true); setError(""); setMessage("");
    const data = new FormData(event.currentTarget);
    const settings = { ...rules.settings };
    for (const key of Object.keys(settings) as (keyof Settings)[]) {
      const value = data.get(key);
      (settings as Record<string, number | null>)[key] = value === "" ? null : Number(value);
    }
    try {
      await api("/incidents/rules", { method: "PUT", body: JSON.stringify({ settings, expectedRevision: rules.revisions[0]?.id ?? null }) });
      setRules(await api<Rules>("/incidents/rules")); setMessage("Detector settings saved with actor and timestamp.");
    } catch (reason) { setError(String(reason)); }
    finally { setBusy(false); }
  }
  return <section aria-label="Incident alerts and detector settings">
    {error && <p role="alert">{error}</p>}{message && <p role="status">{message}</p>}
    <details><summary>Unread operational alerts ({alerts.length})</summary>
      {alerts.length === 0 ? <p>No unread incident alerts.</p> : <ul>{alerts.map(item => <li key={item.id}><Link href={`/portal/incidents/${item.incidentId}`}>{item.incidentTitle}</Link> · {item.eventType} <button type="button" onClick={() => void read(item.id)}>Mark read</button></li>)}</ul>}
    </details>
    {manager && rules && <details><summary>Detector thresholds and configuration history</summary>
      <form onSubmit={save} key={rules.revisions[0]?.id ?? "defaults"}>
        {(Object.entries({ lowBatteryCriticalPercent: "Critical battery threshold (%, optional)", lowBatteryPercent: "Low battery (%)", lowBatteryRecoveryMarginPercent: "Battery recovery margin (%)", minimumAltitudeMslMeters: "Minimum MSL altitude (m, optional)", maximumAltitudeMslMeters: "Maximum MSL altitude (m, optional)", communicationLossSeconds: "Communication loss (seconds)", scanIntervalSeconds: "Detector interval (seconds)", staleAfterSeconds: "Stale after (seconds)", offlineAfterSeconds: "Offline after (seconds)" }) as [keyof Settings, string][]).map(([key, label]) => <label key={key} style={{ display: "block", marginBlock: 8 }}>{label} <input name={key} type="number" step="any" required={!key.includes("Altitude") && key !== "lowBatteryCriticalPercent"} defaultValue={rules.settings[key] ?? ""} /></label>)}
        <button type="submit" disabled={busy}>{busy ? "Saving…" : "Save detector settings"}</button>
      </form>
      <ul>{rules.revisions.map(revision => <li key={revision.id}>{revision.createdAt} · {revision.changedBy}</li>)}</ul>
    </details>}
  </section>;
}
