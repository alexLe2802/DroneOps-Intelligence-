"use client";

import { ApiError } from "@/lib/auth/client";

const statusCopy: Record<number, [string, string]> = {
  401: ["Session expired", "Sign in again to continue."],
  403: ["Access denied", "Your role does not have access to this workspace."],
  404: ["Not found", "The requested data is not available."],
  500: ["Service error", "The server could not complete this request."],
};

export function ModuleLoading({ label = "Loading workspace" }: { label?: string }) {
  return <section className="module-state" role="status"><span className="state-spinner" /><p className="module-kicker">SYNC IN PROGRESS</p><h2>{label}…</h2><p>Retrieving the latest operational data.</p></section>;
}

export function ModuleEmpty({ title, message }: { title: string; message: string }) {
  return <section className="module-state"><span className="state-symbol">◇</span><p className="module-kicker">NO RECORDS</p><h2>{title}</h2><p>{message}</p></section>;
}

export function ModuleError({ error, retry }: { error: unknown; retry: () => void }) {
  const status = error instanceof ApiError ? error.status : 500;
  const [title, fallback] = statusCopy[status] ?? statusCopy[500];
  return <section className="module-state error" role="alert"><span className="state-symbol">!</span><p className="module-kicker">HTTP {status}</p><h2>{title}</h2><p>{error instanceof Error ? error.message : fallback}</p><button className="module-primary" onClick={retry}>TRY AGAIN</button></section>;
}
