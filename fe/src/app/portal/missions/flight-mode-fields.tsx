"use client";

export type FlightMode = "manual" | "automatic";

export default function FlightModeFields({ value, onChange }: { value: FlightMode | null; onChange: (mode: FlightMode) => void }) {
  return <section className="mission-fields mission-flight-mode" aria-labelledby="mission-flight-mode-title">
    <h2 id="mission-flight-mode-title">Choose flight mode · required</h2>
    <p>Select how this mission will be flown before configuring its route.</p>
    <fieldset className="mission-mode-options">
      <legend>Flight control</legend>
      <label className={value === "manual" ? "selected" : ""}>
        <input type="radio" name="flight-mode" value="manual" checked={value === "manual"} required onChange={() => onChange("manual")} />
        <span><strong>Manual operation</strong><small>You fly using your RC controller or ground station. DroneOps plans and monitors the mission; it does not start an automatic flight.</small></span>
      </label>
      <label className={value === "automatic" ? "selected" : ""}>
        <input type="radio" name="flight-mode" value="automatic" checked={value === "automatic"} required onChange={() => onChange("automatic")} />
        <span><strong>Automatic mission</strong><small>Set bounded speeds, altitude and waypoint actions. Review and confirm in Live Monitoring, then explicitly ARM from the scheduled start. The 10-second countdown begins after FC confirms ARM.</small></span>
      </label>
    </fieldset>
    {value === "automatic" && <p className="form-warning">Plan → validate → manager approval → connect the assigned drone → verify FC data and mission upload → confirm → pilot ARM → 10-second countdown. Launch remains unavailable until the FC safety profile is verified.</p>}
    {value === null && <p role="status">Choose Manual operation or Automatic mission. No flight mode is selected yet.</p>}
  </section>;
}
