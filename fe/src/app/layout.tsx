import type { Metadata } from "next";
import type { ReactNode } from "react";
import "./globals.css";

export const metadata: Metadata = {
  title: "DroneOps Intelligence | UAV Mission Operations",
  description:
    "Plan UAV missions, validate flight paths, explore MAVLink telemetry, and review AI-assisted operational insights with DroneOps Intelligence.",
  icons: { icon: "/droneops-logo.svg" },
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body className="min-h-screen antialiased">{children}</body>
    </html>
  );
}
