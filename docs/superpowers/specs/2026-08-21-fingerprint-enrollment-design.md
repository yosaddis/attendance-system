# Fingerprint Enrollment (Desktop Agent) — Design

## Problem

Templates are enrolled once per employee and synced down to stations (per
the spec), but no product surface exists to actually capture and upload
one. Today the only way to enroll a template is a direct, unauthenticated
`POST /api/templates` call — there is no UI in either the desktop agent or
the web portal. Since a physical fingerprint scanner only exists at a
station, enrollment has to happen through the desktop agent — the web
portal has no hardware access.

The backend side is already built and unchanged by this work:
`POST /api/templates` (station-key authenticated, upserts a
`FingerprintTemplate` row, tags it with the station's `DeviceVendor`) and
`GET /api/templates/{employeeId}` (used by `TemplateCacheService` today).
This design adds the capture UI and the vendor-specific quality-enrollment
mechanics only.

## Access control

Enrollment is gated by a **TenantAdmin login**, checked once per
enrollment attempt — not a persistent session. The operator clicks
"Admin" on the punch screen, enters a TenantAdmin email/password (the
same credentials used for the web portal), and the agent calls the
existing `POST /api/auth/login` directly. Only a successful `TenantAdmin`
role result unlocks the Enroll panel; the resulting JWT is discarded
immediately after the check — it is never stored, and it is not the
credential used for the actual template upload (that continues to use the
station's own `X-Station-Key`, exactly like every other backend call the
agent makes).

Enrollment mode is single-shot: after one enrollment completes (success
or failure), the UI returns to the normal punch screen and admin access
is gone until the next explicit login. There is no timed session window.

## Capture mechanics — a real per-vendor difference

"Three captures, merged for quality" is not the same operation on both
vendors, and the design has to route through the vendor-appropriate call
rather than reusing the existing punch-capture path unchanged:

- **ZK4500** (`ZkFingerprintDevice`): a punch capture and an enrollment
  capture are the *same underlying call* (`zkfp2.AcquireFingerprint`) —
  ZK's SDK does not distinguish capture purpose. Three raw captures are
  taken, then `zkfp2.DBMerge(t1, t2, t3, out merged)` folds them into one
  template in a single call.
- **SecuGen** (`SecuGenFingerprintDevice`): enrollment uses a genuinely
  different vendor call, `m_SecuBSP.Enroll()`, not
  `Capture(FIRPurpose.VERIFY)` (confirmed by reading the vendor's own
  `mainform.cs` demo). Merging is incremental: each new `Enroll()` result
  is folded into the running template via
  `CreateTemplate(newCapture, runningMerged, "")` — three placements,
  three fold steps, not one batch call. (The demo shows `CreateTemplate`
  can also embed a payload string into the very first template; this
  design does not use that — we already track employee identity in our
  own backend, so no vendor-side payload is needed.)

This means the interface needs two new, distinct hooks rather than one:
a per-capture "give me one raw enrollment-purpose sample" method, and a
separate "fold N raw samples into one final template" method whose
implementation is free to be either a single batch call (ZK) or an
iterative fold (SecuGen).

## Interfaces

```csharp
public interface IFingerprintDevice
{
    void Acquire();
    byte[] Capture();               // existing, punch/verify-purpose
    byte[] CaptureForEnrollment();  // NEW — one raw enrollment-purpose sample
    void Release();
}

public interface IFingerprintEnroller
{
    // Folds exactly the raw samples CaptureForEnrollment produced into one
    // final template. Implementations decide internally whether that's a
    // single batch call or an iterative fold — callers don't need to know.
    byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures);
}
```

- `ZkFingerprintDevice.CaptureForEnrollment()` — identical body to
  `Capture()` (same poll loop, same `AcquireFingerprint` call; ZK has no
  purpose distinction). Could delegate to `Capture()` directly.
- `SecuGenFingerprintDevice.CaptureForEnrollment()` — calls
  `_secuBsp.Enroll("")`, returns `SecuGenFirTextEncoding.ToBytes(FIRTextData)`
  on success (reusing the existing UTF8 encoding helper — `Enroll()`
  populates the same `FIRTextData` property `Capture()` does).
- `ZkFingerprintEnroller.MergeCaptures` — requires exactly 3 captures
  (throws otherwise), calls `zkfp2.Init()`/`DBInit()` (mirroring
  `ZkFingerprintVerifier`'s independent init/cleanup pattern, since this
  runs after the device has already been released), then
  `zkfp2.DBMerge(t1, t2, t3, out merged, ref mergedLen)`, returns the
  right-sized merged bytes. `DBFree`/`Terminate` in a `finally`.
- `SecuGenFingerprintEnroller.MergeCaptures` — requires at least 1 capture
  (works for any N, unlike ZK's fixed-3), folds iteratively via its own
  short-lived `SecuBSPMx` instance (mirroring `SecuGenFingerprintVerifier`):
  first capture's FIR becomes the running merged FIR directly; each
  subsequent capture folds via `CreateTemplate(nextFir, runningMergedFir, "")`.
- `FakeFingerprintDevice.CaptureForEnrollment()` / `FakeFingerprintEnroller` —
  trivial fakes for hardware-free testing, matching the existing
  `FakeFingerprintDevice`/`FakeFingerprintVerifier` pattern.

Both new vendor implementations are hardware-unverified until real bring-up
(same caveat every prior vendor integration in this project carried) —
tracked as an explicit open item in the implementation plan, not silently
assumed correct.

## New service: `EnrollmentService`

Mirrors `PunchCaptureService`'s shape:

```csharp
public interface IEnrollmentService
{
    Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,  // (captureIndex, totalCaptures) for UI updates
        CancellationToken ct = default);
}
```

Orchestration:
1. Resolve `employeeCode` via the existing `IEmployeeDirectoryService` (same
   lookup punching already uses) — unknown code fails immediately, no
   device interaction.
2. `device.Acquire()` once.
3. Loop 3 times: invoke `onCaptureProgress(i, 3)`, call
   `device.CaptureForEnrollment()`, collect the raw bytes. Any capture
   failure/timeout aborts the whole enrollment (no partial 2-of-3 upload)
   — `device.Release()` still runs via `finally`. This is a NEW
   orchestration, not a reuse of `DeviceCapture.CaptureOnce` (which only
   ever does one Acquire→Capture→Release cycle) — it has the same
   guaranteed-release safety property, but wraps a loop of 3 captures
   inside a single Acquire/Release pair instead of one capture per pair
   (avoiding the ~1-1.3s `OpenDevice` cost 3 times over, per the ZK
   whole-branch review's timing findings).
4. `device.Release()`.
5. `enroller.MergeCaptures(rawCaptures)` → final template bytes (device
   already released — matches how `IFingerprintVerifier` runs after
   release today).
6. `IBackendApiClient.EnrollTemplateAsync(employeeId, finalTemplate)` —
   base64-encodes and POSTs to `/api/templates` with the station's
   existing `X-Station-Key`.
7. Return success/failure with a user-facing message, same
   `EnrollmentResult(bool Success, string Message)` shape as
   `PunchResult`.

This runs off the UI thread via `Task.Run`, matching the fix already
applied to `PunchCaptureService.CapturePunchAsync` for the same reason
(device open/capture is slow and synchronous).

## Backend API client additions

```csharp
public interface IBackendApiClient
{
    // existing methods unchanged...
    Task<LoginResult?> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<bool> EnrollTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default);
}
```

- `LoginAsync` posts to `/api/auth/login` directly (no `X-Station-Key` —
  this is the same public login endpoint the web portal uses, not a
  station-scoped call). Returns `null` on any non-success response
  (caller treats null as "denied", regardless of whether it was a 401 or
  a network failure — matching this being a security gate, not a status
  page). Returns the role string on success so the caller can reject a
  valid login that isn't `TenantAdmin`.
- `EnrollTemplateAsync` uses the existing station-key-authenticated
  `BuildRequestAsync` helper already used by every other method in this
  class, POSTing `{ employeeId, templateData: base64 }` to
  `/api/templates`. Returns `false` on any non-2xx (the caller doesn't
  need to distinguish 400 vs 5xx here the way punch-batch submission
  does — there's no offline queue for enrollment; the operator is
  standing right there and can just retry).

## UI changes (`MainWindow.xaml` / `MainViewModel`)

The window gets a simple mode switch (Punch / AdminLogin / Enroll), driven
by a `Mode` property on `MainViewModel`:

- **Punch mode (default):** unchanged, plus one new "Admin" button.
- **AdminLogin mode:** two `InputBox` prompts (email, password — same
  blocking-dialog style the existing first-run setup already uses, so
  this introduces no new UI pattern). Success with `TenantAdmin` role →
  Enroll mode. Anything else → error message, back to Punch mode.
- **Enroll mode:** employee code entry, a "Start" button, then progress
  text ("Place your finger (1 of 3)", "(2 of 3)", "(3 of 3)") driven by
  `EnrollmentService`'s progress callback, then a result message. Always
  returns to Punch mode afterward, success or failure — enrollment mode
  never lingers.

## Testing

- Unit tests for `EnrollmentService` against `FakeFingerprintDevice` /
  `FakeFingerprintEnroller` / a fake `IBackendApiClient`: happy path (3
  successful captures → merge → upload), abort on a failed capture at
  each of the 3 positions, unknown employee code short-circuits before
  any device access, backend upload failure surfaces as a failure result.
- Unit tests for the admin-gate logic in `MainViewModel` (or wherever the
  login-then-mode-switch logic lives) against a fake `IBackendApiClient`:
  correct TenantAdmin credentials unlock Enroll mode, wrong password
  denies, correct password but wrong role (e.g. `Operator`) denies.
- `ZkFingerprintEnroller`/`SecuGenFingerprintEnroller` registration tests
  (construct + interface-assignable), same pattern as the existing
  `*RegistrationTests` for devices/verifiers — proves wiring, not
  hardware behavior.
- Real hardware bring-up (both vendors, once a physical device is
  available for each): confirm the actual merge call succeeds, confirm a
  template enrolled via 3 real placements later verifies correctly
  through the existing punch flow, confirm SecuGen's `Enroll()` doesn't
  require anything `Capture(FIRPurpose.VERIFY)` didn't already need
  (device open state, timeout tuning) — treated with the same "don't
  assume, verify" rigor as every prior vendor integration in this
  project, tracked as an explicit open checklist in
  `agent/README.md`, not silently assumed correct.

## Explicitly out of scope (YAGNI)

- No timed admin session (single-enrollment gate only, per the approved
  design).
- No re-enrollment confirmation dialog — `POST /api/templates` already
  upserts; overwriting an existing template is intentional and silent
  (an admin choosing to re-enroll someone knows what they're doing).
- No offline queueing for enrollment uploads (unlike punches) — the
  admin is present at the kiosk and can just retry on failure.
- No vendor-side payload/userID embedding in the template (SecuGen's
  `CreateTemplate` payload parameter) — identity is tracked entirely in
  our own backend.
