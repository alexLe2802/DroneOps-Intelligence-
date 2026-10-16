"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/auth/client";
import styles from "./pilot-roster.module.css";
import PilotRegistrationRequests from "./pilot-registration-requests";

type Pilot = { id: string; fullName: string; email: string; role: string; createdAt: string };
type UserPage = { items: Pilot[]; totalPages: number };

export default function PilotRoster() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [view, setView] = useState("roster");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [pilots, setPilots] = useState<Pilot[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let alive = true;
    const timer = window.setTimeout(async () => {
      setLoading(true); setError(""); setPilots([]);
      try {
        // Backend role matching is exact; include both Pilot and uav_operator.
        const users: Pilot[] = [];
        let currentPage = 1;
        let totalPages = 1;
        do {
          const params = new URLSearchParams({ PageNumber: String(currentPage), PageSize: "100", Search: query.trim() });
          const data = await api<UserPage>(`/users?${params}`);
          if (!alive) return;
          users.push(...data.items.filter(user => ["pilot", "uav_operator"].includes(user.role.toLowerCase())));
          totalPages = data.totalPages;
          currentPage++;
        } while (currentPage <= totalPages);
        setPilots(users);
      } catch (reason) {
        if (alive) setError(reason instanceof Error ? reason.message : "Unable to load pilots.");
      } finally { if (alive) setLoading(false); }
    }, 250);
    return () => { alive = false; window.clearTimeout(timer); };
  }, [query, revision]);

  const totalPages = Math.max(1, Math.ceil(pilots.length / 20));
  const currentPage = Math.min(page, totalPages);
  const items = pilots.slice((currentPage - 1) * 20, currentPage * 20);
  const selected = pilots.find(pilot => pilot.id === selectedId) ?? items[0];
  const unavailable = loading || Boolean(error);
  const initials = (name: string) => name.trim().split(/\s+/).map(part => part[0]).slice(0, 2).join("").toUpperCase();
  const date = (value: string) => new Date(value).toLocaleDateString("en-GB");
  return <div className={styles.page}>
    <header className={styles.hero}>
      <div><div className={styles.eyebrow}><span>PERSONNEL REGISTRY</span> OPERATIONS / PILOTS</div><h1>Certified UAV Operator Roster</h1><p>Review pilot accounts, flight credentials, and operator records.</p></div>
      <button className={styles.refresh} type="button" disabled={loading} onClick={() => setRevision(value => value + 1)}>↻ Refresh roster</button>
    </header>
    <section className={styles.stats} aria-label="Personnel overview">
      {[ ["REGISTERED PILOTS", unavailable ? "—" : String(pilots.length).padStart(2, "0"), query ? "Matching your search" : "Personnel in directory"], ["ACTIVE ON DUTY", "—", "Duty data unavailable"], ["IN FLIGHT", "—", "Mission data unavailable"], ["STANDBY OPERATORS", "—", "Availability not reported"], ["LICENSE RENEWAL", "—", "License data unavailable"] ].map(([label, value, note], index) => <article key={label}><div>{label}<span aria-hidden="true">{["▣", "●", "↗", "◷", "◇"][index]}</span></div><strong>{value}</strong><small>{note}</small></article>)}
    </section>
    <div className={styles.tabs} role="tablist" aria-label="Operator management views">
      <button type="button" role="tab" aria-selected={view === "roster"} onClick={() => setView("roster")}>Operator Roster</button>
      <button type="button" role="tab" aria-selected={view === "requests"} onClick={() => setView("requests")}>Registration Requests</button>
    </div>
    {view === "requests" ? <PilotRegistrationRequests /> : <div className={styles.layout}>
      <section className={styles.roster} aria-busy={loading} aria-label="Pilot directory">
        <div className={styles.filters}><label><span aria-hidden="true">⌕</span><span className="sr-only">Search pilots</span><input value={query} onChange={event => { setQuery(event.target.value); setPage(1); setLoading(true); }} placeholder="Search pilot by name or email" /></label><span className={styles.roleBadge}>ALL PILOTS</span></div>
        <div className={styles.tableMeta}><span>ROSTER MATCHES: <b>{unavailable ? "—" : pilots.length} OPERATORS</b></span><span>PERSONNEL DIRECTORY</span></div>
        {error && <div className={styles.empty} role="alert"><h3>Unable to load pilots</h3><p>{error}</p><button type="button" className={styles.refresh} onClick={() => setRevision(value => value + 1)}>Try again</button></div>}
        {loading ? <div className={styles.empty} role="status">Loading personnel records…</div> : !error && <>
          <div className={styles.tableWrap}><table><thead><tr><th>Operator identity</th><th>Role & authority</th><th>Registered</th></tr></thead><tbody>{items.map(pilot => <tr key={pilot.id} className={selected?.id === pilot.id ? styles.selected : ""}>
            <td><button type="button" className={styles.identity} aria-pressed={selected?.id === pilot.id} onClick={() => setSelectedId(pilot.id)}><span className={styles.avatar}>{initials(pilot.fullName)}</span><span><strong>{pilot.fullName}</strong><small>{pilot.email}</small></span></button></td>
            <td><span className={styles.pilotBadge}>↗ UAV PILOT</span><small className={styles.subtext}>OPERATOR ACCOUNT</small></td><td className={styles.date}>{date(pilot.createdAt)}</td>
          </tr>)}</tbody></table></div>
          {items.length === 0 && <div className={styles.empty}><h3>No pilots found</h3><p>{query ? "Try a different name or email address." : "Pilot accounts will appear here once registered."}</p></div>}
          <footer className={styles.pagination}><span>PAGE {currentPage} OF {totalPages} · {pilots.length} PERSONNEL</span><div><button type="button" disabled={currentPage <= 1} onClick={() => setPage(currentPage - 1)}>Prev</button><b>{currentPage}</b><button type="button" disabled={currentPage >= totalPages} onClick={() => setPage(currentPage + 1)}>Next</button></div></footer>
        </>}
      </section>
      <aside className={styles.detail} aria-label="Selected operator profile">
        <div className={styles.detailHeading}><div className={styles.eyebrow}>OPERATOR SPECIFICATION <span aria-hidden="true">●</span></div><h2>Operator Profile & Credentials</h2><p>{selected && !unavailable ? selected.fullName : "Select a pilot to review their record"}</p></div>
        {selected && !unavailable ? <>
          <div className={styles.profile}><span className={styles.largeAvatar}>{initials(selected.fullName)}</span><div><h3>{selected.fullName}</h3><span className={styles.pilotBadge}>UAV OPERATOR / PILOT</span></div></div>
          <dl className={styles.fields}><div><dt>Full legal name</dt><dd>{selected.fullName}</dd></div><div><dt>Registered on</dt><dd>{date(selected.createdAt)}</dd></div><div className={styles.wide}><dt>Email address</dt><dd>{selected.email}</dd></div><div className={styles.wide}><dt>Operator ID</dt><dd className={styles.identifier}>{selected.id}</dd></div><div><dt>License number</dt><dd className={styles.muted}>Not available</dd></div><div><dt>License expiry</dt><dd className={styles.muted}>Not available</dd></div></dl>
        </> : <div className={styles.empty}>{loading ? "Loading operator profile…" : "No operator selected"}</div>}
        <div className={styles.directive}><span aria-hidden="true">◇</span><div><b>FLIGHT CREDENTIALS</b><p>License details and flight clearance status are not available yet.</p></div></div>
        <div className={styles.licenseAction}><button type="button" disabled className={styles.revoke} aria-describedby="revoke-license-note"><span aria-hidden="true">⊘</span> Revoke License</button><p id="revoke-license-note">License revocation will be available soon.</p></div>
      </aside>
    </div>}
  </div>;
}
