import { api } from "@/lib/auth/client";

export function approvePilotUpdateRequest(id: string) {
  return api(`/users/pilot-update-requests/${encodeURIComponent(id)}/approve`, { method: "PUT" });
}

export function rejectPilotUpdateRequest(id: string, rejectReason: string) {
  return api(`/users/pilot-update-requests/${encodeURIComponent(id)}/reject`, {
    method: "PUT",
    body: JSON.stringify({ rejectReason: rejectReason.trim() }),
  });
}
