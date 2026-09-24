# Firebase authentication and application sessions

## Scope and roles

Report 3 §2.1, §3.1.3, UC-01/UC-03 and D-01 define two human roles:

| Database role | Actor | Initial auth scope |
| --- | --- | --- |
| `uav_operator` | UAV Operator / Pilot | Own profile and sessions; future mission APIs must enforce owned/assigned records. |
| `operations_manager` | Operations Manager | Own sessions, list accounts, provision Operators, enable/disable Operators. Future mission APIs must enforce managed records. |

UAV/Simulator and External AI Provider are integration actors, not login roles. There is no Administrator or public application registration. Google/Firebase may create a provider identity, but the backend refuses application access unless the exact verified email was provisioned. Both Google and Firebase email/password are supported. Client-supplied roles are ignored. This implementation does not claim to implement mission authorization before those APIs exist.

Initial manager: `impro2003@gmail.com`, explicitly selected by the project owner. Bootstrap is an explicit database command, not a first-login promotion. It never promotes an existing Operator or creates another Manager if one exists. Manager role changes require a separate reviewed administrative process; the operator endpoint cannot disable the last Manager.

## Authentication flow

1. `/login` uses Firebase Google popup or email/password authentication with `inMemoryPersistence`. It never writes credentials to localStorage/sessionStorage/IndexedDB.
2. A fresh Firebase ID token is POSTed over the same-origin `/api/auth/session` proxy, with an ASP.NET antiforgery header and exact Origin validation.
3. Firebase Admin verifies signature, issuer, audience, expiry and revocation. The application additionally requires verified email, `google.com` or `password` sign-in provider, and authentication within five minutes.
4. Firebase Admin issues an **8-hour JWT session cookie**. PostgreSQL binds the verified Firebase UID to an already-provisioned account, serializes the operation against account deactivation, and stores SHA-256 hashes of the cookie and consumed login proof. Reusing a consumed ID token is rejected. No raw token is saved in the database.
5. Backend sets a host-only `HttpOnly`, `SameSite=Strict` cookie, plus `Secure` and the `__Host-` prefix in production. After exchange, the frontend calls Firebase `signOut` and performs a full navigation to `/portal`.
6. Every protected API request validates the Firebase session JWT (including revocation) and the active, unexpired database session. Role and account status come from the database, not client input or stale JWT role claims.
7. Logout revokes the database session and expires its cookie. Logout everywhere revokes all application sessions for the account. Disabling an Operator revokes its sessions transactionally. These actions do not sign the user out of their Google account or other Firebase applications.

The database deliberately retains revoked hashes to prevent replay, with audit events. Schedule retention cleanup according to the final SRS retention decision; do not remove recent consumed login proofs within the five-minute exchange window.

**DevTools limitation:** an owner of the browser can inspect cookies/network requests, including HttpOnly cookies. JavaScript cannot read the HttpOnly cookie, but XSS can still act through the browser. The temporary Firebase ID token also exists in JS memory during the exchange. Do not promise invisible tokens or absolute XSS prevention. Production script CSP uses per-request nonces; React escapes displayed account text; no `dangerouslySetInnerHTML` is used. Inline styles remain allowed for the existing drone demo. Credentials, cookie headers and Google token payloads must not be logged by infrastructure/APM.

## Email/password sign-in

Enable Firebase Authentication's Email/Password provider and create the user in Firebase Console. The password is sent directly to Firebase by its client SDK, never to the DroneOps backend or PostgreSQL. No public sign-up form is exposed. Provision the matching account/role in DroneOps separately.

The project owner explicitly granted `manager@droneopsintelligence.com` the `operations_manager` role. It is linked to its existing Firebase UID; `impro2003@gmail.com` retains its previous role.

Both login methods require verified email on the backend. After successful password authentication with an unverified email, the login form sends a Firebase verification email, clears the temporary Firebase session, and asks the user to verify then sign in again. It does not create an application session or mark the email verified administratively. The mailbox must be real and accessible. Firebase hosts the verification action page.

The form also offers password visibility and a password-reset request. Reset confirmation deliberately does not disclose whether an email exists. Password inputs are cleared after each attempt, and both login methods share a single busy lock. Keep Firebase email enumeration protection enabled. The original `/api/auth/google` route remains as a compatibility alias for the session exchange; new clients use `/api/auth/session`.

## Local setup

1. In Firebase Console for `droneops-intelligence`, enable **Authentication → Sign-in method → Google**. Add `localhost` to **Authentication → Settings → Authorized domains** (new projects may not include it). Add the production hostname before deployment.
2. Configure a Firebase Admin service account or Application Default Credentials on the backend. The JavaScript web configuration is public project identification and cannot replace server credentials. For a local service account JSON:

   ```sh
   export GOOGLE_APPLICATION_CREDENTIALS="/absolute/path/to/serviceAccountKey.json"
   ```

   Alternatively, set `Auth:ServiceAccountPath` in ignored `appsettings.Local.json` to the JSON path (absolute, or relative to the API content root). The local project uses `secrets/firebase-service-account.json`, excluded from Git and build/publish output. This path takes precedence over ADC; the loader accepts service-account credentials only and requires its project ID to match `Auth:ProjectId`.

   Store the file outside Git; do not paste its contents into source, `.env` public variables, or chat. Backend uses the .NET FirebaseAdmin SDK, not the provided Node.js initialization snippet.
