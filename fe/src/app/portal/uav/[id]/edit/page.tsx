import { requirePortalSession } from "@/lib/portal/server";
import { redirect } from "next/navigation";

export const metadata = { title: "Update UAV | DroneOps Intelligence" };
export default async function EditUavPage({ params }: { params: Promise<{ id: string }> }) {
  const [, { id }] = await Promise.all([requirePortalSession(["uav_operator"]), params]);
  redirect(`/portal/uav/${id}`);
}
