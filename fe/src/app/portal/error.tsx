"use client";

import { useEffect } from "react";

export default function PortalError({ error }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => {
    console.error("Portal route failed", error);
  }, [error]);

  return <main className="shell" style={{ paddingBlock: 80 }}>
    <h1>Workspace unavailable</h1>
    <p>The workspace could not finish loading. Reload it or sign in again.</p>
    <div style={{ display: "flex", flexWrap: "wrap", gap: 12, marginTop: 24 }}>
      <button className="button primary" type="button" onClick={() => window.location.reload()}>Reload workspace</button>
      <a className="button secondary" href="/login">Sign in again</a>
    </div>
  </main>;
}
