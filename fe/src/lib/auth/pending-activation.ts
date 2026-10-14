import "server-only";
import { randomBytes } from "node:crypto";

export const activationCookie = process.env.NODE_ENV === "production" ? "__Host-droneops_activation" : "droneops_activation";
const lifetime = 30 * 60 * 1000;
type Pending = { email: string; password: string; expires: number; attempts: number };
const shared = globalThis as typeof globalThis & { droneopsActivations?: Map<string, Pending> };
const entries = shared.droneopsActivations ??= new Map<string, Pending>();
export function createActivation(email: string, password: string) {
  for (const [key, value] of entries) if (value.expires <= Date.now()) entries.delete(key);
  if (entries.size >= 1000) entries.delete(entries.keys().next().value!);
  const id = randomBytes(32).toString("hex");
  entries.set(id, { email, password, expires: Date.now() + lifetime, attempts: 0 });
  return id;
}
export function getActivation(id: string | undefined) {
  if (!id) return null;
  const value = entries.get(id);
  if (!value || value.expires <= Date.now() || value.attempts >= 5) { entries.delete(id); return null; }
  return value;
}
export function clearActivation(id: string | undefined) { if (id) entries.delete(id); }
