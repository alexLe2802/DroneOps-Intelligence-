"use client";

import { useCallback, useEffect, useState } from "react";
import { getManagedUav, getPendingUavs, setManagedUavStatus } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleEmpty, ModuleLoading } from "../module-state";

export default function UavManagementClient() {
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [selected, setSelected] = useState<AssignedUav | null>(null);
  const [keyword, setKeyword] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  const load = useCallback(async (search = "") => {
    setError(null); setBusy(true);
    try {
      const result = await getPendingUavs(search);
      setAircraft(result);

    } catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }, []);

  useEffect(() => { const timer = window.setTimeout(() => void load(), 0); return () => window.clearTimeout(timer); }, [load]);

  async function inspect(id: string) {
    setBusy(true); setError(null); setMessage("");
    try { setSelected(await getManagedUav(id)); }
    catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }

  async function changeStatus(action: "activate" | "reject" | "deactivate") {
    if (!selected) return;
    setBusy(true); setError(null); setMessage("");
    try {
      const updated = await setManagedUavStatus(selected.id, action);
      setMessage(`${updated.code} is now ${updated.status}.`);
      setSelected(null);
      await load(keyword);
    } catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }

  if (!aircraft && !error) return <ModuleLoading label="Loading UAV fleet" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">OPERATIONS MANAGER / UAV REVIEW</p><h1>UAV Management</h1><p>Review pending UAV registrations. Approved aircraft require a fleet-list API that is not available yet.</p></div></header>
    {error && <div className="form-error" role="alert"><p>{error instanceof Error ? error.message : "Unable to load UAVs. Please try again."}</p><button className="module-secondary" disabled={busy} onClick={() => void load(keyword)}>TRY AGAIN</button></div>}
    {message && <p className="approval-banner" role="status">{message}</p>}
    <>
      <form className="module-toolbar" onSubmit={event => { event.preventDefault(); void load(keyword); }}><label>SEARCH UAV<input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="Code, name, model or pilot" /></label><button className="module-primary" disabled={busy}>SEARCH</button></form>
      <div className="manager-uav-layout">
        <section className="manager-uav-queue">
          {!aircraft?.length ? <ModuleEmpty title="No UAV found" message="No aircraft matches your search and status filter." /> : aircraft.map(uav => <button type="button" className={`manager-uav-row${selected?.id === uav.id ? " selected" : ""}`} key={uav.id} onClick={() => void inspect(uav.id)} disabled={busy}><span><b>{uav.code}</b><small>{uav.name} · {uav.model ?? "Model not specified"}</small></span><span><b>{uav.pilotName || "Unknown pilot"}</b><small>{new Date(uav.createdAt).toLocaleDateString()}</small></span><em>{uav.status}</em></button>)}
        </section>
        <aside className="manager-uav-detail">
          {!selected ? <div className="manager-uav-placeholder"><span>◈</span><h2>Select a UAV</h2><p>Select an aircraft from the list to view its details.</p></div> : <><header><div><p className="module-kicker">UAV RECORD</p><h2>{selected.code}</h2></div><span className={`status status-${selected.status.toLowerCase()}`}>{selected.status}</span></header><dl><div><dt>Name</dt><dd>{selected.name}</dd></div><div><dt>Model</dt><dd>{selected.model ?? "Not specified"}</dd></div><div><dt>Pilot</dt><dd>{selected.pilotName}</dd></div><div><dt>Created</dt><dd>{new Date(selected.createdAt).toLocaleString()}</dd></div></dl><div className="uav-actions"><button className="module-primary" disabled={busy || selected.status === "Active"} onClick={() => void changeStatus("activate")}>APPROVE & ACTIVATE</button><button className="module-danger" disabled={busy || selected.status !== "Pending"} onClick={() => void changeStatus("reject")}>REJECT</button>{selected.status.toLowerCase() === "active" && <button className="module-danger" disabled={busy} onClick={() => void changeStatus("deactivate")}>DEACTIVATE</button>}</div></>}
        </aside>
      </div>
      <p className="api-pending-banner"><b>Update approval contract pending:</b> the backend can approve or reject a Pending UAV, but it has no Pilot update endpoint and does not return old versus proposed values yet. This page will automatically include update requests once that backend contract is added.</p>
    </>
  </>;
}
