import type { AssignedUav } from "@/lib/portal/types";

function metric(value: number | null, unit: string, maximumFractionDigits = 2) {
  return value == null
    ? "Not specified"
    : `${new Intl.NumberFormat("en-US", { maximumFractionDigits }).format(value)} ${unit}`;
}

function date(value: string | null) {
  if (!value) return "Not specified";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleDateString();
}

function Field({ label, value }: { label: string; value: string | number }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>;
}

export default function UavInformation({ aircraft }: { aircraft: AssignedUav }) {
  return <section className="uav-information" aria-label="Complete UAV information">
    <header><div><p className="module-kicker">AIRCRAFT SPECIFICATION</p><h2>Complete UAV information</h2></div><span>{aircraft.code}</span></header>
    <div className="uav-info-groups">
      <article><h3>Identity & ownership</h3><dl>
        <Field label="Code" value={aircraft.code} /><Field label="Name" value={aircraft.name} />
        <Field label="Manufacturer" value={aircraft.manufacturer ?? "Not specified"} /><Field label="Model" value={aircraft.model ?? "Not specified"} />
        <Field label="Serial number" value={aircraft.serialNumber ?? "Not specified"} /><Field label="Pilot" value={aircraft.pilotName || "Not specified"} />
        <Field label="Status" value={aircraft.status} />
      </dl></article>
      <article><h3>Physical specifications</h3><dl>
        <Field label="Empty weight" value={metric(aircraft.emptyWeightKg, "kg", 3)} />
        <Field label="Maximum takeoff weight" value={metric(aircraft.maxTakeoffWeightKg, "kg", 3)} />
        <Field label="Maximum payload" value={metric(aircraft.maxPayloadKg, "kg", 3)} />
      </dl></article>
      <article><h3>Battery system</h3><dl>
        <Field label="Battery type" value={aircraft.batteryType ?? "Not specified"} />
        <Field label="Capacity" value={metric(aircraft.batteryCapacityMah, "mAh", 0)} />
        <Field label="Voltage" value={metric(aircraft.batteryVoltage, "V")} />
        <Field label="Cell count" value={aircraft.batteryCellCount ?? "Not specified"} />
      </dl></article>
      <article><h3>Performance limits</h3><dl>
        <Field label="Maximum flight time" value={metric(aircraft.maxFlightTimeMinutes, "min", 0)} />
        <Field label="Maximum range" value={metric(aircraft.maxRangeKm, "km")} />
        <Field label="Maximum speed" value={metric(aircraft.maxSpeedKmh, "km/h")} />
        <Field label="Maximum wind speed" value={metric(aircraft.maxWindSpeedMps, "m/s")} />
        <Field label="Maximum operating altitude" value={metric(aircraft.maxOperatingAltitudeMeters, "m")} />
      </dl></article>
      <article><h3>Operations & lifecycle</h3><dl>
        <Field label="Purchase date" value={date(aircraft.purchaseDate)} />
        <Field label="Last maintenance" value={date(aircraft.lastMaintenanceAt)} />
        <Field label="Next maintenance" value={date(aircraft.nextMaintenanceAt)} />
        <Field label="Total flights" value={aircraft.totalFlights} />
        <Field label="Total flight hours" value={metric(aircraft.totalFlightHours, "h")} />
        <Field label="Created" value={date(aircraft.createdAt)} />
      </dl></article>
    </div>
  </section>;
}
