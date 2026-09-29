import "server-only";
import { cookies } from "next/headers";
import type { AuthSession } from "./types";

export function backendUrl() {
  const url = new URL(process.env.API_BASE_URL ?? "http://localhost:5159");
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.pathname !== "/")
    throw new Error("API_BASE_URL must be a trusted backend origin.");
  return url.origin;
}

export async function readSession(): Promise<AuthSession | null> {
  const jar = await cookies();
  const name = process.env.NODE_ENV === "production" ? "__Host-droneops_session" : "droneops_session";
  const cookie = jar.get(name);
  if (!cookie) return null;
  const response = await fetch(`${backendUrl()}/api/auth/me`, {
    headers: { Cookie: `${name}=${cookie.value}` }, cache: "no-store", signal: AbortSignal.timeout(15000),
  });
  if (response.status === 401 || response.status === 403) return null;
  if (!response.ok) throw new Error("Authentication service is unavailable.");
  return response.json();
}
