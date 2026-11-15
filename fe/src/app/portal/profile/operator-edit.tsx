"use client";

import { vietnamDateYearsAgo } from "@/lib/portal/vietnam-time";

import { useState, type FormEvent } from "react";
import { ApiError, api } from "@/lib/auth/client";
import type { PilotProfile } from "@/lib/portal/types";

export default function OperatorEdit({ pilot, onUpdated }: { pilot: PilotProfile; onUpdated: () => Promise<void> }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [stale, setStale] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || stale) return;
    const data = new FormData(event.currentTarget);
    const birth = String(data.get("dateOfBirth") ?? "");
    setBusy(true); setError(""); setMessage("");
    try {
      await api("/users/pilot-profile", { method: "PUT", body: JSON.stringify({ expectedVersion: pilot.profileVersion, phoneNumber: String(data.get("phoneNumber") ?? "").trim(), dateOfBirth: birth || undefined, experienceYears: Number(data.get("experienceYears")) }) });
      await onUpdated(); setMessage("Operator information updated.");
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) { setStale(true); setError("Your profile changed in another tab. Reload the latest information before saving again."); }
      else setError(e instanceof Error ? e.message : "Unable to update profile.");
    }
    finally { setBusy(false); }
  }
  return <section className="mission-fields"><h2>Edit operator information</h2><form className="profile-form" onSubmit={submit}>
    <label>Phone number<input name="phoneNumber" type="tel" maxLength={20} defaultValue={pilot.phoneNumber ?? ""} disabled={busy} /></label>
    <label>Date of birth<input name="dateOfBirth" type="date" required defaultValue={pilot.dateOfBirth ?? ""} max={vietnamDateYearsAgo(18)} disabled={busy} /></label>
    <label>Flight experience (years)<input name="experienceYears" type="number" min={0} step={1} required defaultValue={pilot.experienceYears} disabled={busy} /></label>
    {error && <p role="alert">{error}</p>}{stale && <button className="module-secondary" type="button" disabled={busy} onClick={() => void onUpdated().then(() => { setStale(false); setError(""); }).catch(() => setError("Unable to reload your profile. Please try again."))}>Reload latest profile</button>}{message && <p className="module-success" role="status">{message}</p>}<button className="module-primary" disabled={busy || stale}>{busy ? "Saving…" : "Save operator information"}</button>
  </form></section>;
}
