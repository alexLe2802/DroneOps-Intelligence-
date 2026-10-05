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
  console.info("[auth-debug] portal session cookie", { present: Boolean(cookie), valueLength: cookie?.value.length ?? 0 });
  if (!cookie) return null;
  const request = () => fetch(`${backendUrl()}/api/auth/me`, {
    headers: { Cookie: `${name}=${cookie.value}` }, cache: "no-store", signal: AbortSignal.timeout(20000),
  });
  let response: Response;
  try {
    response = await request();
  } catch (error) {
    // Firebase revocation verification can be slow on its first request while the
    // Admin SDK establishes its upstream connection. Retry this idempotent read once.
    if (!(error instanceof DOMException && error.name === "TimeoutError")) throw error;
    response = await request();
  }
  console.info("[auth-debug] auth/me response", {
    status: response.status,
    state: response.headers.get("x-droneops-auth-state"),
  });
  if (response.status === 401 || response.status === 403) return null;
  if (!response.ok) throw new Error("Authentication service is unavailable.");
  return response.json();
}
