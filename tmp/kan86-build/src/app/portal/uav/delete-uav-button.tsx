"use client";

import { useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { deleteAssignedUav } from "@/lib/portal/client";

export default function DeleteUavButton({ id, code, onDeleted }: { id: string; code: string; onDeleted?: () => void }) {
  const router = useRouter();
  const pending = useRef(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  async function remove() {
    if (pending.current || !window.confirm(`Delete UAV ${code}? This permanently removes your aircraft record.`)) return;
    pending.current = true; setBusy(true); setError("");
    try {
      await deleteAssignedUav(id);
      if (onDeleted) onDeleted();
      else router.replace("/portal/uav?deleted=1");
      router.refresh();
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to delete this UAV."); }
    finally { pending.current = false; setBusy(false); }
  }
  return <><button className="module-danger" type="button" disabled={busy} aria-label={`Delete UAV ${code}`} onClick={() => void remove()}>{busy ? "DELETING…" : "DELETE UAV"}</button>{error && <p className="form-error" role="alert">{error}</p>}</>;
}