3. Keep the existing `ConnectionStrings:DefaultConnection` in ignored `be/DroneOps/DroneOps.API/appsettings.Local.json` (or `ConnectionStrings__DefaultConnection`). Existing Supabase TLS certificate validation is preserved.
4. Explicitly apply the additive schema and bootstrap the initial Manager:

   ```sh
   ./be/DroneOps/run-api.sh -- --migrate-auth --bootstrap-manager
   ```

   This command exits after setup. It creates the private `droneops` schema with `roles`, `accounts`, `auth_sessions` and `auth_audit`. Use an owner/migration connection with DDL privileges. The backend connection needs access to this private schema and an RLS-bypassing backend role (the existing Supabase owner connection satisfies this); never expose that credential to the frontend. Tables enable RLS with no public policies, so anonymous/authenticated Supabase client roles have no access.
5. Run the API and frontend in separate terminals:

   ```sh
   ./be/DroneOps/run-api.sh
   cd fe
   npm run dev
   ```

   Open `http://localhost:3000/login`. Backend defaults to `http://localhost:5159`. Both frontend public origin and backend `Auth:PublicOrigin` must agree exactly. Copy `fe/.env.example` to ignored `fe/.env.local` and fill the Firebase web configuration. Required fields: API key, auth domain, project ID and app ID. The current local workspace is already configured; other checkouts need their own `.env.local`. No Analytics is initialized by the login flow.
6. Sign in as `impro2003@gmail.com`. Use **Operators & accounts** to add each Operator's Google email before their first application sign-in. There is no role picker on the login page.

## Production configuration

- Serve the frontend through HTTPS and run ASP.NET with `ASPNETCORE_ENVIRONMENT=Production`; run Next in production as well. Both modes must agree on cookie names. Production cookies deliberately will not work on plain HTTP.
- Set `Auth__PublicOrigin=https://your-hostname` and `API_BASE_URL` to the trusted private backend origin. Browser requests only use same-origin Next `/api`; CORS is not opened to arbitrary clients. Backend remains private, with TLS or a protected local proxy hop. TLS termination is expected at ingress.
- Configure ADC/service account for the correct Firebase project. Restrict Firebase web API key usage appropriately in Google Cloud without blocking Identity Toolkit / Secure Token. Changing project/auth domain also requires updating the explicit CSP allowlist in `fe/src/proxy.ts`.
- Set `Auth__DataProtectionKeyPath` to persistent, access-controlled shared storage for multiple API replicas, and protect keys at rest through deployment facilities. Keys protect CSRF tokens, not the Firebase JWT signing key. .NET's defaults are adequate for a single local instance only.
- App login throttling is 10 attempts/minute per backend peer. The BFF intentionally does not trust or forward arbitrary browser `X-Forwarded-For`; behind the BFF this is a conservative shared limit. Configure a trusted ingress rate limiter keyed by real client IP before scaling.
- Session duration can be set with `Auth__SessionHours` (1–336 hours); default 8 hours. No silent refresh: expiry requires sign-in again.
- Sensitive responses are `no-store`. Do not enable API response/body logging or shared caching of authenticated pages.

## API

| Method | Route | Access |
| --- | --- | --- |
| GET | `/api/auth/csrf` | Anonymous; returns request token, sets HttpOnly antiforgery cookie |
| POST | `/api/auth/session` | Anonymous + CSRF + allowed Origin + verified Firebase identity |
| GET | `/api/auth/me` | Active session |
| GET | `/api/auth/sessions` | Own active sessions |
| DELETE | `/api/auth/sessions/{id}` | Own session only |
| POST | `/api/auth/logout` | CSRF; idempotent when already signed out |
| POST | `/api/auth/logout-all` | Active session + CSRF |
| GET / POST | `/api/accounts` | Operations Manager; POST creates Operator only |
| PATCH | `/api/accounts/{id}/access` | Operations Manager; Operator targets only |

Every unsafe API request requires `Origin` and `X-CSRF-TOKEN`. CSRF tokens must be fetched again after login/logout because they are bound to the current identity. Session API responses never contain JWTs or token hashes.

## Verification

```sh
cd fe
npm run lint
npm run build
# From repository root, with a .NET 8 SDK:
dotnet build be/DroneOps/DroneOps.API
dotnet run --project be/DroneOps/DroneOps.AuthChecks
```

`DroneOps.AuthChecks` runs real HTTP middleware/controller checks with fake Firebase/database boundaries. It checks anonymous access, CSRF, Origin, unprovisioned accounts, role escalation, session ownership/replay, logout, deactivation and production cookie properties. It does not substitute for verifying the real Google popup, service account, or PostgreSQL integration.

References: [Firebase session cookies](https://firebase.google.com/docs/auth/admin/manage-cookies), [Google sign-in](https://firebase.google.com/docs/auth/web/google-signin), [Firebase persistence](https://firebase.google.com/docs/auth/web/auth-state-persistence), [Google branding](https://developers.google.com/identity/branding-guidelines). `fe/public/google-signin.png` is the unmodified Light / Square / Android+Web @4x asset from Google's official sign-in asset bundle, rendered at 270×60 with its original aspect ratio.

Email/password references: [Firebase password authentication](https://firebase.google.com/docs/auth/web/password-auth), [Verification and password reset](https://firebase.google.com/docs/auth/web/manage-users).

## Git hygiene

Root `.gitignore` excludes private credentials, local settings, `.env` files, key/certificate files, backend/frontend build output, test reports and tool caches. `fe/.env.example` contains placeholders only. `fe/src/lib/auth/firebase.ts` is application code and remains tracked; it reads public web settings from environment variables. Moving Firebase web settings out of source does not hide them from browser users. Service-account keys stay server-side and are also excluded from publish output by the API project file.

`.gitignore` does not remove already-tracked files or erase history. Before committing, inspect `git ls-files -ci --exclude-standard` and review staged files. If a real private credential was ever committed, rotate it; merely adding an ignore rule does not revoke it.
