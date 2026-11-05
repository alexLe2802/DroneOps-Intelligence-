"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/auth/client";
import type { PilotProfile } from "@/lib/portal/types";

export default function OperatorEdit({ pilot, onUpdated }: { pilot: PilotProfile; onUpdated: () => Promise<void> }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    const data = new FormData(event.currentTarget);
    const birth = String(data.get("dateOfBirth") ?? "");
    setBusy(true); setError(""); setMessage("");
    try {
      await api("/users/pilot-profile", { method: "PUT", body: JSON.stringify({ phoneNumber: String(data.get("phoneNumber") ?? "").trim(), dateOfBirth: birth || undefined, experienceYears: Number(data.get("experienceYears")) }) });
      await onUpdated(); setMessage("Operator information updated.");
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to update profile."); }
    finally { setBusy(false); }
  }
  return <section className="mission-fields"><h2>Edit operator information</h2><form className="profile-form" onSubmit={submit}>
    <label>Phone number<input name="phoneNumber" type="tel" maxLength={20} defaultValue={pilot.phoneNumber ?? ""} disabled={busy} /></label>
    <label>Date of birth<input name="dateOfBirth" type="date" required defaultValue={pilot.dateOfBirth ?? ""} max={new Date(new Date().getFullYear() - 18, new Date().getMonth(), new Date().getDate()).toLocaleDateString("en-CA")} disabled={busy} /></label>
    <label>Flight experience (years)<input name="experienceYears" type="number" min={0} step={1} required defaultValue={pilot.experienceYears} disabled={busy} /></label>
    {error && <p role="alert">{error}</p>}{message && <p className="module-success" role="status">{message}</p>}<button className="module-primary" disabled={busy}>{busy ? "Saving…" : "Save operator information"}</button>
  </form></section>;
}
