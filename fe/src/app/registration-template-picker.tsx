"use client";

import { useRef, useState, type ChangeEvent } from "react";
import { normalizeTemplateValue, parseRegistrationTemplate, registrationTemplate, type TemplateKind } from "@/lib/registration-template";

export default function RegistrationTemplatePicker({ kind, onApply, disabled }: {
  kind: TemplateKind;
  onApply?: (values: Record<string, string>) => void;
  disabled?: boolean;
}) {
  const [message, setMessage] = useState("");
  const latest = useRef(0);
  async function importFile(event: ChangeEvent<HTMLInputElement>) {
    const input = event.currentTarget;
    const file = input.files?.[0];
    const form = input.form;
    input.value = "";
    if (!file || !form) return;
    const attempt = ++latest.current;
    try {
      if (file.size > 64 * 1024) throw new Error("Choose a template smaller than 64 KB.");
      const text = await file.text();
      if (latest.current !== attempt || !input.isConnected || input.matches(":disabled")) return;
      const entries = parseRegistrationTemplate(kind, text);
      const updates = [];
      const skipped = [];
      for (const [name, raw] of entries) {
        const control = form.elements.namedItem(name);
        if (!(control instanceof HTMLInputElement || control instanceof HTMLTextAreaElement)) throw new Error("This field is unavailable.");
        const check = control.cloneNode() as typeof control;
        const value = normalizeTemplateValue(control.type, raw);
        check.value = value;
        // Browsers discard malformed date/number values and only check length for user edits.
        if (!check.value || (check.maxLength >= 0 && value.length > check.maxLength) || value.length < check.minLength) {
          skipped.push(control.labels?.[0]?.firstChild?.textContent?.trim() || name);
          continue;
        }
        updates.push({ control, value: check.value });
      }
      if (onApply) onApply(Object.fromEntries(updates.map(({ control, value }) => [control.name, value])));
      else for (const { control, value } of updates) control.value = value;
      setMessage(`Filled ${updates.length} field(s). ${skipped.length ? `Enter or review these fields manually: ${skipped.join(", ")}. ` : ""}Review the form before submitting.`);
    } catch (error) {
      if (latest.current === attempt) setMessage(error instanceof Error ? error.message : "Unable to read the template.");
    }
  }
  return <section aria-label="Fill from a template" style={{display: "grid", gap: "0.5rem", padding: "1rem"}}>
    <strong>Fill from a template</strong>
    <p>Choose your completed text file to fill this form. {kind === "pilot"
      ? "Enter your password and attach your license document below."
      : "You can add a photo in the Aircraft image section below."} Review your entries before submitting.</p>
    <a download={`droneops-${kind}-registration.txt`} href={`data:text/plain;charset=utf-8,${encodeURIComponent("\uFEFF" + registrationTemplate(kind))}`}>Download template (.txt)</a>
    <label>Import completed template <input type="file" accept=".txt,text/plain" disabled={disabled} onChange={importFile} /></label>
    <span role="status">{message}</span>
  </section>;
}
