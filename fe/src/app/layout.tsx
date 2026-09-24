import { headers } from "next/headers";
import type { Metadata } from "next";
import type { ReactNode } from "react";
import "./globals.css";

export const metadata: Metadata = {
  title: "DroneOps Intelligence | UAV Mission Operations",
  description:
    "Plan UAV missions, validate flight paths, explore MAVLink telemetry, and review AI-assisted operational insights with DroneOps Intelligence.",
  icons: { icon: "/droneops-logo.svg" },
};

export default async function RootLayout({ children }: { children: ReactNode }) {
  // Request rendering is required for per-request CSP nonces.
  await headers();
  return (
    <html lang="en">
      <body className="min-h-screen antialiased">{children}</body>
    </html>
  );
}
