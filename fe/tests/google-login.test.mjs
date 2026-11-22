import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText).toString("base64")}`;
const server = new URL("../node_modules/next/server.js", import.meta.url).href;

async function loadProxy(role = "uav_operator") {
  let source = await readFile(new URL("../src/app/api/[...path]/route.ts", import.meta.url), "utf8");
  source = source
    .replace(/^import .*pending-activation.*;$/m, 'const activationCookie="activation"; const clearActivation=()=>{}; const getActivation=()=>null; const createActivation=()=>"fixture";')
    .replace('"next/server"', JSON.stringify(server))
    .replace(/^import .*auth\/server.*;$/m, `const accessCookie="droneops_access"; const backendUrl=()=>"http://backend.test"; const sessionFromToken=async()=>({account:{id:"pilot-id",email:"pilot@gmail.com",displayName:"Pilot",role:${JSON.stringify(role)},isActive:true},expiresAt:"2026-12-01T00:00:00Z"});`);
  return import(compile(source));
}

async function googleRequest() {
  const { NextRequest } = await import(server);
  return new NextRequest("http://localhost:3000/api/auth/google-login", {
    method: "POST",
    headers: { host: "localhost:3000", origin: "http://localhost:3000", "content-type": "application/json" },
    body: JSON.stringify({ idToken: "google-id-token" }),
  });
}

test("Google login proxy accepts the route and stores only a validated pilot session", async () => {
  const { POST } = await loadProxy();
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, "http://backend.test/api/auth/google-login");
    assert.deepEqual(JSON.parse(options.body.toString()), { idToken: "google-id-token" });
    return Response.json({ accessToken: "backend-jwt" });
  });
  try {
    const response = await POST(await googleRequest(), { params: Promise.resolve({ path: ["auth", "google-login"] }) });
    assert.equal(response.status, 200);
    assert.match(response.headers.get("set-cookie") ?? "", /droneops_access=backend-jwt/);
    assert.equal((await response.json()).account.role, "uav_operator");
  } finally { mock.mock.restore(); }
});

test("Google login proxy rejects manager and incomplete activation without setting a session", async () => {
  for (const fixture of [
    { role: "operation_manager", body: { accessToken: "manager-jwt" }, status: 403, message: /only for registered pilot/i },
    { role: "uav_operator", body: { code: "first_login_required", requiresFirstLogin: true }, status: 409, message: /first sign-in/i },
  ]) {
    const { POST } = await loadProxy(fixture.role);
    const mock = test.mock.method(globalThis, "fetch", async () => Response.json(fixture.body));
    try {
      const response = await POST(await googleRequest(), { params: Promise.resolve({ path: ["auth", "google-login"] }) });
      assert.equal(response.status, fixture.status);
      assert.doesNotMatch(response.headers.get("set-cookie") ?? "", /droneops_access=/);
      assert.match((await response.json()).message, fixture.message);
    } finally { mock.mock.restore(); }
  }
});

test("pilot login UI sends the Google OAuth credential and never creates an account", async () => {
  const source = await readFile(new URL("../src/app/login/login-form.tsx", import.meta.url), "utf8");
  assert.match(source, /GoogleAuthProvider\.credentialFromResult\(result\)/);
  assert.match(source, /JSON\.stringify\(\{ idToken: credential\.idToken \}\)/);
  assert.doesNotMatch(source, /result\.user\.getIdToken/);
  assert.match(source, /mode === "pilot" && <div className="google-login-section">/);
  assert.doesNotMatch(source, /api<AuthSession>\("\/auth\/register"/);
});

test("login CSP permits only the Firebase endpoints required by Google authentication", async () => {
  const source = await readFile(new URL("../src/proxy.ts", import.meta.url), "utf8");
  assert.match(source, /https:\/\/identitytoolkit\.googleapis\.com/);
  assert.match(source, /https:\/\/securetoken\.googleapis\.com/);
  assert.match(source, /frame-src\$\{firebaseAuthOrigin/);
  assert.match(source, /frame-ancestors 'none'/);
  assert.doesNotMatch(source, /https:\/\/\*\.googleapis\.com/);
});
