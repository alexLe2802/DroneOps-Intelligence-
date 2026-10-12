"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import type { Account } from "@/lib/auth/types";
import { useRouter } from "next/navigation";
import ActiveSessions from "./active-sessions";
import { getProfile, updateProfile } from "@/lib/portal/client";
import type { UserProfile } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../module-state";

export default function ProfileClient({ account }: { account: Account }) {
  const router = useRouter();
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [name, setName] = useState(account.displayName);
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");
  const load = useCallback(() => {
    setProfile(null); setError(null);
    void getProfile().then(value => { setProfile(value); setName(value.fullName); }).catch(setError);
  }, []);
  useEffect(() => {
    let active = true;
    void getProfile()
      .then(value => { if (active) { setProfile(value); setName(value.fullName); } })
      .catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, []);
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError(null); setMessage("");
    try {
      const updated = await updateProfile({ fullName: name.trim() });
      setProfile(updated); setName(updated.fullName); setMessage("Profile updated successfully."); router.refresh();
    } catch (reason) { setError(reason); }
    finally { setSaving(false); }
  }
  if (error) return <ModuleError error={error} retry={load} />;
  if (!profile) return <ModuleLoading label="Loading your profile" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">ACCOUNT / PERSONAL DETAILS</p><h1>Profile</h1><p>Review your identity and update the name shown in DroneOps.</p></div></header>
    <section className="profile-layout"><aside className="profile-summary"><span>{profile.fullName.slice(0, 1).toUpperCase()}</span><h2>{profile.fullName}</h2><p>{profile.email}</p><b>{profile.role.replaceAll("_", " ")}</b><small>MEMBER SINCE {new Date(profile.createdAt).toLocaleDateString()}</small></aside><form className="profile-form" onSubmit={submit}><div><p className="module-kicker">EDIT PROFILE</p><h2>Personal information</h2></div><label>FULL NAME<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={100} required /></label><label>EMAIL ADDRESS<input value={profile.email} readOnly disabled /></label><p className="field-help">Email and role are managed by your Operations Manager.</p>{message && <p className="module-success" role="status">{message}</p>}<button className="module-primary" disabled={saving || name.trim() === profile.fullName}>{saving ? "SAVING…" : "SAVE CHANGES"}</button></form></section>
    <ActiveSessions />
  </>;
}
