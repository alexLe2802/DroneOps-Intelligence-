import Image from "next/image";
import Link from "next/link";
import { redirect } from "next/navigation";
import { readSession } from "@/lib/auth/server";
import LoginForm from "./login-form";
import "../auth.css";

export const metadata = { title: "Sign in | DroneOps Intelligence" };

export default async function LoginPage() {
  // An unavailable backend should not prevent the login page from offering a retry.
  const session = await readSession().catch(() => null);
  if (session) redirect("/portal");

  return <main className="auth-page">
    <header className="auth-header"><Link href="/" className="brand"><Image src="/droneops-logo.svg" width={40} height={40} alt="" /><div className="brand-name">DroneOps <span>INTELLIGENCE</span></div></Link><Link href="/" className="auth-back">Back to overview</Link></header>
    <div className="auth-layout">
      <section className="auth-story"><span className="eyebrow">MISSION CONTROL / SECURE ACCESS</span><h1>Your mission.<br /><span>One connected<br />workspace.</span></h1><p>Plan with clarity. Monitor every flight. Make informed decisions with your team.</p><div className="auth-orbit" aria-hidden="true"><div /><div /><Image src="/droneops-logo.svg" width={100} height={100} alt="" /><span>DRONEOPS · INTELLIGENCE</span></div><div className="auth-story-foot"><span>01 / PLAN</span><span>02 / MONITOR</span><span>03 / ANALYZE</span></div></section>
      <section className="login-card" aria-labelledby="login-title"><span className="auth-kicker">SECURE OPERATOR ACCESS</span><h2 id="login-title">Sign In to Mission Control</h2><p>Enter your assigned credentials to access fleet telemetry, airspace permissions, and mission planning.</p><LoginForm /></section>
    </div>
    <footer className="auth-footer"><span>© 2026 DroneOps Intelligence</span><span>Mission operations, connected.</span></footer>
  </main>;
}
