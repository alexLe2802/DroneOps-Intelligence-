"use client";

export class ApiError extends Error {
  constructor(message: string, public status: number) { super(message); }
}
export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = options.method ?? "GET";
  const headers = new Headers(options.headers);
  if (method !== "GET") {
    // Request a fresh anti-forgery token after each change of authenticated identity.
    const csrfResponse = await fetch("/api/auth/csrf", { cache: "no-store", credentials: "same-origin" });
    if (!csrfResponse.ok) throw new ApiError("Unable to secure your request. Please try again.", csrfResponse.status);
    const csrf: { csrfToken: string } = await csrfResponse.json();
    headers.set("X-CSRF-TOKEN", csrf.csrfToken);
    headers.set("Content-Type", "application/json");
  }
  const response = await fetch(`/api${path}`, { ...options, headers, credentials: "same-origin", cache: "no-store" });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(problem?.message ?? (response.status === 429 ? "Too many sign-in attempts. Please wait a minute." : "Request failed. Please try again."), response.status);
  }
  return response.status === 204 ? undefined as T : response.json();
}
