# Attendance Desktop Agent

## First run

1. In the web portal (or directly via the backend's
   `POST /api/tenants/{tenantId}/stations`), create a station for this
   PC and copy the generated API key — it is shown once.
2. Launch the agent. On first run it prompts for the backend base URL and
   the station API key; these are stored in a local SQLite database at
   `%LOCALAPPDATA%\ZakAttendanceAgent\agent.db`.
3. Enter an employee code and press a punch button. The first punch for
   a given employee requires connectivity (to resolve the code and fetch
   their template); after that, punches work offline and sync
   automatically every 30 seconds once connectivity returns. The one
   exception: an employee enrolled on THIS station via the admin-gated
   Enroll panel normally already has both their employee record and
   template cached locally by the time enrollment completes, so their
   first punch usually works offline too — but this is best-effort, not
   guaranteed: `EnrollmentService.EnrollAsync` deliberately swallows a
   failure populating the local cache after a successful upload (so a
   locked/full-disk `agent.db` doesn't turn a completed enrollment into a
   reported failure), which means that specific failure leaves the
   employee needing connectivity for their first punch after all, with
   nothing logged today to say so. Connectivity is otherwise only
   required the first time a station encounters an employee it has never
   seen (enrolled elsewhere, or not yet cached).

## Running tests

`dotnet test agent/tests/AttendanceAgent.Tests`

All service-layer logic is tested against fakes (`FakeFingerprintDevice`,
`FakeFingerprintVerifier`, `FakeBackendApiClient`) — no physical scanner
or backend server is required.

## Real hardware bring-up (manual, not automated)

### SecuGen

SecuGen SDK integration is wired in (`SecuGenFingerprintDevice`,
`SecuGenFingerprintVerifier`, `SecuGenFingerprintEnroller`, Release builds
only — Debug still uses the fakes for hardware-free local development).
The following still requires
a physical SecuGen device (Hamster Plus or compatible FDx-family reader)
and cannot be automated:

1. ~~Confirm `FIRTextData` is genuinely base64~~ — **VERIFIED WRONG** against a
   real SecuGen USB SDU03P/FDU03 device: `FIRTextData` is not base64 (two
   real captures both failed `Convert.FromBase64String`, one already a
   multiple of 4 in length, ruling out a padding issue). Fixed: the device
   now uses plain UTF8 byte encoding (`SecuGenFirTextEncoding`), confirmed
   via a full real enroll -> capture -> VerifyMatch cycle producing
   `IsMatched: true`. Also confirmed the SDK's default 10s capture timeout
   is too tight in practice; bumped to 15s in `SecuGenFingerprintDevice.Acquire()`.
2. Enroll a fingerprint for a test employee via the enrollment flow.
3. Punch in with the correct finger — verify success and that the device
   is released immediately after each capture (no exclusive lock held
   between punches — confirm by running SecuGen's own diagnostic tool
   concurrently and seeing it can still see the device).
4. Punch in with a different finger — verify rejection.
5. Disconnect the network, punch in/out several times, reconnect — verify
   the queued punches sync within 30 seconds and appear in the backend's
   daily attendance view exactly once each (no duplicates).
