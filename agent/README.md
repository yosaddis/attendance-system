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
   automatically every 30 seconds once connectivity returns.

## Running tests

`dotnet test agent/tests/AttendanceAgent.Tests`

All service-layer logic is tested against fakes (`FakeFingerprintDevice`,
`FakeFingerprintVerifier`, `FakeBackendApiClient`) — no physical scanner
or backend server is required.

## Real hardware bring-up (manual, not automated)

### SecuGen

SecuGen SDK integration is wired in (`SecuGenFingerprintDevice`,
`SecuGenFingerprintVerifier`, Release builds only — Debug still uses the
fakes for hardware-free local development). The following still requires
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

### ZK4500

A tenant configured for the `Zk4500` vendor can use the ZKFinger SDK
integration (`ZkFingerprintDevice`, `ZkFingerprintVerifier`, built via
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

**Known risk, not yet mitigated:** `zkfp2.DBMatch` (called from
`ZkFingerprintVerifier.Verify`) crashed the entire agent process with an
uncatchable `AccessViolationException` when given 2048 bytes of random
template data in place of a real template — confirmed against the real
native library, not simulated. `.NET` cannot safely catch or recover from
this; the whole kiosk process dies and needs an external restart. A
length/null check before the call would NOT have prevented this specific
crash (the random data was already a valid-length 2048-byte array), so no
defensive check was added — a check that doesn't stop the actual failure
mode would be false confidence. Realistic trigger: a stored template that
is corrupted in transit/storage, or a mismatch between a tenant's
configured `deviceVendor` and what a station actually captures with (see
open item 4 above and the `DeviceVendor` MSBuild property — nothing
today cross-checks that a station's compiled vendor matches the
templates its employees actually enrolled with). Properly closing this
gap needs either vendor-documented template format validation (not
available in this SDK's docs) or moving the match call out-of-process so
a crash there can't take the whole kiosk down — both are bigger than this
integration task's scope and are tracked as follow-up work.
