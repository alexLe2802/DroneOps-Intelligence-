"use client";

import Link from "next/link";
import type { AssignedUav } from "@/lib/portal/types";
import DeleteUavButton from "./delete-uav-button";

export default function UavMutationActions({ aircraft, onDeleted, editLabel = "EDIT" }: {
  aircraft: AssignedUav; onDeleted?: () => void; editLabel?: string;
}) {
  const pending = aircraft.hasPendingProposal || ["pending", "pending review"].includes(aircraft.status.toLowerCase());
  const hintId = `uav-review-lock-${aircraft.id}`;
  return <>
    {pending
      ? <button type="button" className="module-secondary" disabled aria-describedby={hintId}>{editLabel}</button>
      : <Link className="module-secondary module-link" href={`/portal/uav/${aircraft.id}/edit`}>{editLabel}</Link>}
    <DeleteUavButton id={aircraft.id} code={aircraft.code} disabled={pending} describedBy={pending ? hintId : undefined} onDeleted={onDeleted} />
    {pending && <p id={hintId} className="uav-review-lock">Awaiting Manager review. Editing and deletion are unavailable until a decision is made.</p>}
  </>;
}