6. **Enrollment via `Enroll()`, not `Capture()`.** Confirm the vendor's `Enroll("")` call
   works reliably in kiosk mode (`ConfigureKioskCaptureWindow`'s settings) the same way
   `Capture(FIRPurpose.VERIFY)` already does — this is a different vendor call, not the same
   one Task 10 already hardware-verified, so its own timeout/window behavior needs its own
   confirmation rather than being assumed identical.
7. **Iterative merge quality.** `SecuGenFingerprintEnroller.MergeCaptures` folds captures one
   at a time via `CreateTemplate`. Confirm against real hardware that three genuine
   placements of the same finger fold into a template that later verifies correctly through
   the existing punch flow.
8. **Admin password entered in cleartext.** `InputBoxAdminCredentialPrompt` uses
   `Microsoft.VisualBasic.Interaction.InputBox`, which cannot mask input — the web portal admin
   password is visible on a shared kiosk screen while being typed. Accepted for now (matches the
   existing precedent of `App.xaml.cs`'s first-run station-key prompt using the same InputBox
   mechanism), but a real deployment should replace this with a masked-input WPF dialog.
9. **SecuGen sample count per `Enroll()` call is unmeasured.** Reflection over
   `SecuBSPMx.NET.dll` shows `BSPInitInfo` carries a `SamplesPerFinger` field distinct from the
   `Capture(FIRPurpose.VERIFY)` path's settings — `Enroll("")` may require more than one physical
   placement per call. If so, the UI's "Place your finger (1 of 3)" progress text would
   undercount how many times a real SecuGen device expects a finger presented for a SINGLE one
   of those three `Enroll()` calls. Needs measurement against a real SecuGen device (unavailable
   during this branch's review) before relying on the current progress text being accurate.

### ZK4500

A tenant configured for the `Zk4500` vendor can use the ZKFinger SDK
integration (`ZkFingerprintDevice`, `ZkFingerprintVerifier`,
`ZkFingerprintEnroller`, built via
`dotnet build agent/AttendanceAgent.sln -c Release -p:DeviceVendor=Zk4500`).
Debug builds always use the fakes regardless of `DeviceVendor`. The
following still requires a physical ZK4500-class device and cannot be
automated:

1. **Raw byte-array templates.** Confirm that templates captured via
   `AcquireFingerprint` are already raw bytes (not text-encoded) and
   that they round-trip correctly through storage and matching without
   encoding surprises (unlike SecuGen's `FIRTextData` encoding surprise).
2. **AcquireFingerprint polling timeout.** The SDK's capture method is
   poll-based and returns immediately with `ZKFP_ERR_CAPTURE` (-8) if no
   finger is present. Confirm that the 15-second timeout (matching SecuGen's
   tuned timeout) is sufficient for this device; adjust
   `ZkFingerprintDevice.CaptureTimeout` if hardware bring-up shows
   otherwise.
3. **Rapid re-init/terminate cycling.** A single punch attempt calls
   `zkfp2.Init()`/`Terminate()` twice in quick succession (once for
   capture, once for verify — since the verifier runs after the device
   is released). Confirm that the native library tolerates this cycle
   without errors or hangs.
4. **System-wide driver installation.** The ZKFinger SDK ships only a
   managed wrapper (`libzkfpcsharp.dll`), with the native runtime
   distributed separately via `setup.exe`. Confirm whether running
   `setup.exe` is a required station provisioning step, or whether the
   driver is already installed system-wide on target machines.
5. Enroll a fingerprint for a test employee via the enrollment flow.
6. Punch in with the correct finger — verify success.
7. Punch in with a **different** finger — verify rejection. This is the
   one gate that catches a broken match decision, in a path where a
   false accept means one employee can punch in as another.
8. ~~Verify the device is not exclusively locked between punches~~ —
   **CONFIRMED not locked**: six consecutive `zkfp2.OpenDevice(0)` calls
   against the attached ZK4500 (VID_1B55&PID_0840) all returned distinct
   non-null handles, so a concurrent reader (e.g. ZKTeco's own
   diagnostic tool) can still see the device between punches.
9. Disconnect the network, punch in/out several times, reconnect — verify
   the queued punches sync within 30 seconds and appear in the backend's
   daily attendance view exactly once each (no duplicates).
10. **Enrollment merge quality.** `ZkFingerprintEnroller.MergeCaptures` requires exactly 3
    raw captures and folds them via a single `DBMerge` call. Confirm against real hardware
    that three genuine placements of the same finger merge into a template that later
    verifies correctly through the existing punch flow (not just that `DBMerge` returns
    `ZKFP_ERR_OK`) — a technically-successful merge of three placements that weren't
    consistent enough could still produce a poor-quality template.
11. **`CaptureForEnrollment` reusing `Capture()`.** Confirmed via reflection that ZK's SDK has
    no separate enrollment-purpose acquisition call, so this delegates directly to the same
    `AcquireFingerprint`-backed `Capture()` used for punches. Confirm this assumption holds
    in practice — if a future SDK version or device firmware introduces a
    purpose-distinguishing capture mode, this would need revisiting.
12. **Merged-template buffer size.** `ZkFingerprintEnroller` declares its own
    `MergedTemplateBufferSize = 2048`, a SEPARATE constant from
    `ZkFingerprintDevice.TemplateBufferSize` (also 2048 today, but the two can silently
    drift — changing one does not change the other). Neither has hardware confirmation
    that a *merged, 3-sample* registration template actually fits in 2048 bytes on this
    device. If a real merge ever needed more space, the failure mode depends on how the
    native marshaling handles an undersized output buffer — confirm real merged-template
    sizes against hardware before relying on either constant, and confirm they stay
    fitting for purpose (a difference between "single capture" and "merged template" size
    requirements is realistic, not just a formatting nitpick) if either is ever changed.

**Known risk, not yet mitigated:** `zkfp2.DBMatch` (called from
`ZkFingerprintVerifier.Verify`) crashed the entire agent process with an
uncatchable `AccessViolationException` when given 2048 bytes of random
template data in place of a real template — confirmed against the real
native library, not simulated. `.NET` cannot safely catch or recover from
this; the whole kiosk process dies and needs an external restart. The
enrollment path added since this was first documented has a SECOND,
equally uncatchable entry point into the same native library:
`ZkFingerprintEnroller.MergeCaptures`'s `zkfp2.DBMerge` call has no
pre-call validation either — lower exposure (its inputs are strictly
device-produced captures, never data read back from storage or the
network), but `EnrollmentService.EnrollAsync`'s
`catch (Exception ex) { ... "Failed to build enrollment template" ... }`
around that call CANNOT catch a native `AccessViolationException`, so the
enrollment path reads as failure-tolerant around a call whose worst
failure mode still kills the kiosk process exactly like `DBMatch`'s does.
A length/null check before either call would NOT have prevented the
`DBMatch` crash that was actually reproduced (the random data was already
a valid-length 2048-byte array), so no defensive check was added for
either — a check that doesn't stop the actual failure mode would be false
confidence. Realistic trigger for `DBMatch`: a stored template that
is corrupted in transit/storage, or a mismatch between a tenant's
configured `deviceVendor` and what a station actually captures with (see
open item 4 above and the `DeviceVendor` MSBuild property — nothing
today cross-checks that a station's compiled vendor matches the
templates its employees actually enrolled with). Properly closing this
gap needs either vendor-documented template format validation (not
available in this SDK's docs) or moving BOTH the match call and the merge
call out-of-process so a crash in either can't take the whole kiosk down
— both options are bigger than this integration task's scope and are
tracked as follow-up work.
