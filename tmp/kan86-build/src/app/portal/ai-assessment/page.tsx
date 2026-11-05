import { requirePortalSession } from "@/lib/portal/server";
import PortalShell from "../portal-shell";
import AiAssessmentClient from "./ai-assessment-client";

export const metadata = { title: "AI Risk Assessment | DroneOps Intelligence" };

export default async function AiAssessmentPage() {
  const session = await requirePortalSession();
  return <PortalShell account={session.account}><AiAssessmentClient /></PortalShell>;
}
