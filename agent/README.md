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

`FakeFingerprintDevice`/`FakeFingerprintVerifier` are registered by
default. To integrate a real scanner:

1. Install the vendor SDK (ZKFinger SDK for ZK4500, or the SecuGen FDx SDK
   Pro for Hamster Plus) and reference its .NET wrapper DLL from
   `AttendanceAgent.csproj`.
2. Implement `IFingerprintDevice`/`IFingerprintVerifier` against that SDK
   (e.g. `ZkFingerprintDevice`, `ZkFingerprintVerifier`), following the
   constraint in this plan's Global Constraints section: acquire the
   device handle only inside `Capture()`'s call path and release it in a
   `finally` — `DeviceCapture.CaptureOnce` already enforces this shape, so
   the real device only needs to implement the three interface methods
   correctly.
3. Swap the DI registrations in `App.xaml.cs` from the fakes to the real
   implementations.
4. Manually verify against physical hardware (this cannot be automated):
   - Enroll a fingerprint for a test employee via the enrollment flow.
   - Punch in with the correct finger — verify success and that the
     device LED/handle is released immediately after (no exclusive lock
     held between punches; confirm by running the vendor's own
     diagnostic tool concurrently and seeing it can still see the
     device).
   - Punch in with a different finger — verify rejection.
   - Disconnect the network, punch in/out several times, reconnect —
     verify the queued punches sync within 30 seconds and appear in the
     backend's daily attendance view exactly once each (no duplicates).
