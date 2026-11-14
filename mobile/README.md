# DroneOps Operator · Flutter

Flutter/Dart client for Android and iOS. One shared UI/codebase, no WebView and no separate Swift/Kotlin feature implementation. Platform runner files are required to package APK/IPA. The old Next.js `/mobile` flow is not used.

## Scope and criteria (SRS IF-02)

- Operator password login, six-digit first-login approval OTP/resend, secure token storage, validated session restore, expired-session handling and local sign-out. Manager accounts are rejected by this client.
- Own profile: name, phone, DOB, experience years; shared ProfileVersion/ExpectedVersion and explicit 409 reload. Approved license is read-only.
- Own/assigned missions: registry/search, mission details, planned waypoints, persisted validation, pending proposal/decision evidence and recorded feedback. No route authoring, Manager administration or aircraft control commands.
- Live monitoring: scoped flight list and selected-mission telemetry, nullable measurements, source/received timestamps, freshness and connectivity. Never label a stale or invalid sample live. Poll only while visible/in foreground; one request in flight; late responses cannot update a paused/disposed view. Search filters locally without a request per keystroke.
- Notifications with unread status and mark-read; incident registry/filter/pagination, incident evidence and recorded history. In-app refresh, not background push.
- Flight history with UTC date/UAV/status filters and pagination; report generation/coverage, immutable revisions, planned execution snapshot when available, recorded route and incident evidence. Missing source responses remain unavailable.
- Textual route/evidence remains available when basemap tiles fail; labels, touch targets, dark Material UI, error/retry/empty states.
- Backend remains responsible for JWT validation, ownership and data freshness. Mobile does not grant extra permissions. API origin uses HTTPS; no integration/gateway credentials are embedded.

Baseline source: `docs/Review 2 submission/Report3_Software Requirement Specification.docx`, IF-02 and ACT-01. The submitted SDD does not describe a separate mobile implementation. The team's latest decision on 2026-11-14 restores mobile scope using Flutter. Submitted documents were not edited.

## Run and verify

Flutter 3.47.1 / Dart 3.13.1, Android SDK 36 and JDK 17. iOS requires macOS/Xcode; minimum iOS 15. Backend default is `https://api.droneops.click` (direct .NET origin, not the Next.js proxy). Override per build:

```sh
cd mobile
flutter pub get
flutter analyze
flutter test
flutter run --dart-define=API_BASE_URL=https://api.droneops.click
flutter build apk --debug --dart-define=API_BASE_URL=https://api.droneops.click
flutter build ipa --release --no-codesign --dart-define=API_BASE_URL=https://api.droneops.click
```

Debug HTTP is allowed only for loopback/emulator hosts by the API client. Production artifacts require HTTPS. Never commit password/token/API secrets. Dependencies are locked in pubspec.lock. Android backups are disabled to protect secure-storage material; iOS Keychain entitlements are configured.

## Distribution

- Debug APK: `build/app/outputs/flutter-apk/app-debug.apk`, signed with a local development key, for testing only.
- Android release: configure ignored `android/key.properties` (`storeFile`, `storePassword`, `keyAlias`, `keyPassword`) using the team's existing keystore, then `flutter build apk --release`. Without it release remains unsigned, never silently signed with a debug key.
- iOS unsigned archive: `build/ios/archive/Runner.xcarchive`. `--no-codesign` deliberately skips IPA export. It is not installable on an iPhone.
- Signed IPA: open `ios/Runner.xcworkspace` in Xcode, add the Apple account through Xcode Settings → Accounts, select the team's Team and unique bundle ID under Signing & Capabilities. Install a matching certificate/provisioning profile, then run `flutter build ipa --release --export-options-plist=<private ExportOptions.plist>`. Output: `build/ios/ipa/`. Apple ID email alone does not provide signing assets. Do not paste Apple passwords/certificates into chat or commit them.

A shared API JWT currently has no server logout/revocation endpoint: sign-out destroys local credentials; the backend still checks expiry/permissions on each request. Full server-side token revocation requires a separately approved backend contract. License proposals, avatar/email-change workflow, route authoring and Manager review remain in Web. No push/background monitoring, offline mutation queue, auto-arm, failsafe bypass or invented telemetry is included.

## Verification limits

See `test/`: API/secure-store fixtures, Operator/Manager scope, OTP, expiry, profile concurrency, filter serialization, missing values/freshness, lifecycle polling and widget sign-in/out. These fixtures are not hosted-backend, real-device, flight or usability acceptance. CI verifies Flutter and creates test APK; manual workflow can create an unsigned iOS archive. Store publication and physical-device signing are separate from implementation completion.
