import { NextRequest, NextResponse } from "next/server";
import { backendUrl } from "@/lib/auth/server";
import { networkInterfaces } from "node:os";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export function GET(request: NextRequest) {
  let browserHost = request.nextUrl.hostname;
  if (["localhost", "127.0.0.1", "0.0.0.0", "::1"].includes(browserHost)) {
    const addresses = Object.values(networkInterfaces()).flat().filter(
      address => address?.family === "IPv4" && !address.internal,
    );
    const privateAddress = addresses.find(address => /^192\.168\./.test(address!.address))
      ?? addresses.find(address => /^10\./.test(address!.address))
      ?? addresses.find(address => /^172\.(1[6-9]|2[0-9]|3[01])\./.test(address!.address));
    browserHost = privateAddress?.address ?? addresses[0]?.address ?? browserHost;
  }
  const configured = new URL(backendUrl());
  if (["localhost", "127.0.0.1", "0.0.0.0", "::1"].includes(configured.hostname)) {
    configured.hostname = browserHost;
  }
  const download = new URL(request.nextUrl.origin);
  download.hostname = browserHost;
  return NextResponse.json(
    { backendOrigin: configured.origin, downloadOrigin: download.origin, laptopHost: browserHost },
    { headers: { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" } },
  );
}
