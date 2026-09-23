"use client";

import { useRef, useState, type PointerEvent } from "react";

export default function TelemetryDemo() {
  const [altitude, setAltitude] = useState(173);
  const [thrust, setThrust] = useState(true);
  const drag = useRef<{ y: number; altitude: number } | null>(null);
  const warning = altitude > 200 ? "CEILING APPROACH: 220 M" : altitude < 95 ? "TERRAIN BUFFER ALERT" : "GEO-RESTRICTION: SECURED";
  function move(event: PointerEvent<HTMLDivElement>) {
    if (drag.current) setAltitude(Math.round(Math.max(80, Math.min(220, drag.current.altitude - (event.clientY - drag.current.y) * .7))));
  }
  return <section id="demo" className="demo-panel" aria-label="Interactive simulated drone telemetry">
    <div className="demo-header"><span><i /> FEED: SIM_AER_X4_HUD</span><button type="button" className="thrust-toggle" aria-pressed={thrust} onClick={() => setThrust(!thrust)}>● THRUST: {thrust ? "ON" : "IDLE"}</button></div>
    <div className="hud">
      <div className="hud-readouts"><div>ALTITUDE: <b>{altitude}.0 m AGL</b><br />SPEED: <b>{thrust ? "16.0" : "0.0"} m/s</b><br />MODE: <b>SIMULATION</b></div><div>BATT: <b>94% (24.8V)</b><br />LINK: <b>−68 dBm</b><br />GPS: <b>12 SAT</b></div></div>
      <div className="radar"><div /><div /><span /></div>
      <span className="horizon">— 0° HORIZON —</span><div className="altitude-scale"><span>220m</span><span>180m</span><span>140m</span><span>100m</span><span>80m</span></div>
      <div className="drone-rig" style={{ transform: `translateY(${(173 - altitude) * .7}px)` }} onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); drag.current = { y: event.clientY, altitude }; }} onPointerMove={move} onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }} onLostPointerCapture={() => { drag.current = null; }}>
        <svg viewBox="0 0 360 220" role="img" aria-label="Quadcopter model; hover over each rotor to spin it, drag vertically or use altitude slider below">
          <defs><linearGradient id="body-fill" x2="1" y2="1"><stop stopColor="#243c50" /><stop offset="1" stopColor="#070d17" /></linearGradient></defs>
          <path d="M180 115 65 52 M180 115 295 52 M180 115 85 165 M180 115 275 165" stroke="#304357" strokeWidth="9" strokeLinecap="round" />
          {[[65,52],[295,52],[85,165],[275,165]].map(([x,y],index) => <g key={index} className="rotor-assembly"><circle cx={x} cy={y} r="14" fill="#0b2034" stroke="#38bdf8" strokeWidth="2" /><g className="rotor" style={{ transformOrigin: `${x}px ${y}px` }}><ellipse cx={x} cy={y} rx="42" ry="5" fill="#0ea5e9" fillOpacity=".45" stroke="#7dd3fc" /><path d={`M${x-42} ${y}h84`} stroke="#b9eeff" /></g><circle cx={x} cy={y} r="4" fill="#73e5ff" /><circle cx={x} cy={y+18} r="3" fill={index < 2 ? "#10b981" : "#fb7185"} /><circle className="rotor-hit-area" cx={x} cy={y} r="44" fill="transparent" /></g>)}
          <path d="m130 148-15 30h30m85-30 15 30h-30" fill="none" stroke="#52647c" strokeWidth="3" />
          <path d="m180 68 48 20 10 36-58 22-58-22 10-36Z" fill="url(#body-fill)" stroke="#38bdf8" strokeWidth="2" /><path d="m180 77 38 16 8 26-46 18-46-18 8-26Z" fill="#0a1421" stroke="#1685ae" /><path d="M165 94q15-12 30 0-3 20-15 20t-15-20" fill="#123c58" stroke="#38bdf8" /><circle cx="180" cy="100" r="6" fill="#43d5f5" /><path d="M166 125h28" stroke="#10b981" strokeWidth="3" /><rect x="171" y="145" width="18" height="15" rx="3" fill="#102a40" stroke="#38bdf8" /><circle cx="180" cy="152" r="5" fill="#7dd3fc" /><text x="180" y="86" textAnchor="middle" fill="#b3cad8" fontSize="5">AER-X4 VALKYRIE</text>
        </svg>
      </div>
      <div className="hud-bottom"><span>10°46′12.4″N · 106°43′28.1″E</span><strong className={altitude > 200 || altitude < 95 ? "warning" : "green"}>{warning}</strong></div>
    </div>
    <div className="demo-controls"><label htmlFor="altitude">SIM ALTITUDE <b>{altitude} m</b></label><input id="altitude" type="range" min="80" max="220" value={altitude} onChange={event => setAltitude(Number(event.target.value))} /><button type="button" onClick={() => { setAltitude(173); setThrust(true); }}>Reset ↺</button></div>
    <div className="mission-summary"><div><strong><span className="cyan">●</span> AER-X4 Valkyrie (ID: UAV-09)</strong><p>Mission: Port Perimeter Patrol · Sector 4</p></div><span className="demo-badge">SIMULATED<br />DEMO DATA</span></div>
    <p className="demo-hint">Hover over a rotor to spin it. Drag the drone or adjust altitude to explore the HUD.</p>
  </section>;
}
