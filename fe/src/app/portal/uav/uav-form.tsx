"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { createUav, updateUav } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";

export default function UavForm({ aircraft }: { aircraft?: AssignedUav }) {
  const router = useRouter();
  const [code, setCode] = useState(aircraft?.code ?? "");
  const [name, setName] = useState(aircraft?.name ?? "");
  const [model, setModel] = useState(aircraft?.model ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError("");
    const request = { code: code.trim().toUpperCase(), name: name.trim(), model: model.trim() || null };
    try {
      const result = aircraft ? await updateUav(aircraft.id, request) : await createUav(request);
      router.push(`/portal/uav/${result.id}?submitted=${aircraft ? "update" : "create"}`);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to submit the UAV request."); }
    finally { setSaving(false); }
  }
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / UAV REQUEST</p><h1>{aircraft ? "Update UAV" : "Add UAV"}</h1><p>{aircraft ? "Submit changed UAV information for admin approval." : "Register an aircraft and send it to an admin for approval."}</p></div></header>
    <form className="uav-form" onSubmit={submit}>
      <label>UAV CODE<input value={code} onChange={event => setCode(event.target.value)} placeholder="UAV-08" minLength={2} maxLength={30} required /></label>
      <label>UAV NAME<input value={name} onChange={event => setName(event.target.value)} placeholder="South Survey Two" minLength={2} maxLength={100} required /></label>
      <label>MODEL<input value={model} onChange={event => setModel(event.target.value)} placeholder="DJI Matrice 350 RTK" maxLength={100} /></label>
      <p className="field-help">An admin must approve new or updated information before it becomes official.</p>
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="form-actions"><Link className="module-secondary module-link" href={aircraft ? `/portal/uav/${aircraft.id}` : "/portal/uav"}>CANCEL</Link><button className="module-primary" disabled={saving}>{saving ? "SUBMITTING…" : "SUBMIT FOR APPROVAL"}</button></div>
    </form>
  </>;
}
