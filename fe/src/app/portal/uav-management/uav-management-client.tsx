"use client";

import { useCallback, useEffect, useState, useRef } from "react";
import { getManagedUav, getManagedUavs, setManagedUavStatus } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleEmpty, ModuleLoading } from "../module-state";

export default function UavManagementClient() {
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [selected, setSelected] = useState<AssignedUav | null>(null);
  const requestId = useRef(0);
  const mutationPending = useRef(false);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [totalItems, setTotalItems] = useState(0);
  const [status, setStatus] = useState("");
  const [appliedKeyword, setAppliedKeyword] = useState("");
  const [keyword, setKeyword] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  const load = useCallback(async (search = appliedKeyword, requestedPage = page, requestedStatus = status) => {
    const currentRequest = ++requestId.current;
    setError(null); setBusy(true);
    try {
      const result = await getManagedUavs({ keyword: search, pageNumber: requestedPage, status: requestedStatus });
      if (currentRequest !== requestId.current) return;
      setAircraft(result.items);
      setTotalPages(result.totalPages); setTotalItems(result.totalItems);
      setSelected(null);

    } catch (reason) { if (currentRequest === requestId.current) setError(reason); }
    finally { if (currentRequest === requestId.current) setBusy(false); }
  }, [appliedKeyword, page, status]);

  useEffect(() => {
    const requests = requestId;
    const timer = window.setTimeout(() => void load(), 0);
    return () => { window.clearTimeout(timer); requests.current++; };
  }, [load]);

  async function inspect(id: string) {
    setBusy(true); setError(null); setMessage("");
    try { setSelected(await getManagedUav(id)); }
    catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }

  async function changeStatus(action: "activate" | "reject" | "deactivate") {
    if (!selected || mutationPending.current) return;
    if (action !== "activate" && !window.confirm(`${action === "deactivate" ? "Deactivate" : "Reject"} UAV ${selected.code}?`)) return;
    mutationPending.current = true;
    setBusy(true); setError(null); setMessage("");
    try {
      const updated = await setManagedUavStatus(selected.id, action);
      setMessage(`${updated.code} is now ${updated.status}.`);
      setSelected(null);
      if (page > 1) setPage(1);
      else await load();
    } catch (reason) { setError(reason); }
    finally { mutationPending.current = false; setBusy(false); }
  }

  if (!aircraft && !error) return <ModuleLoading label="Loading UAV fleet" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">OPERATIONS MANAGER / UAV REVIEW</p><h1>UAV Management</h1><p>Browse the UAV fleet, review registrations, and manage aircraft status.</p></div></header>
    {error && <div className="form-error" role="alert"><p>{error instanceof Error ? error.message : "Unable to load UAVs. Please try again."}</p><button className="module-secondary" disabled={busy} onClick={() => void load()}>TRY AGAIN</button></div>}
    {message && <p className="approval-banner" role="status">{message}</p>}
    <>
      <form className="module-toolbar" onSubmit={event => { event.preventDefault(); if (page === 1 && appliedKeyword === keyword.trim()) void load(); else { setPage(1); setAppliedKeyword(keyword.trim()); } }}><label>SEARCH UAV<input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="Code, name, model or pilot" /></label><label>STATUS<select value={status} disabled={busy} onChange={event => { setStatus(event.target.value); setPage(1); }}><option value="">All statuses</option>{["Pending", "Active", "Inactive", "Rejected"].map(value => <option key={value} value={value}>{value}</option>)}</select></label><button className="module-primary" disabled={busy}>SEARCH</button></form>
      <div className="manager-uav-layout">
        <section className="manager-uav-queue">
          {busy ? <ModuleLoading label="Loading UAV fleet" /> : error && !aircraft ? <p>Fleet data is unavailable. Please retry.</p> : !aircraft?.length ? <ModuleEmpty title="No UAV found" message="No aircraft matches your search and status filter." /> : aircraft.map(uav => <button type="button" className={`manager-uav-row${selected?.id === uav.id ? " selected" : ""}`} key={uav.id} onClick={() => void inspect(uav.id)} disabled={busy}><span><b>{uav.code}</b><small>{uav.name} · {uav.model ?? "Model not specified"}</small></span><span><b>{uav.pilotName || "Unknown pilot"}</b><small>{new Date(uav.createdAt).toLocaleDateString()}</small></span><em>{uav.status}</em></button>)}
          <div className="uav-actions" aria-label="Fleet pagination"><button className="module-secondary" disabled={busy || page <= 1} onClick={() => setPage(value => value - 1)}>PREVIOUS</button><span role="status">Page {page} / {Math.max(1, totalPages)} · {totalItems} UAVs</span><button className="module-secondary" disabled={busy || page >= totalPages} onClick={() => setPage(value => value + 1)}>NEXT</button></div>
        </section>
        <aside className="manager-uav-detail">
          {!selected ? <div className="manager-uav-placeholder"><span>◈</span><h2>Select a UAV</h2><p>Select an aircraft from the list to view its details.</p></div> : <><header><div><p className="module-kicker">UAV RECORD</p><h2>{selected.code}</h2></div><span className={`status status-${selected.status.toLowerCase()}`}>{selected.status}</span></header><dl><div><dt>Name</dt><dd>{selected.name}</dd></div><div><dt>Model</dt><dd>{selected.model ?? "Not specified"}</dd></div><div><dt>Pilot</dt><dd>{selected.pilotName}</dd></div><div><dt>Created</dt><dd>{new Date(selected.createdAt).toLocaleString()}</dd></div></dl><div className="uav-actions"><button className="module-primary" disabled={busy || !["pending", "inactive", "rejected"].includes(selected.status.toLowerCase())} onClick={() => void changeStatus("activate")}>{selected.status.toLowerCase() === "inactive" ? "REACTIVATE" : "APPROVE & ACTIVATE"}</button><button className="module-danger" disabled={busy || selected.status.toLowerCase() !== "pending"} onClick={() => void changeStatus("reject")}>REJECT</button>{selected.status.toLowerCase() === "active" && <button className="module-danger" disabled={busy} onClick={() => void changeStatus("deactivate")}>DEACTIVATE</button>}</div></>}
        </aside>
      </div>

    </>
  </>;
}
