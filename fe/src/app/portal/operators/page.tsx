import { requirePortalSession } from "@/lib/portal/server";
import PortalControls from "../portal-controls";
import PortalShell from "../portal-shell";

export const metadata = { title: "Operator Registration | DroneOps Intelligence" };

export default async function OperatorsPage() {
  const session = await requirePortalSession(["operations_manager"]);
  return <PortalShell account={session.account}>
    <PortalControls account={session.account} registerOnly />
  </PortalShell>;
}
