"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { createUav, updateAssignedUav } from "@/lib/portal/client";

import type { AssignedUav } from "@/lib/portal/types";

export default function UavForm({ aircraft }: { aircraft?: AssignedUav }) {
  const pending = useRef(false);
  const router = useRouter();
  const [code, setCode] = useState(aircraft?.code ?? "");
  const [name, setName] = useState(aircraft?.name ?? "");
  const [model, setModel] = useState(aircraft?.model ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (pending.current) return;
    if (!code.trim() || !name.trim()) { setError("UAV code and name are required."); return; }
    pending.current = true; setSaving(true); setError("");
    const request = { code: code.trim().toUpperCase(), name: name.trim(), model: model.trim() || null };
    try {
      if (aircraft) {
        await updateAssignedUav(aircraft.id, request);
        router.push(`/portal/uav/${aircraft.id}?submitted=update`);
      } else {
        const result = await createUav(request);
        router.push(`/portal/uav/${result.id}?submitted=create`);
      }
      router.refresh();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to submit the UAV request."); }
    finally { pending.current = false; setSaving(false); }
  }
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / UAV REQUEST</p><h1>{aircraft ? "Edit UAV" : "Add UAV"}</h1><p>{aircraft ? "Update your aircraft registration details." : "Register an aircraft and send it to an admin for approval."}</p></div></header>
    <form className="uav-form" onSubmit={submit}>
      <fieldset disabled={saving} style={{ border: 0, padding: 0, margin: 0, display: "grid", gap: 18 }}><legend className="sr-only">Aircraft details</legend><label>UAV CODE<input value={code} onChange={event => setCode(event.target.value)} placeholder="UAV-08" minLength={2} maxLength={30} required /></label>
      <label>UAV NAME<input value={name} onChange={event => setName(event.target.value)} placeholder="South Survey Two" minLength={2} maxLength={100} required /></label>
      <label>MODEL<input value={model} onChange={event => setModel(event.target.value)} placeholder="DJI Matrice 350 RTK" maxLength={100} /></label>
      </fieldset><p className="field-help">{aircraft ? "Changes are saved to your UAV record. Operational status is managed by the Operations Manager." : "The UAV is created with Pending status and becomes operational after an admin approves it."}</p>
      {error && <p className="form-error" role="alert" aria-live="assertive">{error}</p>}
      <div className="form-actions"><Link className="module-secondary module-link" href={aircraft ? `/portal/uav/${aircraft.id}` : "/portal/uav"}>CANCEL</Link><button className="module-primary" disabled={saving}>{saving ? "SAVING…" : aircraft ? "SAVE CHANGES" : "SUBMIT FOR APPROVAL"}</button></div>
    </form>
  </>;
}
