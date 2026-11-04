"use client";

import { useEffect } from "react";
import { api } from "@/lib/auth/client";

// Presence remains portal-wide. All user decisions live in Live Monitoring.
export default function FlightPreparationPresence() {
  useEffect(() => {
    let active = true, pending = false;
    async function poll() {
      if (pending || !active || document.visibilityState !== "visible" || !navigator.onLine) return;
      pending = true;
      try {
        const due = await api<{ missions: { id: string }[] }>("/missions/automation/due");
        for (const mission of due.missions) {
          if (!active || document.visibilityState !== "visible") break;
          await api(`/missions/${mission.id}/automation/presence`, { method: "POST" });
        }
      } catch { /* Backend presence expires on failure; no consent is inferred. */ }
      finally { pending = false; }
    }
    const refresh = () => { void poll(); };
    void poll();
    const timer = window.setInterval(refresh, 10_000);
    document.addEventListener("visibilitychange", refresh);
    window.addEventListener("online", refresh);
    return () => { active = false; clearInterval(timer); document.removeEventListener("visibilitychange", refresh); window.removeEventListener("online", refresh); };
  }, []);
  return null;
}
