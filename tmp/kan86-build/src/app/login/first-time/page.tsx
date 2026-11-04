import { cookies } from "next/headers";
import { activationCookie, getActivation } from "@/lib/auth/pending-activation";
import Image from "next/image";
import Link from "next/link";
import { redirect } from "next/navigation";
import { readSession } from "@/lib/auth/server";
import FirstTimeForm from "./first-time-form";
import "../../auth.css";

export const metadata = { title: "Activate Operator Account | DroneOps Intelligence" };

export default async function FirstTimePage() {
  if (await readSession().catch(() => null)) redirect("/portal");
  if (!getActivation((await cookies()).get(activationCookie)?.value)) redirect("/login");
  return <main className="auth-page">
    <header className="auth-header"><Link href="/" className="brand"><Image src="/droneops-logo.svg" width={40} height={40} alt="" /><div className="brand-name">DroneOps <span>INTELLIGENCE</span></div></Link><Link href="/login" className="auth-back">Back to sign in</Link></header>
    <div className="auth-layout"><section className="auth-story"><span className="eyebrow">UAV OPERATOR / ACCOUNT ACTIVATION</span><h1>Approved.<br /><span>Ready to<br />sign in.</span></h1><p>Enter the six-digit code from your approval email to finish signing in.</p></section><section className="login-card" aria-labelledby="activation-title"><span className="auth-kicker">FIRST-TIME ACCESS</span><h2 id="activation-title">Activate your Operator account</h2><p>For registration requests approved by an Operations Manager.</p><FirstTimeForm /></section></div>
  </main>;
}
