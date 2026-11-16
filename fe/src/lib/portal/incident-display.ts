export function incidentDescription(value: string | null | undefined) {
  return incidentDescriptionFindings(value).join(" ");
}

/** Presentation only: retain the first waypoint for each identical weather finding. */
export function incidentDescriptionFindings(value: string | null | undefined): string[] {
  const text = value?.replace(/\s*Weather warnings are advisory\./gi, "").trim();
  if (!text) return [];
  const seen = new Set<string>();
  return text.split(/(?=\bWP\d+:\s*)/).map(part => part.trim()).filter(part => {
    const match = /^WP\d+:\s*([\s\S]+)$/.exec(part);
    // Do not merge non-weather operational checks or different measured values.
    if (!match || !/\b(forecast|weather|wind|gust|rain|cloud|visibility|precipitation|snow|temperature|humidity)\b/i.test(match[1])) return true;
    const key = match[1].replace(/\s+/g, " ").replace(/[.!]+$/, "").toLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
