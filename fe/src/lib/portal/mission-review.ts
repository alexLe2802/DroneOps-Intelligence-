import type { AccountRole } from "@/lib/auth/types";
import type { MissionDetail, MissionUpdateSummary, MissionValidationResult } from "./types";

export function validationState(result?: MissionValidationResult | null) {
  if (!result) return "Unavailable";
  if (result.stale || result.status === "Stale") return "Stale";
  if (result.error || result.status === "Error" || result.outcome === "Error") return "Error";
  if (result.incomplete || result.status === "Incomplete" || result.outcome === "Incomplete") return "Incomplete";
  if (!result.isValid || result.status === "Fail" || result.outcome === "Fail" || result.issues?.length) return "Fail";
  return "Pass";
}

export function approvalBlockReason(result: MissionValidationResult | null | undefined, missionId: string, requestId?: string) {
  const state = validationState(result);
  if (state === "Stale") return "Validation is outdated. Revalidate the submitted proposal before approving.";
  if (state === "Error") return "Validation could not finish because of an error. Revalidate before approving.";
  if (state === "Incomplete") return "Validation is incomplete. Resolve missing evidence and revalidate before approving.";
  if (state === "Fail") return "Mandatory checks failed. Resolve the blocking findings and revalidate before approving.";
  if (state !== "Pass") return "No validation evidence is available. Revalidate before approving.";
  if (!result?.id || result.missionId !== missionId || !requestId || result.updateRequestId !== requestId) {
    return "Validation evidence does not identify this review request. Refresh and revalidate the selected proposal.";
  }
  return null;
}

export function missionActions(mission: MissionDetail, role: AccountRole, actorId?: string) {
  const status = mission.status.toLowerCase().replaceAll(" ", "");
  const authorized = role === "operation_manager" || (!!actorId && mission.createdBy === actorId);
  const preflight = ["draft", "rejected", "pending", "pendingreview", "submitted", "approved", "pendingconfirm"].includes(status);
  return {
    edit: authorized && preflight,
    submit: authorized && role === "uav_operator" && ["draft", "rejected"].includes(status) && !mission.pendingUpdate,
    validate: authorized && preflight && status !== "pendingconfirm",
    cancel: authorized && preflight,
    start: false,
    review: authorized && role === "operation_manager" && !!mission.pendingUpdate && preflight,
  };
}

export function reviewHistory(history: MissionUpdateSummary[]) {
  return [...history].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt) || a.id.localeCompare(b.id));
}
