"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { Clock3, Lightbulb, Route, Sparkles } from "lucide-react";
import { generateMissionSuggestion, getMyMissionSuggestions } from "@/lib/portal/client";
import type { MissionSuggestion } from "@/lib/portal/types";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import styles from "./mission-suggestion-panel.module.css";

const minimumPromptLength = 10;
const maximumPromptLength = 3000;

type MissionSuggestionPanelProps = {
  disabled?: boolean;
  onApply: (suggestion: MissionSuggestion) => void;
};

export default function MissionSuggestionPanel({ disabled = false, onApply }: MissionSuggestionPanelProps) {
  const [prompt, setPrompt] = useState("");
  const [items, setItems] = useState<MissionSuggestion[] | null>(null);
  const [selectedId, setSelectedId] = useState("");
  const [appliedId, setAppliedId] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const request = useRef(false);
  const selected = selectedId ? items?.find(item => item.id === selectedId) : undefined;
  const trimmedPrompt = prompt.trim();
  const promptError = trimmedPrompt.length > 0 && trimmedPrompt.length < minimumPromptLength
    ? `Describe the mission in at least ${minimumPromptLength} characters.`
    : trimmedPrompt.length > maximumPromptLength
      ? `Keep the request within ${maximumPromptLength} characters.`
      : "";

  const loadHistory = useCallback(() => {
    setError("");
    setItems(null);
    void getMyMissionSuggestions()
      .then(history => {
        setItems(history);
        setSelectedId(current => history.some(item => item.id === current) ? current : "");
      })
      .catch(reason => {
        setItems([]);
        setError(reason instanceof Error ? reason.message : "Suggestion history is unavailable.");
      });
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(loadHistory, 0);
    return () => window.clearTimeout(timer);
  }, [loadHistory]);

  async function generate(event: FormEvent) {
    event.preventDefault();
    if (request.current || !trimmedPrompt || promptError) return;
    request.current = true;
    setBusy(true);
    setError("");
    setAppliedId("");
    try {
      const created = await generateMissionSuggestion(trimmedPrompt);
      setItems(current => [created, ...(current ?? []).filter(item => item.id !== created.id)]);
      setSelectedId(created.id);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Mission suggestion is unavailable. Continue with manual planning.");
    } finally {
      request.current = false;
      setBusy(false);
    }
  }

  function applySelected() {
    if (!selected || disabled) return;
    onApply(selected);
    setAppliedId(selected.id);
  }

  const waypointCount = selected?.suggestedWaypointCount || 0;

  return <section className={styles.panel} aria-labelledby="mission-suggestion-title">
    <header className={styles.header}>
      <div><p className={styles.eyebrow}>PILOT / MISSION DRAFT ASSISTANT</p><h2 id="mission-suggestion-title">Describe a mission, then review the proposed guidance</h2><p>Provide the objective, location, timing and constraints. The assistant suggests mission details only; the pilot still selects the UAV, schedule and every waypoint.</p></div>
      <span className={styles.icon}><Sparkles size={22} aria-hidden="true" /></span>
    </header>

    <form className={styles.form} onSubmit={generate}>
      <label htmlFor="mission-suggestion-prompt">WHAT DO YOU NEED THE UAV TO DO?</label>
      <textarea id="mission-suggestion-prompt" value={prompt} minLength={minimumPromptLength} maxLength={maximumPromptLength} disabled={busy || disabled} aria-invalid={Boolean(promptError)} aria-describedby={promptError ? "mission-suggestion-hint mission-suggestion-error" : "mission-suggestion-hint"} placeholder="Example: Inspect the eastern solar array before 10:00, avoid the residential boundary and prioritize stable image coverage." onChange={event => setPrompt(event.target.value)} />
      <div className={styles.formFooter}><small id="mission-suggestion-hint">Include location, purpose, timing and known constraints · {prompt.length}/{maximumPromptLength}</small><button className="module-primary" type="submit" disabled={busy || disabled || !trimmedPrompt || Boolean(promptError)}><Sparkles size={15} aria-hidden="true" />{busy ? "GENERATING…" : "GENERATE DRAFT"}</button></div>
      {promptError && <p className={styles.inlineError} id="mission-suggestion-error" role="alert">{promptError}</p>}
      {error && <div className={styles.error} role="alert"><p>{error}</p><button type="button" className="module-secondary" disabled={busy || disabled} onClick={loadHistory}>RETRY HISTORY</button></div>}
    </form>

    {selected && <article className={styles.result}>
      <header><div><span className={styles.eyebrow}>PROPOSED MISSION DRAFT</span><h3>{selected.missionName || "Untitled mission suggestion"}</h3><p>{selected.summary || selected.missionPurpose}</p></div><time><Clock3 size={13} aria-hidden="true" />{formatVietnamDateTime(selected.createdAt)}</time></header>
      <dl className={styles.stats}>
        <div><dt>Altitude</dt><dd>{selected.suggestedAltitude ?? "—"} m</dd></div>
        <div><dt>Waypoints</dt><dd>{waypointCount}</dd></div>
        <div><dt>Duration</dt><dd>{selected.suggestedFlightDuration ?? "—"} min</dd></div>
        <div><dt>Cruise speed</dt><dd>{selected.averageSpeedMetersPerSecond ?? "—"} m/s</dd></div>
        <div><dt>Speed ceiling</dt><dd>{selected.maxSpeedMetersPerSecond ?? "—"} m/s</dd></div>
      </dl>
      <div className={styles.advice}><section><h4><Route size={15} aria-hidden="true" />Mission purpose</h4><p>{selected.missionPurpose || "No purpose was returned."}</p></section><section><h4><Lightbulb size={15} aria-hidden="true" />Planning cautions</h4><p>{selected.riskAssessment || "No planning caution was returned. Mandatory validation is still required."}</p></section><section><h4><Sparkles size={15} aria-hidden="true" />Recommendations</h4><p>{selected.recommendations || "No recommendation was returned."}</p></section></div>
      <footer><span>Waypoint coordinates are never applied. Place and verify the route manually on the map.</span><button className="module-primary" type="button" disabled={disabled} onClick={applySelected}>{appliedId === selected.id ? "DETAILS APPLIED" : "APPLY MISSION DETAILS"}</button></footer>
    </article>}

    {items && items.length > 0 && <details className={styles.history}>
      <summary>Previous mission suggestions <span>{items.length}</span></summary>
      <div>{items.map(item => <button key={item.id} type="button" aria-pressed={item.id === selected?.id} onClick={() => { setSelectedId(item.id); setAppliedId(""); }}><b>{item.missionName || "Untitled suggestion"}</b><span>{formatVietnamDateTime(item.createdAt)}</span><small>{item.prompt}</small></button>)}</div>
    </details>}
  </section>;
}
