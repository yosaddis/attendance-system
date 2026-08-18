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

SecuGen SDK integration is wired in (`SecuGenFingerprintDevice`,
`SecuGenFingerprintVerifier`, Release builds only — Debug still uses the
fakes for hardware-free local development). The following still requires
a physical SecuGen device (Hamster Plus or compatible FDx-family reader)
and cannot be automated:

1. Confirm `FIRTextData` is genuinely base64 (see the comment in
   `SecuGenFingerprintDevice.Capture()`) — capture a real fingerprint and
   verify `Convert.FromBase64String` doesn't throw. If it does, this is
   the SDK detail to fix first.
2. Enroll a fingerprint for a test employee via the enrollment flow.
3. Punch in with the correct finger — verify success and that the device
   is released immediately after each capture (no exclusive lock held
   between punches — confirm by running SecuGen's own diagnostic tool
   concurrently and seeing it can still see the device).
4. Punch in with a different finger — verify rejection.
5. Disconnect the network, punch in/out several times, reconnect — verify
   the queued punches sync within 30 seconds and appear in the backend's
   daily attendance view exactly once each (no duplicates).

A tenant configured for the `Zk4500` vendor still needs its own
`IFingerprintDevice`/`IFingerprintVerifier` pair (ZKFinger SDK) once that
vendor's SDK resources and hardware are available — same pattern as the
SecuGen integration above, just a different vendor SDK reference and
`Devices/` subfolder.
