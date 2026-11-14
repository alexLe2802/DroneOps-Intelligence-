import { requirePortalSession } from "@/lib/portal/server";
import LicenseChanges from "../profile/license-changes";
import PilotRoster from "./pilot-roster";
import PortalShell from "../portal-shell";

export const metadata = { title: "Operators | DroneOps Intelligence" };

type OperatorsPageProps = {
  searchParams: Promise<{ requestId?: string | string[]; registrationId?: string | string[] }>;
};

const firstValue = (value: string | string[] | undefined) => Array.isArray(value) ? value[0] : value;

export default async function OperatorsPage({ searchParams }: OperatorsPageProps) {
  const session = await requirePortalSession(["operation_manager"]);
  const query = await searchParams;
  const requestId = firstValue(query.requestId);
  const registrationId = firstValue(query.registrationId);
  return <PortalShell account={session.account}>
    <PilotRoster key={registrationId ? `registration-${registrationId}` : "operator-roster"} initialRegistrationId={registrationId} />
    <LicenseChanges manager initialRequestId={requestId} />
  </PortalShell>;
}
