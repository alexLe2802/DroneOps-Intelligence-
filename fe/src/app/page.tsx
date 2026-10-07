import Image from "next/image";
import { readSession } from "@/lib/auth/server";
import LandingAccount from "./landing-account";
import TelemetryDemo from "./telemetry-demo";

const features = [
  { id: "geofence", icon: "◎", title: "Geofence & Flight Path Validation", description: "Check mission waypoints and flight paths against configured geofences, no-fly zones, and altitude limits before submitting a mission for approval.", detail: "Pre-flight validation", tag: "Configured boundaries" },
  { id: "telemetry", icon: "ϟ", title: "Real-Time MAVLink Telemetry", description: "Bring position, altitude, speed, battery, and connectivity into one operational view. Follow mission progress and surface abnormal conditions for operator review.", detail: "Mission monitoring", tag: "MAVLink integration" },
  { id: "ai-risk", icon: "✧", title: "AI Mission Decision Support", description: "Summarize mission context, identify potential risks, and review suggested precautions with AI assistance. Operators stay in control of every mission decision.", detail: "Human-led decisions", tag: "AI-assisted review" },
];

export default async function HomePage() {
  const session = await readSession().catch(() => null);
  return (
    <>
      <a className="skip-link" href="#overview">Skip to content</a>
      <header className="site-header">
        <div className="shell header-inner">
          <a href="#overview" className="brand" aria-label="DroneOps Intelligence home">
            <Image src="/droneops-logo.svg" alt="" width={40} height={40} priority />
            <div><div className="brand-name">DroneOps <span>INTELLIGENCE</span></div><small>MISSION CONTROL</small></div>
          </a>
          <nav className="desktop-nav" aria-label="Main navigation">
            <a href="#overview">Platform<br />overview</a><a href="#telemetry">MAVLink<br />telemetry</a><a href="#geofence">Geofence<br />engine</a><a href="#ai-risk">AI risk<br />assist</a>
          </nav>
          <LandingAccount initialSession={session} />
        </div>
      </header>
      <main>
        <section id="overview" className="hero">
          <div className="shell hero-grid">
            <div className="hero-copy">
              <h1>Next-Gen Autonomous<br /><span>UAV Mission Operations</span><br />&amp; Live Telemetry</h1>
              <p className="hero-description">Mission planning, geofence validation, real-time MAVLink telemetry, and AI-assisted risk advisory. Your entire mission lifecycle, connected.</p>
              <div className="metrics"><div><strong>PLAN</strong><small>Validate flight paths</small></div><div><strong>MONITOR</strong><small>Track mission status</small></div><div><strong>ANALYZE</strong><small>Learn from every flight</small></div></div>
              <div className="hero-actions"><a className="button primary" href="/portal"><span aria-hidden="true">→</span> Launch mission portal</a><a className="button secondary" href="#architecture"><span aria-hidden="true">▥</span> Explore architecture</a></div>
              <p className="hero-note"><span /> Built for UAV operators, mission managers, and operational teams.</p>
            </div>
            <TelemetryDemo />
          </div>
        </section>
        <section id="architecture" className="capabilities shell">
          <div className="section-heading"><span className="eyebrow">OPERATIONAL CAPABILITIES</span><h2>One platform.<br className="mobile-only" /> Every stage of your mission.</h2><p>From the first waypoint to the final flight review, keep your team<br className="desktop-only" /> connected to the information that matters.</p></div>
          <div className="feature-grid">{features.map(feature => <article className="feature-card" id={feature.id} key={feature.id}><div className="feature-icon" aria-hidden="true">{feature.icon}</div><h3>{feature.title}</h3><p className="feature-description">{feature.description}</p><div className="feature-bottom"><span>{feature.detail}</span><strong>{feature.tag}</strong></div></article>)}</div>
          <aside className="clearance" id="compliance"><div className="clearance-icon" aria-hidden="true">▣</div><div><h3>MISSION TRACEABILITY, FROM PLAN TO REVIEW</h3><p>Connect mission versions, approvals, incidents, and post-flight history in one workflow.</p></div><a className="button outline" href="#architecture">Explore capabilities →</a></aside>
        </section>
      </main>
      <footer className="site-footer"><div className="shell"><div className="footer-grid"><div><a href="#overview" className="footer-brand"><Image src="/droneops-logo.svg" alt="" width={24} height={24} />DroneOps Intelligence</a><p>Mission planning, operational monitoring, and insights for connected UAV teams.</p></div><div><h3>SYSTEM PROTOCOL</h3><p>MAVLink telemetry<br />Mission & waypoint management<br />Geofence validation<br />Flight history & incidents</p></div><div><h3>OPERATIONAL WORKFLOW</h3><p>Plan & validate<br />Review & approve<br />Monitor & respond<br />Analyze & improve</p></div></div><div className="footer-bottom"><p>© 2026 DroneOps Intelligence. <a href="#overview">Back to top ↑</a></p></div></div></footer>
    </>
  );
}
