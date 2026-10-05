import "server-only";
import { redirect } from "next/navigation";
import { readSession } from "@/lib/auth/server";
import type { AccountRole, AuthSession } from "@/lib/auth/types";

export async function requirePortalSession(roles?: AccountRole[]): Promise<AuthSession> {
  const session = await readSession();
  if (!session) redirect("/login");
  if (roles && !roles.includes(session.account.role)) redirect("/portal");
  return session;
}
