"use client";

import Link from "next/link";
import type { OperationalAnalytics } from "@/lib/portal/types";
import { archiveHref, analyticsQuery } from "@/lib/portal/analytics-scope";
import styles from "./analytics.module.css";

export default function AnalyticsDetails({ data, scope }: { data: OperationalAnalytics; scope: string }) {
  const c = data.missingCoverage;
  function dayLink(day: string) {
    const params = new URLSearchParams(analyticsQuery(new URLSearchParams(scope))); params.set("fromDate", day); params.set("toDate", day);
    return archiveHref(params);
  }
  return <section className={styles.panel}>
    <h2>Operational analytics</h2>
    <p>Completion = {data.completedFlights} completed / {data.totalFlights} started flights.
      {data.successRatePercentage == null ? " Unavailable: no started flights in this window." : ` ${data.successRatePercentage.toFixed(1)}%.`}
      {" "}A non-completed flight may still be active; this is not a safety score.</p>
    <p>Window: {data.fromDate || "All recorded starts"} — {data.toDate || "Latest recorded start"} · {data.contract.windowBasis} · {data.contract.windowTimeZone}.
      Date filters use UTC calendar days; flight timestamps elsewhere use GMT+7.</p>
    <p>Duration: {data.reportsAvailable ? `${data.totalFlightHours.toFixed(2)} ${data.contract.units.totalFlightHours}` : "Unavailable — no available reports"}.
      {" "}Recorded distance: {c.validDistanceSegments ? `${data.totalDistanceKilometers.toFixed(2)} ${data.contract.units.totalDistanceKilometers}` : "Unavailable — no valid GPS segments"}.</p>
    <div className={styles.tableWrap} role="region" aria-label="Completion trend" tabIndex={0}>
      <table><caption>Daily completion trend (UTC start date)</caption><thead><tr><th scope="col">Date</th><th scope="col">Started</th><th scope="col">Completed</th><th scope="col">Other / ongoing</th><th scope="col">Completion</th><th scope="col">Source</th></tr></thead>
        <tbody>{data.completionTrend.map(row => <tr key={row.date}><th scope="row">{row.date}</th><td>{row.totalFlights}</td><td>{row.completedFlights}</td><td>{row.failedOrAbortedFlights}</td><td>{row.totalFlights ? `${(row.completedFlights / row.totalFlights * 100).toFixed(1)}%` : "Unavailable"}</td><td><Link href={dayLink(row.date)}>Flights on {row.date}</Link></td></tr>)}</tbody></table>
      {!data.completionTrend.length && <p>No started flights in this window.</p>}
    </div>
    <div className={styles.breakdowns}>{[["Incident type", data.incidentTypeBreakdown], ["Incident severity", data.incidentSeverityBreakdown], ["Excluded reports", data.excludedReportReasons]].map(([title, values]) => <div key={String(title)}><h3>{String(title)}</h3><dl>{Object.entries(values).map(([key, count]) => <div key={key}><dt>{key}</dt><dd>{count}</dd></div>)}</dl>{!Object.keys(values).length && <p>No recorded entries in this scope.</p>}</div>)}</div>
    <h3>Coverage and exclusions</h3>
    <p>{data.reportsAvailable} available reports / {data.totalFlights} started flights; {data.reportsExcluded} excluded. Reports that are queued, failed, outdated or absent are not included in duration totals.</p>
    <dl className={styles.coverage}>{Object.entries({ "Valid GPS segments": c.validDistanceSegments, "Excluded GPS segments": c.excludedDistanceSegments, "Excluded gaps (seconds)": c.excludedGapSeconds, "Missing GPS samples": c.missingGpsSamples, "Invalid telemetry samples": c.invalidTelemetrySamples, "Flights without valid GPS coverage": c.missionsWithoutValidGpsCoverage, "Flights without available reports": c.missionsWithoutAvailableReport }).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{value}</dd></div>)}</dl>
    <p>Distance sums consecutive valid GPS samples. Gaps longer than {data.contract.communicationLossThresholdSeconds} seconds are excluded. Incident counts use distinct incident IDs.</p>
    <Link href={archiveHref(new URLSearchParams(analyticsQuery(new URLSearchParams(scope))), "/portal/flight-history/sources")}>Inspect source flights, telemetry and incidents</Link>
    <p className={styles.sources}>Sources: authorized flight records, recorded telemetry and distinct incident records. Each source flight links to its own evidence.</p>
  </section>;
}
