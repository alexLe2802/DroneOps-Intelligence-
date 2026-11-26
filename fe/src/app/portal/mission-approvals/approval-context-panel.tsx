"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { getManagedUav } from "@/lib/portal/client";
import type { ApprovalUavSummary, PendingMissionApproval } from "@/lib/portal/types";
import { apiInstant } from "@/lib/portal/vietnam-time";
import styles from "./approval-context.module.css";

function formatDate(value: string | null | undefined): string {
  if (!value) return "Not recorded";
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "Asia/Ho_Chi_Minh",
  }).format(apiInstant(value));
}

function formatDay(value: string | null | undefined): string {
  if (!value) return "Not recorded";
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    timeZone: "Asia/Ho_Chi_Minh",
  }).format(apiInstant(value));
}

function metric(value: number | null | undefined, unit: string): string {
  if (value == null || !Number.isFinite(value)) return "Not recorded";
  return `${value} ${unit}`;
}

export default function ApprovalContextPanel({ selected }: { selected: PendingMissionApproval }) {
  const [loadedUav, setLoadedUav] = useState<{ uavId: string; uav: ApprovalUavSummary | null } | null>(null);

  const effectiveUavId = selected.proposedPlan?.uavId || selected.uavId;

  useEffect(() => {
    if (selected.assignedUav || !effectiveUavId) return;
    let active = true;
    void getManagedUav(effectiveUavId)
      .then(record => {
        if (!active) return;
        setLoadedUav({
          uavId: effectiveUavId,
          uav: {
            id: record.id,
            code: record.code,
            name: record.name,
            status: record.status,
            hasPendingProposal: record.hasPendingProposal,
            manufacturer: record.manufacturer,
            model: record.model,
            serialNumber: record.serialNumber,
            emptyWeightKg: record.emptyWeightKg,
            maxTakeoffWeightKg: record.maxTakeoffWeightKg,
            maxPayloadKg: record.maxPayloadKg,
            batteryType: record.batteryType,
            batteryCapacityMah: record.batteryCapacityMah,
            batteryVoltage: record.batteryVoltage,
            batteryCellCount: record.batteryCellCount,
            maxFlightTimeMinutes: record.maxFlightTimeMinutes,
            maxRangeKm: record.maxRangeKm,
            maxSpeedKmh: record.maxSpeedKmh,
            maxWindSpeedMps: record.maxWindSpeedMps,
            maxOperatingAltitudeMeters: record.maxOperatingAltitudeMeters,
            lastMaintenanceAt: record.lastMaintenanceAt,
            nextMaintenanceAt: record.nextMaintenanceAt,
            totalFlightHours: record.totalFlightHours,
            totalFlights: record.totalFlights,
          },
        });
      })
      .catch(() => {
        if (active) setLoadedUav({ uavId: effectiveUavId, uav: null });
      });
    return () => {
      active = false;
    };
  }, [effectiveUavId, selected.assignedUav]);

  const loadingUav = !selected.assignedUav && Boolean(effectiveUavId) && loadedUav?.uavId !== effectiveUavId;
  const uav = selected.assignedUav ?? (loadedUav?.uavId === effectiveUavId ? loadedUav.uav : null);
  const record = selected.pilotRecord ?? null;

  const uavStatusTone = !uav
    ? ""
    : uav.status === "Active"
      ? styles.badgeGood
      : uav.status === "Pending"
        ? styles.badgeWarn
        : styles.badgeDanger;

  const batterySummary = !uav
    ? "Not recorded"
    : [
        uav.batteryType,
        uav.batteryCapacityMah != null ? `${uav.batteryCapacityMah} mAh` : null,
        uav.batteryVoltage != null ? `${uav.batteryVoltage} V` : null,
        uav.batteryCellCount != null ? `${uav.batteryCellCount}S` : null,
      ]
        .filter(Boolean)
        .join(" · ") || "Not recorded";

  const weightSummary = !uav
    ? "Not recorded"
    : [
        uav.emptyWeightKg != null ? `Empty ${uav.emptyWeightKg} kg` : null,
        uav.maxTakeoffWeightKg != null ? `MTOW ${uav.maxTakeoffWeightKg} kg` : null,
        uav.maxPayloadKg != null ? `Payload ${uav.maxPayloadKg} kg` : null,
      ]
        .filter(Boolean)
        .join(" · ") || "Not recorded";

  return (
    <section className={styles.grid} aria-label="Manager review context">
      <article className={styles.card}>
        <header className={styles.header}>
          <div>
            <span className={styles.kicker}>PILOT TRACK RECORD</span>
            <h2>{selected.pilotName || "Unknown Pilot"}</h2>
            <p className={styles.subtitle}>{selected.pilotEmail || "Email unavailable"}</p>
          </div>
          {record ? (
            <span
              className={`${styles.badge} ${
                record.openIncidentsCount > 0
                  ? styles.badgeDanger
                  : record.totalIncidentsCount > 0
                    ? styles.badgeWarn
                    : styles.badgeGood
              }`}
            >
              {record.openIncidentsCount > 0
                ? `${record.openIncidentsCount} OPEN INCIDENT${record.openIncidentsCount === 1 ? "" : "S"}`
                : record.totalIncidentsCount > 0
                  ? `${record.totalIncidentsCount} PAST INCIDENT${record.totalIncidentsCount === 1 ? "" : "S"}`
                  : "CLEAN RECORD"}
            </span>
          ) : (
            <span className={styles.badge}>UNAVAILABLE</span>
          )}
        </header>

        <div className={styles.body}>
          {record ? (
            <>
              <div className={styles.kpiRow}>
                <div className={`${styles.kpi} ${record.completedMissionsCount > 0 ? styles.kpiGood : ""}`}>
                  <span>Completed Missions</span>
                  <strong>{record.completedMissionsCount}</strong>
                </div>
                <div className={`${styles.kpi} ${record.totalIncidentsCount > 0 ? styles.kpiWarn : ""}`}>
                  <span>Total Incidents</span>
                  <strong>{record.totalIncidentsCount}</strong>
                </div>
                <div className={`${styles.kpi} ${record.openIncidentsCount > 0 ? styles.kpiDanger : ""}`}>
                  <span>Open Incidents</span>
                  <strong>{record.openIncidentsCount}</strong>
                </div>
              </div>

              {record.recentCompletedMissions.length > 0 ? (
                <div className={styles.tableWrap}>
                  <table className={styles.table}>
                    <thead>
                      <tr>
                        <th>Completed Mission</th>
                        <th>UAV</th>
                        <th>Incidents</th>
                        <th>Debrief</th>
                      </tr>
                    </thead>
                    <tbody>
                      {record.recentCompletedMissions.map(item => (
                        <tr key={item.missionId}>
                          <td>
                            <span className={styles.missionName}>{item.missionName}</span>
                            <span className={styles.metaText}>{formatDate(item.endTime ?? item.startTime)}</span>
                          </td>
                          <td>
                            <span className={styles.metaText}>{item.uavCode || "—"}</span>
                          </td>
                          <td>
                            <span
                              className={`${styles.incidentPill} ${
                                item.incidentCount > 0 ? styles.incidentAlert : styles.incidentNone
                              }`}
                            >
                              {item.incidentCount}
                            </span>
                          </td>
                          <td>
                            <Link
                              className={styles.link}
                              href={`/portal/flight-history/${encodeURIComponent(item.missionId)}`}
                            >
                              View report →
                            </Link>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <div className={styles.empty}>
                  No completed missions recorded for this pilot yet.
                </div>
              )}
            </>
          ) : (
            <div className={styles.empty}>
              Pilot track record is unavailable from the current backend response. Refresh after deploying the updated backend service.
            </div>
          )}
        </div>

        <footer className={styles.footer}>
          <span>Showing up to 10 most recent completed missions</span>
          <Link className={styles.link} href="/portal/incidents">
            All incidents →
          </Link>
        </footer>
      </article>

      <article className={styles.card}>
        <header className={styles.header}>
          <div>
            <span className={styles.kicker}>ASSIGNED AIRCRAFT SPECIFICATIONS</span>
            <h2>{uav ? `${uav.code} · ${uav.name}` : selected.uavCode || "Unassigned UAV"}</h2>
            <p className={styles.subtitle}>
              {uav
                ? [uav.manufacturer, uav.model].filter(Boolean).join(" · ") || "Manufacturer / model not recorded"
                : selected.uavName || "No aircraft record attached"}
            </p>
          </div>
          <span className={`${styles.badge} ${uavStatusTone}`}>
            {loadingUav ? "LOADING..." : uav?.status ?? "UNAVAILABLE"}
          </span>
        </header>

        <div className={styles.body}>
          {uav ? (
            <dl className={styles.specGrid}>
              <div>
                <dt>Serial Number</dt>
                <dd>{uav.serialNumber || "Not recorded"}</dd>
              </div>
              <div>
                <dt>Flight Totals</dt>
                <dd>
                  {uav.totalFlights} flight{uav.totalFlights === 1 ? "" : "s"} · {uav.totalFlightHours} h
                </dd>
              </div>
              <div>
                <dt>Battery</dt>
                <dd>{batterySummary}</dd>
              </div>
              <div>
                <dt>Max Flight Time</dt>
                <dd>{metric(uav.maxFlightTimeMinutes, "min")}</dd>
              </div>
              <div>
                <dt>Max Wind Resistance</dt>
                <dd>{metric(uav.maxWindSpeedMps, "m/s")}</dd>
              </div>
              <div>
                <dt>Max Operating Altitude</dt>
                <dd>{metric(uav.maxOperatingAltitudeMeters, "m")}</dd>
              </div>
              <div>
                <dt>Max Speed / Range</dt>
                <dd>
                  {uav.maxSpeedKmh != null ? `${uav.maxSpeedKmh} km/h` : "—"} ·{" "}
                  {uav.maxRangeKm != null ? `${uav.maxRangeKm} km` : "—"}
                </dd>
              </div>
              <div>
                <dt>Weight &amp; Payload</dt>
                <dd>{weightSummary}</dd>
              </div>
              <div>
                <dt>Last Maintenance</dt>
                <dd>{formatDay(uav.lastMaintenanceAt)}</dd>
              </div>
              <div>
                <dt>Next Maintenance</dt>
                <dd>{formatDay(uav.nextMaintenanceAt)}</dd>
              </div>
            </dl>
          ) : (
            <div className={styles.empty}>
              {loadingUav
                ? "Loading aircraft specifications…"
                : "Detailed UAV specifications are unavailable for this mission."}
            </div>
          )}
        </div>

        <footer className={styles.footer}>
          <span>
            {uav?.hasPendingProposal
              ? "This UAV has a pending specification update proposal."
              : "Approved aircraft registry specifications"}
          </span>
          <Link className={styles.link} href="/portal/uav-management">
            UAV Registry →
          </Link>
        </footer>
      </article>
    </section>
  );
}
