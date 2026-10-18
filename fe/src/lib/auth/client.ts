"use client";

export class ApiError extends Error {
  constructor(message: string, public status: number) { super(message); }
}
export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = options.method ?? "GET";
  const headers = new Headers(options.headers);
  if (method !== "GET") {
    // The JWT proxy checks the request Origin before forwarding mutations.
    headers.set("Content-Type", "application/json");
  }
  const response = await fetch(`/api${path}`, { ...options, headers, credentials: "same-origin", cache: "no-store" });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const validationMessage = problem?.errors && typeof problem.errors === "object"
      ? Object.values(problem.errors).flat().find((value): value is string => typeof value === "string")
      : undefined;
    throw new ApiError(problem?.message ?? problem?.Message ?? validationMessage ?? (response.status === 429 ? "Too many sign-in attempts. Please wait a minute." : "Request failed. Please try again."), response.status);
  }
  return response.status === 204 ? undefined as T : response.json();
}
