"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api } from "@/lib/auth/client";
import { getMissionHistory } from "@/lib/portal/client";
import type { MissionUpdateSummary } from "@/lib/portal/types";
import { formatVietnamDate } from "@/lib/portal/vietnam-time";
import styles from "./pilot-roster.module.css";

type Allocation = { id: string; name: string; createdBy: string; uavId: string | null; uavCode?: string | null; status: string; startTime: string | null; endTime: string | null };
type AllocationPage = { items: Allocation[]; totalCount: number; totalPages: number };

function DecisionHistory({ missionId }: { missionId: string }) {
  const [history, setHistory] = useState<MissionUpdateSummary[] | null>(null);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    getMissionHistory(missionId).then(value => { if (active) setHistory(value); }).catch(reason => { if (active) setError(reason instanceof Error ? reason.message : "History unavailable."); });
    return () => { active = false; };
  }, [missionId, revision]);
  if (error) return <div role="alert"><p>{error}</p><button type="button" onClick={() => { setError(""); setRevision(value => value + 1); }}>Retry history</button></div>;
  if (!history) return <p>Loading review history…</p>;
  return history.length ? <ol>{history.map(entry => <li key={entry.id}>
    <strong>{entry.isInitial ? "Initial submission" : "Plan update"} · {entry.status}</strong>
    <p>Submitted {formatVietnamDate(entry.createdAt)} · Actor {entry.actorId}</p>
    <p>Reviewer: {entry.reviewedBy || "Not recorded"} · Decision: {entry.reviewedAt ? formatVietnamDate(entry.reviewedAt) : "Not recorded"}</p>
    <p>Reason: {entry.reason || "Not recorded"}</p>
    <p>Proposed UAV: {entry.proposedPlan.uavId || "Not recorded"}{entry.basePlan && <> · Previous UAV: {entry.basePlan.uavId || "Not recorded"}</>}</p>
  </li>)}</ol> : <p>No recorded review history. This does not establish that an allocation was approved.</p>;
}

export default function OperatorAllocations({ userId }: { userId: string }) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<AllocationPage | null>(null);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [opened, setOpened] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    const params = new URLSearchParams({ createdBy: userId, pageNumber: String(page), pageSize: "10" });
    api<AllocationPage>(`/missions?${params}`).then(value => {
      if (!active) return;
      // Fail closed if a mismatched response is returned; never attribute another operator's missions to this profile.
      if (value.items.some(item => item.createdBy !== userId)) throw new Error("Mission scope mismatch. Reload after the backend update is available.");
      setData(value);
    }).catch(reason => { if (active) setError(reason instanceof Error ? reason.message : "Allocations unavailable."); });
    return () => { active = false; };
  }, [page, revision, userId]);
  function changePage(next: number) { setData(null); setError(""); setOpened(null); setPage(next); }
  return <section className={styles.allocations}>
    <h3>Mission allocations & review history</h3>
    <p>Mission responsibility belongs to this Operator. UAV ownership is unchanged. Drafts and pending requests are not approved allocations.</p>
    {error ? <div role="alert"><p>{error}</p><button type="button" onClick={() => { setError(""); setRevision(value => value + 1); }}>Retry allocations</button></div> : !data ? <p>Loading mission allocations…</p> : <>
      {!data.items.length ? <p>No recorded missions for this Operator.</p> : <div className={styles.allocationTable} tabIndex={0} role="region" aria-label="Operator mission allocations"><table>
        <caption>Recorded missions for the selected Operator</caption>
        <thead><tr><th scope="col">Mission / UAV</th><th scope="col">Status / planned time (GMT+7)</th><th scope="col">Evidence</th></tr></thead>
        <tbody>{data.items.map(mission => <tr key={mission.id}>
          <th scope="row"><Link href={`/portal/missions/${encodeURIComponent(mission.id)}`}>{mission.name}</Link><p>{mission.uavCode || mission.uavId || "UAV not recorded"}</p></th>
          <td>{mission.status}<p>{mission.startTime ? formatVietnamDate(mission.startTime) : "Start not recorded"}<br />{mission.endTime ? formatVietnamDate(mission.endTime) : "End not recorded"}</p></td>
          <td><button type="button" aria-expanded={opened === mission.id} onClick={() => setOpened(value => value === mission.id ? null : mission.id)}>Review history</button></td>
        </tr>)}</tbody>
      </table></div>}
      <div className={styles.pagination}><span>Page {page} / {Math.max(1, data.totalPages)} · {data.totalCount} missions</span><div><button type="button" disabled={page <= 1} onClick={() => changePage(page - 1)}>Previous</button><button type="button" disabled={page >= data.totalPages} onClick={() => changePage(page + 1)}>Next</button></div></div>
      {opened && <DecisionHistory key={opened} missionId={opened} />}
    </>}
  </section>;
}
