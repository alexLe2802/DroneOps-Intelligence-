import "server-only";
import { cookies } from "next/headers";
import type { AuthSession, AccountRole } from "./types";

export const accessCookie = process.env.NODE_ENV === "production" ? "__Host-droneops_access" : "droneops_access";
export function backendUrl() {
  const url = new URL(process.env.API_BASE_URL ?? "http://localhost:5159");
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.pathname !== "/") throw new Error("API_BASE_URL must be a trusted backend origin.");
  return url.origin;
}
export function canonicalRole(role: string): AccountRole | null {
  if (["uav_operator", "Pilot"].includes(role)) return "uav_operator";
  if (["operation_manager", "operations_manager", "OperationsManager", "Admin", "SystemAdmin"].includes(role)) return "operation_manager";
  return null;
}
export async function sessionFromToken(token: string): Promise<AuthSession | null> {
  // The backend verifies signature/expiry; JWT decoding below only extracts expiry after verification.
  const response = await fetch(`${backendUrl()}/api/users/profile`, { headers: { Authorization: `Bearer ${token}` }, cache: "no-store", signal: AbortSignal.timeout(20000) });
  if (response.status === 401 || response.status === 403 || response.status === 404) return null;
  if (!response.ok) {
    // Log only upstream status and request IDs; never log the access token or response body.
    console.error("Authentication profile request failed", {
      endpoint: "/api/users/profile",
      status: response.status,
      traceId: response.headers.get("traceparent") ?? response.headers.get("x-request-id"),
    });
    throw new Error(`Authentication service is unavailable (profile API HTTP ${response.status}).`);
  }
  const profile = await response.json();
  const role = canonicalRole(profile.role);
  if (!role) return null;
  try {
    const claims = JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString());
    if (!Number.isFinite(claims.exp) || claims.exp * 1000 <= Date.now()) return null;
    return { account: { id: profile.id, email: profile.email, displayName: profile.fullName, role, isActive: true }, expiresAt: new Date(claims.exp * 1000).toISOString() };
  } catch { return null; }
}
export async function readSession(): Promise<AuthSession | null> {
  const token = (await cookies()).get(accessCookie)?.value;
  return token ? sessionFromToken(token) : null;
}
