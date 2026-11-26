"use client";

import { useRef, useState } from "react";
import { Sparkles } from "lucide-react";
import { analyzeMissionDraft } from "@/lib/portal/client";
import type { DraftAnalysis, DraftAnalysisRequest } from "@/lib/portal/types";
import { formatVietnamDateTime, toVietnamDateTimeInput } from "@/lib/portal/vietnam-time";
import styles from "./draft-analysis-panel.module.css";

type Props = {
  draft: DraftAnalysisRequest | null; missing: string[]; disabled: boolean;
  applyCruise: (value: string) => void; applyCeiling: (value: string) => void; applyEnd: (value: string) => void;
};

export default function DraftAnalysisPanel({ draft, missing, disabled, applyCruise, applyCeiling, applyEnd }: Props) {
  const [result, setResult] = useState<DraftAnalysis | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const pending = useRef(false);
  async function analyze() {
    if (!draft || disabled || pending.current) return;
    if (Date.parse(draft.startTime) <= Date.now()) { setError("Choose a future start date and time."); return; }
    pending.current = true;
    setBusy(true);
    setError("");
    try { setResult(await analyzeMissionDraft(draft)); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Draft analysis unavailable."); }
    finally { setBusy(false); pending.current = false; }
  }

  return <section className={styles.panel} aria-labelledby="draft-analysis-title">
    <header className={styles.header}>
      <div className={styles.titleGroup}>
        <span className={styles.icon} aria-hidden="true"><Sparkles size={20} /></span>
        <div>
          <span className={styles.kicker}>AI DRAFT ASSISTANT</span>
          <h2 id="draft-analysis-title">Analyze mission with AI</h2>
          <p>Check this aircraft, route, wind limits, and forecast before saving your draft.</p>
        </div>
      </div>
      <button type="button" className={styles.runButton} onClick={analyze} disabled={disabled || !draft || busy}
        aria-describedby={missing.length ? "analysis-requirements" : undefined}>
        <Sparkles size={15} aria-hidden="true" />
        {busy ? "Analyzing…" : "Use AI"}
      </button>
    </header>

    {missing.length > 0 && <ul id="analysis-requirements" className={styles.requirements}>{missing.map(item => <li key={item}>{item}</li>)}</ul>}
    {error && <p className={styles.error} role="alert">{error}</p>}

    {result && draft && <article className={styles.result}>
      <header className={styles.resultHeader}>
        <h3>Planning advice</h3>
        <span className={styles.forecastPill}>
          Forecast: {result.weatherStatus} · {result.provider} · {formatVietnamDateTime(result.fetchedAt)}
        </span>
      </header>
      <div className={styles.resultBody}>
        <p className={styles.explanation}>{result.explanation || "AI explanation unavailable. Calculated findings are shown below."}</p>
        {result.findings.length > 0 && <ul className={styles.findings}>{result.findings.map(item => <li key={item}>{item}</li>)}</ul>}
        {result.windAlerts.length > 0 && <div className={styles.windAlerts}>
          {result.windAlerts.map((item, i) => <p className={styles.windAlert} key={i}>
            {item.segmentFrom != null ? `WP${item.segmentFrom}–WP${item.segmentTo}` : `WP${item.sequenceOrder}`} · {item.estimatedArrivalTime ? formatVietnamDateTime(item.estimatedArrivalTime) : "Arrival unknown"} · Wind {item.windSpeedMetersPerSecond ?? "—"} / limit {result.windLimit ?? "—"} m/s · Gust {item.windGustMetersPerSecond ?? "—"} / limit {result.gustLimit ?? "—"} m/s · {item.riskLevel}
          </p>)}
        </div>}
      </div>
      <dl className={styles.stats}>
        <div>
          <dt>Cruise speed</dt>
          <dd>{result.cruise ?? "—"} m/s</dd>
          {result.cruiseBasis && <small>{result.cruiseBasis}</small>}
          {result.cruise != null && result.cruise !== draft.cruise && <button type="button" className={styles.applyButton} onClick={() => applyCruise(String(result.cruise))}>Apply cruise speed</button>}
        </div>
        <div>
          <dt>Speed ceiling</dt>
          <dd>{result.ceiling ?? "—"} m/s</dd>
          <small>Aircraft limit and applicable plan limits</small>
          {result.ceiling != null && result.ceiling !== draft.ceiling && <button type="button" className={styles.applyButton} onClick={() => applyCeiling(String(result.ceiling))}>Apply ceiling</button>}
        </div>
        <div>
          <dt>Estimated finish</dt>
          <dd>{result.estimatedEnd ? formatVietnamDateTime(result.estimatedEnd) : "Unavailable"}</dd>
          {result.estimatedEnd && <button type="button" className={styles.applyButton} onClick={() => applyEnd(toVietnamDateTimeInput(result.estimatedEnd!))}>Use estimated finish</button>}
        </div>
      </dl>
    </article>}
  </section>;
}
