// Keep only supported scope fields when sharing a view or returning from a report.
export const scopeKeys = ["fromDate", "toDate", "uavId", "missionId", "status", "search", "page"] as const;
export function archiveQuery(source: URLSearchParams): string {
  const clean = new URLSearchParams();
  for (const key of scopeKeys) { const value = source.get(key)?.trim(); if (value) clean.set(key, value); }
  return clean.toString();
}
export function archiveHref(source: URLSearchParams, path = "/portal/flight-history"): string {
  const query = archiveQuery(source);
  return `${path}${query ? `?${query}` : ""}`;
}
export function analyticsQuery(source: URLSearchParams): string {
  const clean = new URLSearchParams();
  for(const key of ["fromDate","toDate","uavId","missionId"]) { const value=source.get(key); if(value) clean.set(key,value); }
  return clean.toString();
}
