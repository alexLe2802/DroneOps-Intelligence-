"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "@/lib/auth/client";
import { roleLabels, type Account, type SessionInfo } from "@/lib/auth/types";

type PortalControlsProps = {
  account: Account;
  registerOnly?: boolean;
};

export default function PortalControls({ account, registerOnly = false }: PortalControlsProps) {
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [accountType, setAccountType] = useState("new");
  const [showProvisionForm, setShowProvisionForm] = useState(false);
  const pending = useRef(false);
  const manager = account.role === "operations_manager";
  const handleError = useCallback((e: unknown) => {
    if (e instanceof ApiError && (e.status === 401 || e.status === 403)) { window.location.replace("/login"); return; }
    setError(e instanceof Error ? e.message : "Request failed. Please try again.");
  }, []);
  const load = useCallback(async () => {
    const [nextSessions, nextAccounts] = await Promise.all([
      api<SessionInfo[]>("/auth/sessions"), manager ? api<Account[]>("/accounts") : Promise.resolve([]),
    ]);
    setSessions(nextSessions); setAccounts(nextAccounts); setLoaded(true);
  }, [manager]);
  useEffect(() => {
    let alive = true;
    async function verify() { try { await api("/auth/me"); } catch (e) { if (alive) handleError(e); } }
    Promise.all([
      api<SessionInfo[]>("/auth/sessions"), manager ? api<Account[]>("/accounts") : Promise.resolve([]),
    ]).then(([nextSessions, nextAccounts]) => {
      if (alive) { setSessions(nextSessions); setAccounts(nextAccounts); setLoaded(true); }
    }).catch(e => { if (alive) handleError(e); });
    // Revalidate on focus/BFCache restore and periodically, including expiry or revocation from another tab.
    const focus = () => { if (document.visibilityState === "visible") void verify(); };
    window.addEventListener("pageshow", focus);
    document.addEventListener("visibilitychange", focus);
    const timer = window.setInterval(focus, 60000);
    return () => { alive = false; clearInterval(timer); window.removeEventListener("pageshow", focus); document.removeEventListener("visibilitychange", focus); };
  }, [manager, handleError]);
  async function act(task: () => Promise<void>) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(""); setMessage("");
    try { await task(); } catch (e) { handleError(e); }
    finally { pending.current = false; setBusy(false); }
  }
  const logout = (all: boolean) => act(async () => {
    await api(all ? "/auth/logout-all" : "/auth/logout", { method: "POST" });
    window.location.replace("/login");
  });
  async function provision(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = new FormData(form);
    const read = (name: string) => String(values.get(name) ?? "").trim();
    const displayName = read("fullName");
    const password = accountType === "new" ? String(values.get("password") ?? "") : undefined;
    if (!displayName) { setError("Enter the Pilot's full name."); setMessage(""); return; }
    if (password !== undefined && (!password.trim() || password !== values.get("confirmPassword"))) {
      setError("Enter a password and make sure both passwords match."); setMessage(""); return;
    }
    const licenseIssuedDate = read("licenseIssuedDate");
    const licenseExpiredDate = read("licenseExpiredDate");
    if (licenseIssuedDate && licenseExpiredDate && new Date(licenseExpiredDate) <= new Date(licenseIssuedDate)) {
      setError("License expiration date must be after the issued date."); setMessage(""); return;
    }
    await act(async () => {
      try {
        const result = await api<{ message: string }>("/accounts", { method: "POST", body: JSON.stringify({
          email: read("email"), displayName, fullName: displayName, accountType, password,
          droneType: read("droneType"),
          pilotLicenseNo: read("pilotLicenseNo"),
          usagePurpose: read("usagePurpose"),
          phoneNumber: read("phoneNumber"),
          dateOfBirth: read("dateOfBirth"),
          licenseIssuedDate,
          licenseExpiredDate,
          experienceYears: Number(read("experienceYears") || 0),
          description: read("description"),
          isActive: values.get("isActive") === "true",
        }) });
        form.reset(); setAccountType("new"); setShowProvisionForm(false); setMessage(result.message); await load();
      } finally {
        // Passwords are never persisted in component state or retained after a network attempt.
        for (const name of ["password", "confirmPassword"]) {
          const input = form.elements.namedItem(name);
          if (input instanceof HTMLInputElement) input.value = "";
        }
      }
    });
  }
  const pilotAccounts = accounts.filter(item => item.role === "uav_operator");
  const activeOperators = pilotAccounts.filter(item => item.isActive).length;
  const selectedOperator = pilotAccounts[0] ?? accounts[0];
  if (registerOnly) return <div className="portal-content operator-registration-content">
    {error && <p className="auth-error" role="alert">{error} <button className="text-button" disabled={busy} onClick={() => act(load)}>Retry</button></p>}
    {message && <p className="auth-feedback" role="status">{message}</p>}
    <div className="operator-roster-hero"><div><span>FE-01 // UC-03</span><small>PERS-SPEC // REV-8</small><h2>Certified UAV Operator & Commander Roster</h2><p>Manage pilot licenses, civil aviation clearance certificates, duty assignments, and role access controls.</p></div><div className="operator-hero-actions"><button type="button">EXPORT AUDIT CSV</button><button type="button" className="button primary" disabled={busy} aria-haspopup="dialog" onClick={() => setShowProvisionForm(true)}>+ Provision New Operator</button></div></div>
    <section className="operator-stat-grid" aria-label="Operator roster summary"><article><span>Total certified</span><strong>{pilotAccounts.length || accounts.length}<small> personnel</small></strong><p className="green">100% verified identity</p></article><article><span>Active on duty</span><strong>{activeOperators.toString().padStart(2, "0")}<small> online</small></strong><p>{pilotAccounts.length ? Math.round(activeOperators / pilotAccounts.length * 100) : 0}% fleet coverage</p></article><article><span>In-flight (PIC)</span><strong>03<small> pilot in cmd</small></strong><p className="blue">Missions active</p></article><article><span>Standby operators</span><strong>09<small> ready</small></strong><p>NOC station ready</p></article><article><span>Renewal required</span><strong className="warning">02<small> pending</small></strong><p>CAAV cert &lt; 30 days</p></article></section>
    <div className="operator-roster-layout">
    {manager && <section className="portal-card operator-roster-card"><div className="portal-card-heading"><div><span className="auth-kicker">ACTIVE ROSTER</span><h2>Certified Operators</h2><p>Search, review, and provision operators for DroneOps access.</p></div></div>
      {showProvisionForm && <div className="provision-modal-backdrop" role="presentation" onClick={() => setShowProvisionForm(false)}>
        <div className="provision-modal" role="dialog" aria-modal="true" aria-labelledby="pilot-registration-title" onClick={event => event.stopPropagation()}>
          <div className="provision-modal-header">
            <div><span className="auth-kicker">PILOT ONBOARDING</span><h2 id="pilot-registration-title">Register Pilot</h2><p>Capture account, identity, license, and operating details.</p></div>
            <button type="button" className="provision-modal-close" aria-label="Close registration" disabled={busy} onClick={() => setShowProvisionForm(false)}>x</button>
          </div>
          <form onSubmit={provision}>
        <fieldset className="provision-form" disabled={busy} aria-describedby="pilot-form-help">
          <legend>Pilot registration</legend>
          <label>Account setup<select name="accountType" value={accountType} onChange={e => setAccountType(e.target.value)}>
            <option value="new">New email/password account</option><option value="existing">Existing Firebase account</option>
          </select></label>
          <label>Role<input value="UAV Operator / Pilot" readOnly aria-readonly="true" /></label>
          <div className="provision-section"><span>01</span><strong>Pilot identity</strong></div>
          <label>Full name<input name="fullName" required minLength={2} maxLength={100} autoComplete="name" placeholder="Pilot's full name" /></label>
          <label>Email address<input name="email" type="email" required maxLength={254} autoComplete="off" placeholder="pilot@example.com" /></label>
          <label>Phone number<input name="phoneNumber" type="tel" required minLength={8} maxLength={20} autoComplete="tel" placeholder="+84..." /></label>
          <label>Date of birth<input name="dateOfBirth" type="date" required max={new Date().toISOString().slice(0, 10)} /></label>
          {accountType === "new" && <>
            <label>Initial password<input name="password" type="password" required minLength={12} maxLength={128} autoComplete="new-password" aria-describedby="pilot-password-help" /></label>
            <label>Confirm password<input name="confirmPassword" type="password" required minLength={12} maxLength={128} autoComplete="new-password" /></label>
            <p id="pilot-password-help" className="provision-note">Use 12-128 characters. Share the password securely with the Pilot. Email verification is required at first sign-in.</p>
          </>}
          <div className="provision-section"><span>02</span><strong>License & operations</strong></div>
          <label>Drone type<select name="droneType" required defaultValue="">
            <option value="" disabled>Select drone type</option><option value="multirotor">Multirotor</option><option value="fixed-wing">Fixed-wing</option><option value="hybrid-vtol">Hybrid VTOL</option><option value="other">Other</option>
          </select></label>
          <label>Pilot license number<input name="pilotLicenseNo" required minLength={3} maxLength={64} autoComplete="off" placeholder="License number" /></label>
          <label>Usage purpose<select name="usagePurpose" required defaultValue="">
            <option value="" disabled>Select purpose</option><option value="mapping">Mapping / survey</option><option value="inspection">Infrastructure inspection</option><option value="monitoring">Live monitoring</option><option value="training">Training</option><option value="other">Other</option>
          </select></label>
          <label>Experience years<input name="experienceYears" type="number" required min="0" max="80" step="1" placeholder="0" /></label>
          <label>License issued date<input name="licenseIssuedDate" type="date" required /></label>
          <label>License expired date<input name="licenseExpiredDate" type="date" required /></label>
          <label>DroneOps access<select name="isActive" defaultValue="true"><option value="true">Enabled</option><option value="false">Disabled</option></select></label>
          <label className="provision-wide">Description<textarea name="description" maxLength={500} rows={4} placeholder="Operational notes, license scope, or manager remarks" /></label>
          <div className="provision-submit"><button className="button primary" type="submit" disabled={busy}>{busy ? "Please wait..." : accountType === "new" ? "Create Pilot account ->" : "Grant Pilot access ->"}</button></div>
          <p id="pilot-form-help" className="provision-note">{accountType === "existing" ? "Use the email already registered in Firebase. Its password and sign-in methods will stay unchanged. " : ""}Drone model is intentionally collected later when the Pilot creates a UAV record. Disabled access blocks sign-in to DroneOps.</p>
        </fieldset>
          </form>
        </div>
      </div>}<div className="operator-filter-strip"><label><span className="sr-only">Search</span><input placeholder="Search by Pilot name, email, or callsign" /></label><select defaultValue="all"><option value="all">All Roles</option><option value="pilot">UAV Pilot</option><option value="manager">Ops Manager</option></select><select defaultValue="all"><option value="all">All Clearance</option><option value="active">Active</option><option value="disabled">Disabled</option></select></div><div className="account-table-wrap"><table className="account-table"><thead><tr><th>Operator identity</th><th>Role & authority</th><th>Access</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{accounts.map(a => <tr key={a.id}><td><strong>{a.displayName}</strong><br /><span>{a.email}</span></td><td>{roleLabels[a.role]}<br /><span>TAC-clearance L{a.role === "operations_manager" ? "4" : "3"}</span></td><td><span className={a.isActive ? "green" : "warning"}>{a.isActive ? "Online / enabled" : "Access disabled"}</span></td><td>{a.role === "uav_operator" && <button className="text-button" disabled={busy} onClick={() => act(async () => { await api(`/accounts/${a.id}/access`, { method: "PATCH", body: JSON.stringify({ isActive: !a.isActive }) }); await load(); })}>{a.isActive ? "Revoke flight auth" : "Restore access"}</button>}</td></tr>)}</tbody></table></div></section>}
    {selectedOperator && <aside className="operator-detail-panel"><span className="auth-kicker">COMMAND SPECIFICATION</span><h2>Operator Profile & Mission Allocation</h2><div className="operator-detail-grid"><label>Full legal name<input value={selectedOperator.displayName} readOnly /></label><label>Call-sign / handle<input value={selectedOperator.email.split("@")[0].toUpperCase()} readOnly /></label><label>Security clearance<select defaultValue="l3"><option value="l3">Level 3 Tactical</option><option value="l4">Level 4 Tactical Clear</option></select></label><label>Assigned station<select defaultValue="noc"><option value="noc">Station Alpha-01 NOC</option><option value="field">Field Command Node</option></select></label></div><div className="operator-directive"><b>Operational directive BR-01</b><p>Strict role-scoped access control enforced. Operators are isolated to assigned flight records and active command channels.</p></div><div className="operator-detail-actions"><button className="button primary">Update Operator Profile</button><button className="button danger-button" disabled={busy || selectedOperator.role !== "uav_operator"} onClick={() => selectedOperator.role === "uav_operator" && act(async () => { await api(`/accounts/${selectedOperator.id}/access`, { method: "PATCH", body: JSON.stringify({ isActive: !selectedOperator.isActive }) }); await load(); })}>{selectedOperator.isActive ? "Revoke Flight Auth" : "Restore Flight Auth"}</button></div></aside>}
    </div>
  </div>;
  return <div className={registerOnly ? "portal-content operator-registration-content" : "portal-content"}>
    <div className="portal-toolbar"><span className="green">● Authorized workspace</span><button className="button secondary" disabled={busy} onClick={() => logout(false)}>Sign out →</button></div>
    {error && <p className="auth-error" role="alert">{error} <button className="text-button" disabled={busy} onClick={() => act(load)}>Retry</button></p>}
    {message && <p className="auth-feedback" role="status">{message}</p>}
    <section className="portal-card"><div className="portal-card-heading"><div><span className="auth-kicker">ACCOUNT SECURITY</span><h2>Your active sessions</h2><p>Recognize where you are signed in. Revoke any session you no longer use.</p></div><button className="button outline" disabled={busy || !loaded} onClick={() => logout(true)}>Sign out everywhere</button></div>
      {!loaded && <p role="status">Loading your sessions…</p>}
      <ul className="session-list">{sessions.map(s => <li key={s.id}><div><strong>{s.isCurrent ? "This browser" : "Another browser"}</strong><p className="session-agent">{s.userAgent || "Browser information unavailable"}</p><small>Signed in {new Date(s.createdAt).toLocaleString()} · Expires {new Date(s.expiresAt).toLocaleString()}</small></div><button className="button secondary" disabled={busy} onClick={() => act(async () => { await api(`/auth/sessions/${s.id}`, { method: "DELETE" }); if (s.isCurrent) window.location.replace("/login"); else await load(); })}>{s.isCurrent ? "Sign out" : "Revoke"}</button></li>)}</ul>
    </section>
    {manager && <section className="portal-card"><div className="portal-card-heading"><div><span className="auth-kicker">TEAM ACCESS</span><h2>Operators & accounts</h2><p>Create a Pilot account or grant access to an existing Firebase user.</p></div><button type="button" className="button primary" disabled={busy} aria-haspopup="dialog" onClick={() => setShowProvisionForm(true)}>Register Pilot</button></div>
      {showProvisionForm && <div className="provision-modal-backdrop" role="presentation" onClick={() => setShowProvisionForm(false)}>
        <div className="provision-modal" role="dialog" aria-modal="true" aria-labelledby="pilot-registration-title" onClick={event => event.stopPropagation()}>
          <div className="provision-modal-header">
            <div><span className="auth-kicker">PILOT ONBOARDING</span><h2 id="pilot-registration-title">Register Pilot</h2><p>Capture account, identity, license, and operating details.</p></div>
            <button type="button" className="provision-modal-close" aria-label="Close registration" disabled={busy} onClick={() => setShowProvisionForm(false)}>x</button>
          </div>
          <form onSubmit={provision}>
        <fieldset className="provision-form" disabled={busy} aria-describedby="pilot-form-help">
          <legend>Pilot registration</legend>
          <label>Account setup<select name="accountType" value={accountType} onChange={e => setAccountType(e.target.value)}>
            <option value="new">New email/password account</option><option value="existing">Existing Firebase account</option>
          </select></label>
          <label>Role<input value="UAV Operator / Pilot" readOnly aria-readonly="true" /></label>
          <div className="provision-section"><span>01</span><strong>Pilot identity</strong></div>
          <label>Full name<input name="fullName" required minLength={2} maxLength={100} autoComplete="name" placeholder="Pilot's full name" /></label>
          <label>Email address<input name="email" type="email" required maxLength={254} autoComplete="off" placeholder="pilot@example.com" /></label>
          <label>Phone number<input name="phoneNumber" type="tel" required minLength={8} maxLength={20} autoComplete="tel" placeholder="+84..." /></label>
          <label>Date of birth<input name="dateOfBirth" type="date" required max={new Date().toISOString().slice(0, 10)} /></label>
          {accountType === "new" && <>
            <label>Initial password<input name="password" type="password" required minLength={12} maxLength={128} autoComplete="new-password" aria-describedby="pilot-password-help" /></label>
            <label>Confirm password<input name="confirmPassword" type="password" required minLength={12} maxLength={128} autoComplete="new-password" /></label>
            <p id="pilot-password-help" className="provision-note">Use 12–128 characters. Share the password securely with the Pilot. Email verification is required at first sign-in.</p>
          </>}
          <div className="provision-section"><span>02</span><strong>License & operations</strong></div>
          <label>Drone type<select name="droneType" required defaultValue="">
            <option value="" disabled>Select drone type</option><option value="multirotor">Multirotor</option><option value="fixed-wing">Fixed-wing</option><option value="hybrid-vtol">Hybrid VTOL</option><option value="other">Other</option>
          </select></label>
          <label>Pilot license number<input name="pilotLicenseNo" required minLength={3} maxLength={64} autoComplete="off" placeholder="License number" /></label>
          <label>Usage purpose<select name="usagePurpose" required defaultValue="">
            <option value="" disabled>Select purpose</option><option value="mapping">Mapping / survey</option><option value="inspection">Infrastructure inspection</option><option value="monitoring">Live monitoring</option><option value="training">Training</option><option value="other">Other</option>
          </select></label>
          <label>Experience years<input name="experienceYears" type="number" required min="0" max="80" step="1" placeholder="0" /></label>
          <label>License issued date<input name="licenseIssuedDate" type="date" required /></label>
          <label>License expired date<input name="licenseExpiredDate" type="date" required /></label>
          <label>DroneOps access<select name="isActive" defaultValue="true"><option value="true">Enabled</option><option value="false">Disabled</option></select></label>
          <label className="provision-wide">Description<textarea name="description" maxLength={500} rows={4} placeholder="Operational notes, license scope, or manager remarks" /></label>
          <div className="provision-submit"><button className="button primary" type="submit" disabled={busy}>{busy ? "Please wait…" : accountType === "new" ? "Create Pilot account →" : "Grant Pilot access →"}</button></div>
          <p id="pilot-form-help" className="provision-note">{accountType === "existing" ? "Use the email already registered in Firebase. Its password and sign-in methods will stay unchanged. " : ""}Drone model is intentionally collected later when the Pilot creates a UAV record. Disabled access blocks sign-in to DroneOps.</p>
        </fieldset>
          </form>
        </div>
      </div>}<div className="account-table-wrap"><table className="account-table"><thead><tr><th>Team member</th><th>Role</th><th>Access</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{accounts.map(a => <tr key={a.id}><td><strong>{a.displayName}</strong><br /><span>{a.email}</span></td><td>{roleLabels[a.role]}</td><td><span className={a.isActive ? "green" : "warning"}>{a.isActive ? "Enabled" : "Disabled"}</span></td><td>{a.role === "uav_operator" && <button className="text-button" disabled={busy} onClick={() => act(async () => { await api(`/accounts/${a.id}/access`, { method: "PATCH", body: JSON.stringify({ isActive: !a.isActive }) }); await load(); })}>{a.isActive ? "Disable access" : "Enable access"}</button>}</td></tr>)}</tbody></table></div></section>}
  </div>;
}
