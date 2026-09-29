import { NextRequest, NextResponse } from "next/server";
import { backendUrl } from "@/lib/auth/server";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
const allowed = /^(auth\/(csrf|google|session|me|sessions|logout|logout-all)|auth\/sessions\/[0-9a-f-]{36}|accounts|accounts\/[0-9a-f-]{36}\/access)$/;

async function forward(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const path = (await context.params).path.join("/");
  if (!allowed.test(path)) return NextResponse.json({ message: "Not found." }, { status: 404 });
  const headers = new Headers();
  for (const name of ["content-type", "x-csrf-token", "origin", "user-agent"]) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }
  const cookieNames = ["droneops_session", "__Host-droneops_session", "droneops_csrf", "__Host-droneops_csrf"];
  headers.set("cookie", request.cookies.getAll().filter(c => cookieNames.includes(c.name)).map(c => `${c.name}=${c.value}`).join("; "));
  try {
    // Stream with a strict cap so oversized requests cannot consume unbounded BFF memory.
    const chunks: Uint8Array[] = [];
    let size = 0;
    if (request.body) {
      const reader = request.body.getReader();
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        size += value.byteLength;
        if (size > 16384) { await reader.cancel(); return NextResponse.json({ message: "Request is too large." }, { status: 413 }); }
        chunks.push(value);
      }
    }
    const upstream = await fetch(`${backendUrl()}/api/${path}`, {
      method: request.method, headers, body: size ? Buffer.concat(chunks) : undefined,
      cache: "no-store", redirect: "manual", signal: AbortSignal.timeout(20000),
    });
    const responseHeaders = new Headers({ "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" });
    if (upstream.headers.has("content-type")) responseHeaders.set("Content-Type", upstream.headers.get("content-type")!);
    for (const cookie of upstream.headers.getSetCookie()) responseHeaders.append("Set-Cookie", cookie);
    return new NextResponse(upstream.body, { status: upstream.status, headers: responseHeaders });
  } catch {
    return NextResponse.json({ message: "Sign-in services are temporarily unavailable. Please try again later." }, { status: 503, headers: { "Cache-Control": "no-store" } });
  }
}
export { forward as GET, forward as POST, forward as PATCH, forward as DELETE };
