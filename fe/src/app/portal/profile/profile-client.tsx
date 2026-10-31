"use client";

import UserAvatar from "../user-avatar";
import styles from "./profile-summary.module.css";
import EmailChange from "./email-change";
import OperatorEdit from "./operator-edit";
import LicenseChanges from "./license-changes";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "@/lib/auth/client";
import type { Account } from "@/lib/auth/types";
import { useRouter } from "next/navigation";
import { getPilotProfile, getProfile, updateProfile } from "@/lib/portal/client";
import type { PilotProfile, UserProfile } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../module-state";

export default function ProfileClient({ account }: { account: Account }) {
  const router = useRouter();
  const [pilot, setPilot] = useState<PilotProfile | null>(null);
  const [pilotError, setPilotError] = useState("");
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [name, setName] = useState(account.displayName);
  const [error, setError] = useState<unknown>(null);
  const [saving, setSaving] = useState(false);
  const [avatarFile, setAvatarFile] = useState<File | null>(null);
  const avatarInput = useRef<HTMLInputElement>(null);
  const [avatarPreview, setAvatarPreview] = useState("");
  const [avatarMessage, setAvatarMessage] = useState("");
  useEffect(() => () => { if (avatarPreview) URL.revokeObjectURL(avatarPreview); }, [avatarPreview]);
  function chooseAvatar(file: File | undefined) {
    setAvatarError(""); setAvatarMessage("");
    if (!file) return;
    if (!["image/jpeg", "image/png", "image/webp"].includes(file.type) || file.size > 5 * 1024 * 1024) {
      setAvatarError("Choose a JPEG, PNG or WebP image up to 5 MB.");
      if (avatarInput.current) avatarInput.current.value = "";
      return;
    }
    setAvatarFile(file); setAvatarPreview(URL.createObjectURL(file));
  }
  const [avatarError, setAvatarError] = useState("");
  const [uploading, setUploading] = useState(false);
  async function uploadAvatar() {
    if (!avatarFile || uploading) return;
    setUploading(true); setAvatarError(""); setAvatarMessage("");
    try {
      if (!["image/jpeg", "image/png", "image/webp"].includes(avatarFile.type) || avatarFile.size > 5 * 1024 * 1024) throw new Error("Choose a JPEG, PNG or WebP image up to 5 MB.");
      const body = new FormData(); body.set("file", avatarFile);
      const result = await api<{ avatarUrl: string }>("/users/avatar", { method: "POST", body });
      setProfile(current => current ? { ...current, avatarUrl: result.avatarUrl } : current);
      setAvatarFile(null); setAvatarPreview(""); if (avatarInput.current) avatarInput.current.value = ""; setAvatarMessage("Avatar updated successfully."); router.refresh();
    } catch (e) { setAvatarError(e instanceof Error ? e.message : "Avatar upload failed."); }
    finally { setUploading(false); }
  }
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
  useEffect(() => {
    if (account.role !== "uav_operator") return;
    let active = true;
    void getPilotProfile().then(value => { if (active) setPilot(value); }).catch(reason => {
      if (active) setPilotError(reason instanceof Error ? reason.message : "Unable to load operator license details.");
    });
    return () => { active = false; };
  }, [account.role]);
  function retryPilot() {
    setPilotError("");
    void getPilotProfile().then(setPilot).catch(reason => setPilotError(reason instanceof Error ? reason.message : "Unable to load operator details."));
  }
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
    <section className="profile-layout"><aside className={styles.card}>
      <UserAvatar src={avatarPreview || profile.avatarUrl} name={profile.fullName} size={96} className={styles.avatar} />
      <div className={styles.controls}>
        <input ref={avatarInput} className={styles.hiddenInput} type="file" accept="image/jpeg,image/png,image/webp" aria-label="Choose avatar image" disabled={uploading} onChange={e => chooseAvatar(e.target.files?.[0])} />
        <button type="button" className={styles.choose} disabled={uploading} onClick={() => avatarInput.current?.click()}>Choose image</button>
        <p className={styles.hint}>JPEG, PNG or WebP · up to 5 MB</p>
        {avatarFile && <p className={styles.filename}>{avatarFile.name}</p>}
        <button type="button" className="module-primary" disabled={!avatarFile || uploading} onClick={() => void uploadAvatar()}>{uploading ? "Uploading…" : "Save avatar"}</button>
        {avatarError && <p className={styles.error} role="alert">{avatarError}</p>}
        {avatarMessage && <p className={styles.success} role="status">{avatarMessage}</p>}
      </div>
      <div className={styles.identity}><h2>{profile.fullName}</h2><p>{profile.email}</p><b className={styles.role}>{profile.role.replaceAll("_", " ")}</b><small className={styles.member}>Member since {new Date(profile.createdAt).toLocaleDateString()}</small></div>
    </aside><form className="profile-form" onSubmit={submit}><div><p className="module-kicker">EDIT PROFILE</p><h2>Personal information</h2></div><label>FULL NAME<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={100} required /></label><label>EMAIL ADDRESS<input value={profile.email} readOnly disabled /></label><p className="field-help">Verify your new address below to change your email. Your role is managed by your Operations Manager.</p>{message && <p className="module-success" role="status">{message}</p>}<button className="module-primary" disabled={saving || name.trim() === profile.fullName}>{saving ? "SAVING…" : "SAVE CHANGES"}</button></form></section>
    {account.role === "uav_operator" && <section className="mission-fields" aria-label="Operator license details"><h2>Operator & flight license</h2>{pilotError ? <div role="alert"><p>{pilotError}</p><button className="module-secondary" type="button" onClick={retryPilot}>RETRY</button></div> : !pilot ? <p role="status">Loading operator details…</p> : <div className="request-detail-grid">{[
      ["Phone", pilot.phoneNumber], ["Date of birth", pilot.dateOfBirth],
      ["License number", pilot.pilotLicenseNo], ["License issued", pilot.licenseIssuedDate],
      ["License expires", pilot.licenseExpiredDate], ["Experience", `${pilot.experienceYears} years`],
      ["Drone type", pilot.droneType], ["Drone model", pilot.droneModel],
    ].map(([label, value]) => <div key={label}><span>{label}</span><b>{value || "Not provided"}</b></div>)}</div>}</section>}

    <EmailChange onChanged={async () => { window.location.replace("/login?emailChanged=1"); }} />
    {account.role === "uav_operator" && pilot && <OperatorEdit key={`${pilot.phoneNumber}-${pilot.dateOfBirth}-${pilot.experienceYears}`} pilot={pilot} onUpdated={async () => setPilot(await getPilotProfile())} />}
    {account.role === "uav_operator" && <LicenseChanges onRefresh={retryPilot} />}
  </>;
}
