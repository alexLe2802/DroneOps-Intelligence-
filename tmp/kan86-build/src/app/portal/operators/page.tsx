import { requirePortalSession } from "@/lib/portal/server";
import LicenseChanges from "../profile/license-changes";
import PilotRoster from "./pilot-roster";
import PortalShell from "../portal-shell";

export const metadata = { title: "Operators | DroneOps Intelligence" };

export default async function OperatorsPage() {
  const session = await requirePortalSession(["operation_manager"]);
  return <PortalShell account={session.account}>
    <PilotRoster />
    <LicenseChanges manager />
  </PortalShell>;
}
