import { incidentDescriptionFindings } from "@/lib/portal/incident-display";
import styles from "./incident-description.module.css";

export default function IncidentDescription({ value, fallback }: { value: string | null | undefined; fallback: string }) {
  const findings = incidentDescriptionFindings(value);
  const text = findings.join(" ") || fallback;
  if (text.length <= 240) return <p>{text}</p>;
  const boundary = text.lastIndexOf(" ", 240);
  const preview = text.slice(0, boundary > 0 ? boundary : 240);
  return <div className={styles.description}>
    <p>{preview}…</p>
    <details>
      <summary>Show full description</summary>
      <ul>{(findings.length ? findings : [fallback]).map((finding, index) => <li key={index}>{finding}</li>)}</ul>
    </details>
  </div>;
}
