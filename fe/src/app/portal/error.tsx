"use client";
export default function PortalError({ reset }: { reset: () => void }) {
  return <main className="shell" style={{ paddingBlock: 80 }}><h1>Workspace unavailable</h1><p>We could not verify your session. Please try again.</p><button className="button primary" onClick={reset}>Try again</button></main>;
}
