"use client";

import { useRef, useState, type PointerEvent } from "react";

export default function TelemetryDemo() {
  const [altitude, setAltitude] = useState(173);
  const [thrust, setThrust] = useState(true);
  const drag = useRef<{ y: number; altitude: number } | null>(null);
  const warning = altitude > 200 ? "CEILING APPROACH: 220 M" : altitude < 95 ? "TERRAIN BUFFER ALERT" : "GEOFENCE: CLEAR";
  function move(event: PointerEvent<HTMLDivElement>) {
    if (drag.current) setAltitude(Math.round(Math.max(80, Math.min(220, drag.current.altitude - (event.clientY - drag.current.y) * 0.7))));
  }
  return (
    <section id="demo" className="demo-panel" aria-label="Interactive simulated drone telemetry">
      <div className="demo-header">
        <span><i /> INTERACTIVE FLIGHT PREVIEW</span>
        <button type="button" className="thrust-toggle" aria-pressed={thrust} onClick={() => setThrust(!thrust)}>
          ● THRUST: {thrust ? "ON" : "IDLE"}
        </button>
      </div>
      <div className="hud">
        <div className="hud-readouts">
          <div>
            ALTITUDE: <b>{altitude}.0 m AGL</b><br />
            SPEED: <b>{thrust ? "16.0" : "0.0"} m/s</b><br />
            MODE: <b>SIMULATION</b>
          </div>
          <div>
            BATT: <b>94% (24.8V)</b><br />
            LINK: <b>−68 dBm</b><br />
            GPS: <b>12 SAT</b>
          </div>
        </div>
        <div className="radar"><div /><div /><span /></div>
        <span className="horizon">— 0° HORIZON —</span>
        <div className="altitude-scale"><span>220m</span><span>180m</span><span>140m</span><span>100m</span><span>80m</span></div>
        <div
          className="drone-rig"
          onPointerDown={event => {
            event.currentTarget.setPointerCapture(event.pointerId);
            drag.current = { y: event.clientY, altitude };
          }}
          onPointerMove={move}
          onPointerUp={() => { drag.current = null; }}
          onPointerCancel={() => { drag.current = null; }}
          onLostPointerCapture={() => { drag.current = null; }}
        >
          <svg viewBox="0 0 360 220" role="img" aria-label="Quadcopter model with continuously spinning rotors; drag vertically or use altitude slider below">
            <defs>
              <linearGradient id="body-fill" x2="1" y2="1">
                <stop stopColor="#1e3a5f" />
                <stop offset="1" stopColor="#0d1c2e" />
              </linearGradient>
            </defs>
            <path d="M180 115 65 52 M180 115 295 52 M180 115 85 165 M180 115 275 165" stroke="#334155" strokeWidth="9" strokeLinecap="round" />
            {[[65, 52], [295, 52], [85, 165], [275, 165]].map(([x, y], index) => (
              <g key={index} className="rotor-assembly">
                <circle cx={x} cy={y} r="14" fill="#ffffff" stroke="#1754a1" strokeWidth="2.5" />
                <g className="rotor" style={{ transformOrigin: `${x}px ${y}px` }}>
                  <ellipse cx={x} cy={y} rx="42" ry="5" fill="#1754a1" fillOpacity="0.24" stroke="#1754a1" strokeWidth="1.4" />
                  <path d={`M${x - 42} ${y}h84`} stroke="#003d7e" strokeWidth="1.6" />
                </g>
                <circle cx={x} cy={y} r="4.5" fill="#003d7e" />
                <circle cx={x} cy={y + 18} r="3.2" fill={index < 2 ? "#16a34a" : "#dc2626"} />
                <circle className="rotor-hit-area" cx={x} cy={y} r="44" fill="transparent" />
              </g>
            ))}
            <path d="m130 148-15 30h30m85-30 15 30h-30" fill="none" stroke="#475569" strokeWidth="3.2" />
            <path d="m180 68 48 20 10 36-58 22-58-22 10-36Z" fill="url(#body-fill)" stroke="#1754a1" strokeWidth="2" />
            <path d="m180 77 38 16 8 26-46 18-46-18 8-26Z" fill="#0f2744" stroke="#60a5fa" />
            <path d="M165 94q15-12 30 0-3 20-15 20t-15-20" fill="#1754a1" stroke="#93c5fd" />
            <circle cx="180" cy="100" r="6" fill="#60a5fa" />
            <path d="M166 125h28" stroke="#34d399" strokeWidth="3" />
            <rect x="171" y="145" width="18" height="15" rx="3" fill="#1e3a5f" stroke="#60a5fa" />
            <circle cx="180" cy="152" r="5" fill="#93c5fd" />
            <text x="180" y="86" textAnchor="middle" fill="#e2e8f0" fontSize="5.5" fontWeight="700">DRONEOPS QUAD</text>
          </svg>
        </div>
        <div className="hud-bottom">
          <span>10°46′12.4″N · 106°43′28.1″E</span>
          <strong className={altitude > 200 || altitude < 95 ? "warning" : "green"}>{warning}</strong>
        </div>
      </div>
      <div className="demo-controls">
        <label htmlFor="altitude">ALTITUDE <b>{altitude} m</b></label>
        <input id="altitude" type="range" min="80" max="220" value={altitude} onChange={event => setAltitude(Number(event.target.value))} />
        <button type="button" onClick={() => { setAltitude(173); setThrust(true); }}>Reset</button>
      </div>
      <div className="mission-summary">
        <div>
          <strong><span className="cyan">●</span> Interactive Quadcopter Telemetry Model</strong>
          <p>Drag the aircraft vertically or adjust the altitude slider to preview geofence ceiling and buffer states.</p>
        </div>
        <span className="demo-badge">SIMULATED<br />PREVIEW</span>
      </div>
    </section>
  );
}
