import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import ValidationResultsClient from "./validation-results-client";

export const metadata = { title: "Validation Results | DroneOps Intelligence" };

export default async function ValidationResultsPage({ searchParams }: {
  searchParams: Promise<{ missionId?: string | string[]; runId?: string | string[] }>;
}) {
  const [session, query] = await Promise.all([
    requirePortalSession(["uav_operator", "operation_manager"]), searchParams,
  ]);
  return <PortalShell account={session.account}><ValidationResultsClient key={`${query.missionId ?? ""}:${query.runId ?? ""}`}
    role={session.account.role}
    missionId={typeof query.missionId === "string" ? query.missionId : undefined}
    runId={typeof query.runId === "string" ? query.runId : undefined}
  /></PortalShell>;
}
