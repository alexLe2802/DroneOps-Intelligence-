import { activationCookie, createActivation, getActivation, clearActivation } from "@/lib/auth/pending-activation";
import { NextRequest, NextResponse } from "next/server";
import { accessCookie, backendUrl, sessionFromToken } from "@/lib/auth/server";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
const allowed = /^(auth\/(csrf|login|forgot-password|reset-password|me|sessions|logout|logout-all)|auth\/sessions\/[0-9a-f-]{36}|accounts|accounts\/[0-9a-f-]{36}\/access|pilot-registrations|pilot-registrations\/first-time-login(?:\/resend-otp)?|pilot-registrations\/[0-9a-f-]{36}(?:\/(?:approve|reject))?|users\/(profile|pilot-profile)|uavs\/myUAV|uavs|admin\/uavs\/(?:pending|[0-9a-f-]{36}(?:\/(?:activate|reject|deactivate))?)|missions|missions\/[0-9a-f-]{36}|mission-suggestions(?:\/mine|\/[0-9a-f-]{36})?|geofences(?:\/[0-9a-f-]{36})?|flight-history(?:\/analytics|\/[0-9a-f-]{36}\/(?:finalize|report|telemetry|incidents))?|ai\/assessments|ai\/assessments\/mission\/[A-Za-z0-9_-]{1,80})$/;

async function forward(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const path = (await context.params).path.join("/");
  if (!allowed.test(path)) return NextResponse.json({ message: "Not found." }, { status: 404 });
  const token = request.cookies.get(accessCookie)?.value;
  if (!["GET", "HEAD"].includes(request.method) && request.headers.get("origin") !== request.nextUrl.origin)
    return NextResponse.json({ message: "Request origin is not allowed." }, { status: 403 });
  if (path === "auth/me") {
    const session = token ? await sessionFromToken(token) : null;
    return NextResponse.json(session ?? { message: "Sign in again." }, { status: session ? 200 : 401 });
  }
  if (path === "auth/logout" && request.method === "POST") {
    const response = NextResponse.json({ message: "Signed out of this browser." });
    clearActivation(request.cookies.get(activationCookie)?.value);
    response.cookies.set(activationCookie, "", { httpOnly: true, sameSite: "strict", secure: process.env.NODE_ENV === "production", path: "/", maxAge: 0 });
    response.cookies.set(accessCookie, "", { httpOnly: true, sameSite: "strict", secure: process.env.NODE_ENV === "production", path: "/", maxAge: 0 });
    return response;
  }
  if (path === "auth/sessions" || path.startsWith("auth/sessions/") || path === "auth/logout-all")
    return NextResponse.json({ message: "The PostgreSQL login API does not support session listing or revoking all JWTs yet. Sign out of this browser instead." }, { status: 501 });
  const aiRequest = path === "ai/assessments" || path.startsWith("ai/assessments/mission/");
  const timeoutMs = (path === "ai/assessments" || path === "mission-suggestions") && request.method === "POST" ? 60000 : 20000;
  const headers = new Headers();
  for (const name of ["content-type", "x-csrf-token", "origin", "user-agent"]) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }
  if (token) headers.set("Authorization", `Bearer ${token}`);
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
    let body = size ? Buffer.concat(chunks) : undefined;
    if (path.startsWith("pilot-registrations/first-time-login") && request.method === "POST") {
      const activation = getActivation(request.cookies.get(activationCookie)?.value);
      if (!activation) return NextResponse.json({ message: "Your activation session expired. Return to sign in to continue." }, { status: 409 });
      const input = JSON.parse(body?.toString() ?? "{}");
      if (path === "pilot-registrations/first-time-login" && (typeof input.otp !== "string" || !/^[0-9]{6}$/.test(input.otp))) return NextResponse.json({ message: "Enter the six-digit OTP." }, { status: 400 });
      if (path === "pilot-registrations/first-time-login") activation.attempts++;
      body = Buffer.from(JSON.stringify({ email: activation.email, password: activation.password, ...(path === "pilot-registrations/first-time-login" ? { otp: input.otp } : {}) }));
    }
    const upstream = await fetch(`${backendUrl()}/api/${path}${request.nextUrl.search}`, {
      method: request.method, headers, body,
      cache: "no-store", redirect: "manual", signal: AbortSignal.timeout(timeoutMs),
    });
    if (upstream.ok && request.method === "POST" && (path === "auth/login" || path === "pilot-registrations/first-time-login")) {
      const result = await upstream.json();
      if (path === "auth/login" && result.code === "first_login_required" && result.requiresFirstLogin === true) {
        const credentials = JSON.parse(body!.toString());
        clearActivation(request.cookies.get(activationCookie)?.value);
        const id = createActivation(credentials.email, credentials.password);
        const response = NextResponse.json({ code: result.code, requiresFirstLogin: true, message: result.message });
        response.cookies.set(activationCookie, id, { httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "strict", path: "/", maxAge: 1800 });
        return response;
      }
      if (typeof result.accessToken !== "string") return NextResponse.json({ message: "Backend returned no access token." }, { status: 502 });
      const session = await sessionFromToken(result.accessToken);
      if (!session) return NextResponse.json({ message: "The account cannot access DroneOps." }, { status: 403 });
      clearActivation(request.cookies.get(activationCookie)?.value);
      const response = NextResponse.json(session);
      response.cookies.set(activationCookie, "", { httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "strict", path: "/", maxAge: 0 });
      response.cookies.set(accessCookie, result.accessToken, { httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "strict", path: "/", expires: new Date(session.expiresAt) });
      return response;
    }
    const responseHeaders = new Headers({ "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" });
    if (upstream.headers.has("content-type")) responseHeaders.set("Content-Type", upstream.headers.get("content-type")!);
    const cookies = upstream.headers.getSetCookie();
    if (cookies.length > 0) {
      for (const cookie of cookies) responseHeaders.append("Set-Cookie", cookie);
    } else {
      // Some Node/Next runtimes expose a single Set-Cookie value through get()
      // but return an empty array from getSetCookie(). Login must still forward it
      // to the browser or the subsequent /portal request appears anonymous.
      const cookie = upstream.headers.get("set-cookie");
      if (cookie) responseHeaders.append("Set-Cookie", cookie);
    }
    return new NextResponse(upstream.body, { status: upstream.status, headers: responseHeaders });
  } catch {
    return NextResponse.json({ message: aiRequest
      ? "AI assessment service is temporarily unavailable. Refresh assessment history before retrying."
      : "Backend services are temporarily unavailable. Please try again later." }, { status: 503, headers: { "Cache-Control": "no-store" } });
  }
}
export { forward as GET, forward as POST, forward as PUT, forward as PATCH, forward as DELETE };
