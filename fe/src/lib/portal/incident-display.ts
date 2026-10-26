export function incidentDescription(value: string | null | undefined) {
  return value?.replace(/\s*Weather warnings are advisory\./gi, "").trim();
}
