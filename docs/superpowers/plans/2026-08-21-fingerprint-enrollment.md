# Fingerprint Enrollment (Desktop Agent) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a TenantAdmin-gated fingerprint enrollment flow to the desktop agent so a real employee template can be captured and uploaded without a direct API call — closing the last gap identified in the design spec (no product surface for enrollment existed anywhere).

**Architecture:** A new admin-gated "Enroll" panel on the existing punch window. Three raw enrollment-purpose captures are taken through a new `IFingerprintDevice.CaptureForEnrollment()` method, then folded into one final template through a new, per-vendor `IFingerprintEnroller.MergeCaptures()` — ZK4500 batches all three into one `DBMerge` call, SecuGen folds them incrementally via repeated `CreateTemplate` calls (these are genuinely different vendor operations, not the same call reused). The merged template uploads via the station's existing `X-Station-Key` auth to the already-built `POST /api/templates`.

**Amendment after Tasks 1-5 (whole-branch review):** Tasks 1-5 shipped with "No backend changes" as a hard constraint. A whole-branch review found that the admin gate checks `Role == "TenantAdmin"` only, not tenant identity — any tenant's TenantAdmin can unlock enrollment on any OTHER tenant's station (the upload itself stays correctly scoped to the station's own tenant via station-key auth, so cross-tenant employee data can't actually be corrupted, but the login gate itself doesn't enforce "this admin belongs to this station's company"). The user decided this must be fixed, which requires a small, additive backend change (Task 6) — the "No backend changes" constraint is explicitly amended for Tasks 6-7 only; it still holds for Tasks 1-5's own scope retroactively (they didn't need one).

**Tech Stack:** .NET 8 WPF desktop agent, CommunityToolkit.Mvvm, xUnit, existing ZKFinger/SecuGen vendor SDKs.

## Global Constraints

- Admin gate: a TenantAdmin login check (via the existing `POST /api/auth/login`), performed once per enrollment attempt, never persisted as a session. Only role `TenantAdmin` unlocks the Enroll panel.
- Enrollment mode is single-shot: after one enrollment completes (success or failure) or is cancelled, the UI returns to the Punch panel. No timed admin session.
- Enrollment requires exactly 3 finger placements, merged into one final template — this is a real per-vendor operation (ZK: `DBMerge`; SecuGen: iterative `CreateTemplate`), not the same call as a punch capture reused three times.
- The template upload uses the station's existing `X-Station-Key` header (same as every other backend call the agent makes) — the admin's JWT from the login check is discarded immediately, never stored or reused for the upload.
- No offline queueing for enrollment uploads (unlike punches) — on failure, the operator is standing at the kiosk and can just retry.
- Every new device/enroller class must satisfy `IFingerprintDevice`/`IFingerprintEnroller` exactly — no interface drift between vendors.
- Debug builds always use fakes (`FakeFingerprintDevice`, `FakeFingerprintEnroller`) regardless of the `DeviceVendor` MSBuild property, matching the existing convention for `IFingerprintDevice`/`IFingerprintVerifier`.

---

### Task 1: Backend API client — admin login and template upload

**Files:**
- Modify: `agent/src/AttendanceAgent/Api/IBackendApiClient.cs`
- Modify: `agent/src/AttendanceAgent/Api/ApiDtos.cs`
- Modify: `agent/src/AttendanceAgent/Api/BackendApiClient.cs`
- Modify: `agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs`
- Test: `agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs`

**Interfaces:**
- Consumes: nothing new — extends the existing `IBackendApiClient`/`BackendApiClient` from Backend Task 1-9 and Agent Task 2-3.
- Produces: `IBackendApiClient.LoginAsync(string email, string password, CancellationToken ct = default)` returning `Task<LoginResult?>` where `LoginResult(string Role)`; `IBackendApiClient.EnrollTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default)` returning `Task<bool>`. Later tasks (2, 3) consume both by name and exact signature.

- [ ] **Step 1: Write the failing tests**

```csharp
// Add to agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs, inside the existing class body

[Fact]
public async Task LoginAsync_Success_ReturnsRole()
{
    using var db = DbWithSettings();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { token = "abc", role = "TenantAdmin", tenantId = Guid.NewGuid() }),
    });
    var client = new BackendApiClient(new HttpClient(handler), db);

    var result = await client.LoginAsync("admin@acme.test", "correct-horse-battery");

    Assert.Equal("TenantAdmin", result!.Role);
    Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    Assert.EndsWith("/api/auth/login", handler.LastRequest!.RequestUri!.AbsolutePath);
}

[Fact]
public async Task LoginAsync_Unauthorized_ReturnsNull()
{
    using var db = DbWithSettings();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
    var client = new BackendApiClient(new HttpClient(handler), db);

    var result = await client.LoginAsync("admin@acme.test", "wrong-password");

    Assert.Null(result);
}

[Fact]
public async Task LoginAsync_PostsEmailAndPasswordAsJsonBody()
{
    using var db = DbWithSettings();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { token = "abc", role = "TenantAdmin" }),
    });
    var client = new BackendApiClient(new HttpClient(handler), db);

    await client.LoginAsync("admin@acme.test", "correct-horse-battery");

    var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
    using var json = JsonDocument.Parse(body);
    Assert.Equal("admin@acme.test", json.RootElement.GetProperty("Email").GetString());
    Assert.Equal("correct-horse-battery", json.RootElement.GetProperty("Password").GetString());
}

[Fact]
public async Task EnrollTemplateAsync_Success_ReturnsTrue_AndSendsStationKeyHeader()
{
    using var db = DbWithSettings();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
    var client = new BackendApiClient(new HttpClient(handler), db);

    var result = await client.EnrollTemplateAsync(Guid.NewGuid(), new byte[] { 1, 2, 3 });

    Assert.True(result);
    Assert.Equal("secret-key", handler.LastRequest!.Headers.GetValues("X-Station-Key").Single());
}

[Fact]
public async Task EnrollTemplateAsync_EncodesTemplateAsBase64InRequestBody()
{
    using var db = DbWithSettings();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
    var client = new BackendApiClient(new HttpClient(handler), db);
    var employeeId = Guid.NewGuid();

    await client.EnrollTemplateAsync(employeeId, new byte[] { 1, 2, 3 });

    var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
    using var json = JsonDocument.Parse(body);
    Assert.Equal(employeeId, json.RootElement.GetProperty("EmployeeId").GetGuid());
    Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), json.RootElement.GetProperty("TemplateData").GetString());
}

[Fact]
public async Task EnrollTemplateAsync_HttpFailure_ReturnsFalse()
{
    using var db = DbWithSettings();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
    var client = new BackendApiClient(new HttpClient(handler), db);

    var result = await client.EnrollTemplateAsync(Guid.NewGuid(), new byte[] { 1, 2, 3 });

    Assert.False(result);
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build error — `LoginAsync`/`EnrollTemplateAsync` do not exist on `BackendApiClient` yet.

- [ ] **Step 3: Add the DTOs**

```csharp
// Add to agent/src/AttendanceAgent/Api/ApiDtos.cs, alongside the existing records

public record LoginResult(string Role);

internal record LoginRequestPayload(string Email, string Password);
internal record EnrollTemplateRequestPayload(Guid EmployeeId, string TemplateData);
```

- [ ] **Step 4: Add the interface methods**

```csharp
// agent/src/AttendanceAgent/Api/IBackendApiClient.cs — full file
using AttendanceAgent.Data;

namespace AttendanceAgent.Api;

public interface IBackendApiClient
{
    Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default);
    Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default);
    Task<PunchBatchSubmitResult> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default);
    Task<LoginResult?> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<bool> EnrollTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement both methods**

```csharp
// Add to agent/src/AttendanceAgent/Api/BackendApiClient.cs, inside the class body

public async Task<LoginResult?> LoginAsync(string email, string password, CancellationToken ct = default)
{
    // Deliberately does NOT go through BuildRequestAsync — that helper always attaches
    // X-Station-Key, but /api/auth/login is the same public login endpoint the web portal
    // uses, not a station-scoped call. This is only ever used as a one-time admin-gate
    // check; the resulting JWT is not read from the response or stored anywhere.
    var settings = await _db.Settings.SingleAsync(ct);
    var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BackendBaseUrl), "/api/auth/login"))
    {
        Content = JsonContent.Create(new LoginRequestPayload(email, password), options: JsonOptions),
    };
    var response = await _http.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode) return null;
    return await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions, ct);
}

public async Task<bool> EnrollTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default)
{
    var request = await BuildRequestAsync(HttpMethod.Post, "/api/templates", ct);
    request.Content = JsonContent.Create(
        new EnrollTemplateRequestPayload(employeeId, Convert.ToBase64String(templateData)),
        options: JsonOptions);
    var response = await _http.SendAsync(request, ct);
    return response.IsSuccessStatusCode;
}
```

- [ ] **Step 6: Update the fake so the test project compiles**

```csharp
// agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs — full file
using AttendanceAgent.Api;
using AttendanceAgent.Data;

namespace AttendanceAgent.Tests;

public class FakeBackendApiClient : IBackendApiClient
{
    public EmployeeLookupResult? LookupResult { get; set; }
    public bool ThrowOnLookup { get; set; }
    public Func<Exception>? LookupExceptionToThrow { get; set; }
    public byte[]? TemplateResult { get; set; }
    public bool ThrowOnTemplate { get; set; }
    public Func<Exception>? TemplateExceptionToThrow { get; set; }
    public PunchBatchSubmitResult SubmitResult { get; set; } = PunchBatchSubmitResult.Accepted;
    public bool ThrowOnSubmit { get; set; }
    public Func<Exception>? SubmitExceptionToThrow { get; set; }
    public LoginResult? LoginResult { get; set; }
    public bool ThrowOnLogin { get; set; }
    public bool EnrollTemplateResult { get; set; } = true;
    public bool ThrowOnEnrollTemplate { get; set; }
    public (Guid EmployeeId, byte[] TemplateData)? LastEnrolledTemplate { get; private set; }

    public Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default)
    {
        if (LookupExceptionToThrow is not null) throw LookupExceptionToThrow();
        if (ThrowOnLookup) throw new HttpRequestException("offline");
        return Task.FromResult(LookupResult);
    }

    public Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (TemplateExceptionToThrow is not null) throw TemplateExceptionToThrow();
        if (ThrowOnTemplate) throw new HttpRequestException("offline");
        return Task.FromResult(TemplateResult);
    }

    public Task<PunchBatchSubmitResult> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default)
    {
        if (SubmitExceptionToThrow is not null) throw SubmitExceptionToThrow();
        if (ThrowOnSubmit) throw new HttpRequestException("offline");
        return Task.FromResult(SubmitResult);
    }

    public Task<LoginResult?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        if (ThrowOnLogin) throw new HttpRequestException("offline");
        return Task.FromResult(LoginResult);
    }

    public Task<bool> EnrollTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default)
    {
        if (ThrowOnEnrollTemplate) throw new HttpRequestException("offline");
        LastEnrolledTemplate = (employeeId, templateData);
        return Task.FromResult(EnrollTemplateResult);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: all tests pass, including the 6 new ones (56 existing + 6 = 62 total — confirm the exact count in your run's output).

- [ ] **Step 8: Commit**

```bash
git add agent/src/AttendanceAgent/Api agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs
git commit -m "feat: add admin login and template upload to the backend API client"
```

---

### Task 2: Enrollment interfaces, fakes, and `EnrollmentService`

**Files:**
- Modify: `agent/src/AttendanceAgent/Devices/IFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/IFingerprintEnroller.cs`
- Modify: `agent/src/AttendanceAgent/Devices/FakeFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/FakeFingerprintEnroller.cs`
- Create: `agent/src/AttendanceAgent/Services/IEnrollmentService.cs`
- Create: `agent/src/AttendanceAgent/Services/EnrollmentService.cs`
- Test: `agent/tests/AttendanceAgent.Tests/EnrollmentServiceTests.cs`

**Interfaces:**
- Consumes: `IEmployeeDirectoryService.ResolveAsync(string code, CancellationToken ct = default)` returning `Task<EmployeeLookupResult?>` (existing); `IBackendApiClient.EnrollTemplateAsync` (Task 1).
- Produces: `IFingerprintDevice.CaptureForEnrollment()` (new method on the existing interface — every implementation, including the two real vendors in Tasks 4-5, must add it); `IFingerprintEnroller.MergeCaptures(IReadOnlyList<byte[]> rawCaptures)`; `IEnrollmentService.EnrollAsync(string employeeCode, IFingerprintDevice device, IFingerprintEnroller enroller, Action<int,int> onCaptureProgress, CancellationToken ct = default)` returning `Task<EnrollmentResult>` where `EnrollmentResult(bool Success, string Message)`. Task 3 consumes `IEnrollmentService`/`EnrollmentResult` by these exact names.

- [ ] **Step 1: Write the failing tests**

```csharp
// agent/tests/AttendanceAgent.Tests/EnrollmentServiceTests.cs — full file
using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceAgent.Tests;

/// <summary>
/// FakeFingerprintDevice's single NextEnrollmentCapture property can't return a different byte
/// array per call, but EnrollAsync_ThreeSuccessfulCaptures_MergesAndUploads needs each of the 3
/// captures distinguishable to prove ordering is preserved into MergeCaptures.
/// </summary>
public class SequencedFakeFingerprintDevice : IFingerprintDevice
{
    private readonly Queue<byte[]> _captures;
    public bool IsAcquired { get; private set; }

    public SequencedFakeFingerprintDevice(Queue<byte[]> captures) => _captures = captures;

    public void Acquire() => IsAcquired = true;
    public byte[] Capture() => throw new NotSupportedException("Not used by enrollment.");
    public byte[] CaptureForEnrollment() => _captures.Dequeue();
    public void Release() => IsAcquired = false;
}

public class EnrollmentServiceTests
{
    [Fact]
    public async Task EnrollAsync_ThreeSuccessfulCaptures_MergesAndUploads()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var service = new EnrollmentService(employees, api);
        var captureQueue = new Queue<byte[]>(new[] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 } });
        var enroller = new FakeFingerprintEnroller { MergedResult = new byte[] { 9, 9, 9 } };
        var progressCalls = new List<(int, int)>();

        var result = await service.EnrollAsync("E002", new SequencedFakeFingerprintDevice(captureQueue), enroller,
            (i, total) => progressCalls.Add((i, total)));

        Assert.True(result.Success);
        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, progressCalls);
        Assert.Equal(3, enroller.LastCaptures!.Count);
        Assert.Equal(new byte[] { 1 }, enroller.LastCaptures![0]);
        Assert.Equal(new byte[] { 2 }, enroller.LastCaptures![1]);
        Assert.Equal(new byte[] { 3 }, enroller.LastCaptures![2]);
        Assert.Equal(employeeId, api.LastEnrolledTemplate!.Value.EmployeeId);
        Assert.Equal(new byte[] { 9, 9, 9 }, api.LastEnrolledTemplate!.Value.TemplateData);
    }

    [Fact]
    public async Task EnrollAsync_UnknownEmployeeCode_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient();
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var service = new EnrollmentService(employees, api);
        var device = new FakeFingerprintDevice();

        var result = await service.EnrollAsync("NOPE", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
        Assert.False(device.IsAcquired);
        Assert.Empty(device.CallLog);
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_CaptureFails_AbortsAndReleasesDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var service = new EnrollmentService(employees, api);
        var device = new FakeFingerprintDevice { ThrowOnCaptureForEnrollment = true };

        var result = await service.EnrollAsync("E002", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
        Assert.False(device.IsAcquired);
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_MergeFails_DoesNotUpload()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var service = new EnrollmentService(employees, api);
        var device = new FakeFingerprintDevice { NextEnrollmentCapture = new byte[] { 1 } };
        var enroller = new FakeFingerprintEnroller { ThrowOnMerge = true };

        var result = await service.EnrollAsync("E002", device, enroller, (_, _) => { });

        Assert.False(result.Success);
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_UploadFails_ReturnsFailureResult()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate"),
            EnrollTemplateResult = false,
        };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var service = new EnrollmentService(employees, api);
        var device = new FakeFingerprintDevice { NextEnrollmentCapture = new byte[] { 1 } };

        var result = await service.EnrollAsync("E002", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build error — `IFingerprintDevice.CaptureForEnrollment`, `IFingerprintEnroller`, `EnrollmentService`, `IEnrollmentService`, `EnrollmentResult`, and `FakeFingerprintEnroller.ThrowOnMerge`/`MergedResult`/`LastCaptures` don't exist yet.

- [ ] **Step 3: Extend `IFingerprintDevice` and add `IFingerprintEnroller`**

```csharp
// agent/src/AttendanceAgent/Devices/IFingerprintDevice.cs — full file
namespace AttendanceAgent.Devices;

public interface IFingerprintDevice
{
    void Acquire();
    byte[] Capture();
    byte[] CaptureForEnrollment();
    void Release();
}
```

```csharp
// agent/src/AttendanceAgent/Devices/IFingerprintEnroller.cs — new file
namespace AttendanceAgent.Devices;

/// <summary>
/// Folds the raw samples IFingerprintDevice.CaptureForEnrollment produced into one final
/// enrollment template. Implementations decide internally whether that's a single batch call
/// (ZK4500's DBMerge) or an iterative fold (SecuGen's repeated CreateTemplate) — callers don't
/// need to know which.
/// </summary>
public interface IFingerprintEnroller
{
    byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures);
}
```

- [ ] **Step 4: Update `FakeFingerprintDevice` and add `FakeFingerprintEnroller`**

```csharp
// agent/src/AttendanceAgent/Devices/FakeFingerprintDevice.cs — full file
namespace AttendanceAgent.Devices;

public class FakeFingerprintDevice : IFingerprintDevice
{
    public bool IsAcquired { get; private set; }
    public byte[] NextCapture { get; set; } = Array.Empty<byte>();
    public bool ThrowOnCapture { get; set; }
    public byte[] NextEnrollmentCapture { get; set; } = Array.Empty<byte>();
    public bool ThrowOnCaptureForEnrollment { get; set; }
    public List<string> CallLog { get; } = new();

    public void Acquire()
    {
        IsAcquired = true;
        CallLog.Add("Acquire");
    }

    public byte[] Capture()
    {
        CallLog.Add("Capture");
        if (ThrowOnCapture) throw new InvalidOperationException("No finger detected");
        return NextCapture;
    }

    public byte[] CaptureForEnrollment()
    {
        CallLog.Add("CaptureForEnrollment");
        if (ThrowOnCaptureForEnrollment) throw new InvalidOperationException("No finger detected");
        return NextEnrollmentCapture;
    }

    public void Release()
    {
        IsAcquired = false;
        CallLog.Add("Release");
    }
}
```

```csharp
// agent/src/AttendanceAgent/Devices/FakeFingerprintEnroller.cs — new file
namespace AttendanceAgent.Devices;

public class FakeFingerprintEnroller : IFingerprintEnroller
{
    public byte[] MergedResult { get; set; } = Array.Empty<byte>();
    public bool ThrowOnMerge { get; set; }
    public List<byte[]>? LastCaptures { get; private set; }

    public byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures)
    {
        LastCaptures = rawCaptures.ToList();
        if (ThrowOnMerge) throw new InvalidOperationException("Merge failed");
        return MergedResult;
    }
}
```

- [ ] **Step 5: Add `IEnrollmentService` and implement `EnrollmentService`**

```csharp
// agent/src/AttendanceAgent/Services/IEnrollmentService.cs — new file
using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public record EnrollmentResult(bool Success, string Message);

public interface IEnrollmentService
{
    Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default);
}
```

```csharp
// agent/src/AttendanceAgent/Services/EnrollmentService.cs — new file
using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public class EnrollmentService : IEnrollmentService
{
    private const int RequiredCaptureCount = 3;

    private readonly IEmployeeDirectoryService _employees;
    private readonly Api.IBackendApiClient _api;

    public EnrollmentService(IEmployeeDirectoryService employees, Api.IBackendApiClient api)
    {
        _employees = employees;
        _api = api;
    }

    public async Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default)
    {
        var employee = await _employees.ResolveAsync(employeeCode, ct);
        if (employee is null)
            return new EnrollmentResult(false, $"Employee code '{employeeCode}' not recognized.");

        // Mirrors DeviceCapture.CaptureOnce's safety contract: Acquire() runs OUTSIDE the
        // try/finally below, so a failed Acquire() (which some device implementations already
        // guarantee cleans up its own partial state before throwing — see
        // ZkFingerprintDevice.Acquire()) never triggers a Release() call on a device that was
        // never successfully opened.
        try
        {
            await Task.Run(() => device.Acquire(), ct);
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Failed to access fingerprint device: {ex.Message}");
        }

        var rawCaptures = new List<byte[]>();
        try
        {
            for (var i = 1; i <= RequiredCaptureCount; i++)
            {
                // Runs on the calling context (the WPF dispatcher, when called from
                // MainViewModel) because nothing in this method uses ConfigureAwait(false) —
                // each `await Task.Run(...)` below hops onto a thread-pool thread only for the
                // blocking device call itself, then resumes back here, so it's safe for the
                // caller to update UI-bound state directly inside onCaptureProgress.
                onCaptureProgress(i, RequiredCaptureCount);
                var capture = await Task.Run(() => device.CaptureForEnrollment(), ct);
                rawCaptures.Add(capture);
            }
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Fingerprint capture failed: {ex.Message}");
        }
        finally
        {
            await Task.Run(() => device.Release(), ct);
        }

        byte[] merged;
        try
        {
            merged = await Task.Run(() => enroller.MergeCaptures(rawCaptures), ct);
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Failed to build enrollment template: {ex.Message}");
        }

        var uploaded = await _api.EnrollTemplateAsync(employee.EmployeeId, merged, ct);
        return uploaded
            ? new EnrollmentResult(true, $"Fingerprint enrolled for {employee.Name}.")
            : new EnrollmentResult(false, "Failed to upload the enrolled template to the backend.");
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: all tests pass (previous total + 5 new `EnrollmentServiceTests`).

- [ ] **Step 7: Commit**

```bash
git add agent/src/AttendanceAgent/Devices agent/src/AttendanceAgent/Services/IEnrollmentService.cs agent/src/AttendanceAgent/Services/EnrollmentService.cs agent/tests/AttendanceAgent.Tests/EnrollmentServiceTests.cs
git commit -m "feat: add enrollment orchestration service with per-vendor merge abstraction"
```

---

### Task 3: Admin gate and Enroll UI in the desktop agent

**Files:**
- Create: `agent/src/AttendanceAgent/Services/IAdminCredentialPrompt.cs`
- Create: `agent/src/AttendanceAgent/Services/InputBoxAdminCredentialPrompt.cs`
- Modify: `agent/src/AttendanceAgent/ViewModels/MainViewModel.cs`
- Modify: `agent/src/AttendanceAgent/MainWindow.xaml`
- Modify: `agent/src/AttendanceAgent/HostComposition.cs`
- Test: `agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `IBackendApiClient.LoginAsync` (Task 1), `IEnrollmentService.EnrollAsync`/`EnrollmentResult` (Task 2), `IFingerprintDevice`/`IFingerprintEnroller` (existing DI registrations).
- Produces: `IAdminCredentialPrompt.PromptForCredentials()` returning `(string Email, string Password)?` — exists purely so `MainViewModel` never calls a real blocking modal dialog directly, which would make it untestable (a unit test invoking a real `Microsoft.VisualBasic.Interaction.InputBox` call would hang waiting for real user input). `MainWindow`/DI wiring for `MainViewModel`'s two new bool-free `Visibility` properties.

- [ ] **Step 1: Write the failing tests**

```csharp
// Add to agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs, inside the existing class body
// (add `using System.Windows;` and `using AttendanceAgent.Api;` at the top of the file alongside the existing usings)

[Fact]
public async Task AdminLogin_CorrectTenantAdminCredentials_SwitchesToEnrollPanel()
{
    var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin") };
    var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

    await vm.AdminLoginCommand.ExecuteAsync(null);

    Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
    Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
}

[Fact]
public async Task AdminLogin_WrongRole_StaysOnPunchPanelWithError()
{
    var api = new FakeBackendApiClient { LoginResult = new LoginResult("Operator") };
    var prompt = new FakeAdminCredentialPrompt { Result = ("operator@zak.local", "ChangeMe123!") };
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

    await vm.AdminLoginCommand.ExecuteAsync(null);

    Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
    Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
    Assert.Equal("Admin login failed.", vm.StatusMessage);
}

[Fact]
public async Task AdminLogin_InvalidCredentials_StaysOnPunchPanelWithError()
{
    var api = new FakeBackendApiClient { LoginResult = null };
    var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "wrong-password") };
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

    await vm.AdminLoginCommand.ExecuteAsync(null);

    Assert.Equal("Admin login failed.", vm.StatusMessage);
}

[Fact]
public async Task AdminLogin_PromptCancelled_DoesNothing()
{
    var api = new FakeBackendApiClient();
    var prompt = new FakeAdminCredentialPrompt { Result = null };
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

    await vm.AdminLoginCommand.ExecuteAsync(null);

    Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
    Assert.Equal("", vm.StatusMessage);
}

[Fact]
public async Task StartEnrollment_OnSuccess_ReturnsToPunchPanelWithMessage()
{
    var enrollment = new FakeEnrollmentService { Result = new EnrollmentResult(true, "Fingerprint enrolled for Yoseph Addisu Abate.") };
    var vm = new MainViewModel(new FakeCaptureService(), enrollment, new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
    vm.EnrollEmployeeCode = "E002";

    await vm.StartEnrollmentCommand.ExecuteAsync(null);

    Assert.Equal("Fingerprint enrolled for Yoseph Addisu Abate.", vm.StatusMessage);
    Assert.Equal("", vm.EnrollEmployeeCode);
    Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
    Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
}

[Fact]
public void CancelEnrollment_ReturnsToPunchPanel()
{
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), new FakeBackendApiClient(), new FakeFingerprintDevice(), new FakeFingerprintEnroller(), new FakeAdminCredentialPrompt());
    vm.EnrollEmployeeCode = "E002";
    vm.EnrollPanelVisibility = Visibility.Visible;
    vm.PunchPanelVisibility = Visibility.Collapsed;

    vm.CancelEnrollmentCommand.Execute(null);

    Assert.Equal("", vm.EnrollEmployeeCode);
    Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
    Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
}
```

Add these two new test doubles as separate files (mirroring `FakeCaptureService.cs`'s existing one-fake-per-file convention):

```csharp
// agent/tests/AttendanceAgent.Tests/FakeEnrollmentService.cs — new file
using AttendanceAgent.Devices;
using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeEnrollmentService : IEnrollmentService
{
    public EnrollmentResult Result { get; set; } = new(true, "ok");

    public Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default) =>
        Task.FromResult(Result);
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/FakeAdminCredentialPrompt.cs — new file
using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeAdminCredentialPrompt : IAdminCredentialPrompt
{
    public (string Email, string Password)? Result { get; set; }

    public (string Email, string Password)? PromptForCredentials() => Result;
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build error — `IAdminCredentialPrompt`, the new `MainViewModel` constructor parameters/properties/commands, and `LoginResult`'s single-arg constructor usage don't exist yet in this shape.

- [ ] **Step 3: Add `IAdminCredentialPrompt` and its real implementation**

```csharp
// agent/src/AttendanceAgent/Services/IAdminCredentialPrompt.cs — new file
namespace AttendanceAgent.Services;

/// <summary>
/// Exists so MainViewModel never calls a real blocking modal dialog directly — a unit test
/// invoking a real Microsoft.VisualBasic.Interaction.InputBox call would hang waiting for actual
/// user input. Returns null if the operator leaves either field blank (treated as "cancelled").
/// </summary>
public interface IAdminCredentialPrompt
{
    (string Email, string Password)? PromptForCredentials();
}
```

```csharp
// agent/src/AttendanceAgent/Services/InputBoxAdminCredentialPrompt.cs — new file
namespace AttendanceAgent.Services;

public class InputBoxAdminCredentialPrompt : IAdminCredentialPrompt
{
    public (string Email, string Password)? PromptForCredentials()
    {
        var email = Microsoft.VisualBasic.Interaction.InputBox("Admin email:", "Admin login");
        if (string.IsNullOrWhiteSpace(email)) return null;

        var password = Microsoft.VisualBasic.Interaction.InputBox("Admin password:", "Admin login");
        if (string.IsNullOrWhiteSpace(password)) return null;

        return (email, password);
    }
}
```

- [ ] **Step 4: Update `MainViewModel`**

```csharp
// agent/src/AttendanceAgent/ViewModels/MainViewModel.cs — full file
using System.Windows;
using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AttendanceAgent.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPunchCaptureService _captureService;
    private readonly IEnrollmentService _enrollmentService;
    private readonly IBackendApiClient _api;
    private readonly IFingerprintDevice _device;
    private readonly IFingerprintEnroller _enroller;
    private readonly IAdminCredentialPrompt _prompt;

    [ObservableProperty]
    private string employeeCode = "";

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private string enrollEmployeeCode = "";

    [ObservableProperty]
    private string enrollProgressMessage = "";

    [ObservableProperty]
    private Visibility punchPanelVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility enrollPanelVisibility = Visibility.Collapsed;

    public MainViewModel(
        IPunchCaptureService captureService,
        IEnrollmentService enrollmentService,
        IBackendApiClient api,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        IAdminCredentialPrompt prompt)
    {
        _captureService = captureService;
        _enrollmentService = enrollmentService;
        _api = api;
        _device = device;
        _enroller = enroller;
        _prompt = prompt;
    }

    [RelayCommand]
    private async Task PunchAsync(string punchType)
    {
        var result = await _captureService.CapturePunchAsync(EmployeeCode, punchType, _device);
        StatusMessage = result.Message;
        if (result.Success) EmployeeCode = "";
    }

    [RelayCommand]
    private async Task AdminLoginAsync()
    {
        var credentials = _prompt.PromptForCredentials();
        if (credentials is null) return;

        var login = await _api.LoginAsync(credentials.Value.Email, credentials.Value.Password);
        if (login is null || login.Role != "TenantAdmin")
        {
            StatusMessage = "Admin login failed.";
            return;
        }

        StatusMessage = "";
        PunchPanelVisibility = Visibility.Collapsed;
        EnrollPanelVisibility = Visibility.Visible;
    }

    [RelayCommand]
    private async Task StartEnrollmentAsync()
    {
        var result = await _enrollmentService.EnrollAsync(
            EnrollEmployeeCode,
            _device,
            _enroller,
            (i, total) => EnrollProgressMessage = $"Place your finger ({i} of {total})");

        StatusMessage = result.Message;
        EnrollEmployeeCode = "";
        EnrollProgressMessage = "";
        PunchPanelVisibility = Visibility.Visible;
        EnrollPanelVisibility = Visibility.Collapsed;
    }

    [RelayCommand]
    private void CancelEnrollment()
    {
        EnrollEmployeeCode = "";
        EnrollProgressMessage = "";
        PunchPanelVisibility = Visibility.Visible;
        EnrollPanelVisibility = Visibility.Collapsed;
    }
}
```

- [ ] **Step 5: Update `MainWindow.xaml`**

```xml
<!-- agent/src/AttendanceAgent/MainWindow.xaml — full file -->
<Window x:Class="AttendanceAgent.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ZAK Attendance" Height="320" Width="420">
    <Grid>
        <StackPanel Margin="16" Visibility="{Binding PunchPanelVisibility}">
            <TextBlock Text="Employee ID / PIN" Margin="0,0,0,4" />
            <TextBox Text="{Binding EmployeeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />
            <WrapPanel>
                <Button Content="IN" Command="{Binding PunchCommand}" CommandParameter="In" Margin="0,0,8,0" />
                <Button Content="BREAK OUT" Command="{Binding PunchCommand}" CommandParameter="BreakOut" Margin="0,0,8,0" />
                <Button Content="BREAK IN" Command="{Binding PunchCommand}" CommandParameter="BreakIn" Margin="0,0,8,0" />
                <Button Content="OUT" Command="{Binding PunchCommand}" CommandParameter="Out" />
            </WrapPanel>
            <Button Content="Admin" Command="{Binding AdminLoginCommand}" Margin="0,12,0,0" HorizontalAlignment="Left" />
            <TextBlock Text="{Binding StatusMessage}" Margin="0,16,0,0" TextWrapping="Wrap" />
        </StackPanel>
        <StackPanel Margin="16" Visibility="{Binding EnrollPanelVisibility}">
            <TextBlock Text="Enroll fingerprint — Employee ID / PIN" Margin="0,0,0,4" />
            <TextBox Text="{Binding EnrollEmployeeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />
            <WrapPanel>
                <Button Content="Start Enrollment" Command="{Binding StartEnrollmentCommand}" Margin="0,0,8,0" />
                <Button Content="Cancel" Command="{Binding CancelEnrollmentCommand}" />
            </WrapPanel>
            <TextBlock Text="{Binding EnrollProgressMessage}" Margin="0,16,0,0" TextWrapping="Wrap" />
            <TextBlock Text="{Binding StatusMessage}" Margin="0,8,0,0" TextWrapping="Wrap" />
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 6: Register the new services in `HostComposition`**

```csharp
// In agent/src/AttendanceAgent/HostComposition.cs, inside ConfigureServices —
// add these two lines directly after the existing `services.AddScoped<IPunchCaptureService, PunchCaptureService>();` line:
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddSingleton<IAdminCredentialPrompt, InputBoxAdminCredentialPrompt>();
```

```csharp
// In the same file, extend the existing #if DEBUG / #elif DEVICE_VENDOR_ZK4500 / #else block —
// add ONLY the #if DEBUG line for now (the #elif/#else branches reference
// ZkFingerprintEnroller/SecuGenFingerprintEnroller, which don't exist until Tasks 4-5):
#if DEBUG
        services.AddSingleton<IFingerprintDevice, FakeFingerprintDevice>();
        services.AddSingleton<IFingerprintVerifier, FakeFingerprintVerifier>();
        services.AddSingleton<IFingerprintEnroller, FakeFingerprintEnroller>();
#elif DEVICE_VENDOR_ZK4500
        services.AddSingleton<IFingerprintDevice, AttendanceAgent.Devices.Zk.ZkFingerprintDevice>();
        services.AddSingleton<IFingerprintVerifier, AttendanceAgent.Devices.Zk.ZkFingerprintVerifier>();
#else
        services.AddSingleton<IFingerprintDevice, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintDevice>();
        services.AddSingleton<IFingerprintVerifier, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintVerifier>();
#endif
```

Note: after this step, `-p:DeviceVendor=Zk4500 -c Release` and the default `-c Release` (SecuGen) builds will compile fine (DI registration gaps are a runtime concern, not a compile error) but would fail to *resolve* `MainViewModel` if actually run — that gap closes in Tasks 4 and 5, which each add their own vendor's `IFingerprintEnroller` registration line. `dotnet test` (Debug config, the check this task actually runs) is fully covered by the `#if DEBUG` line above.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: all tests pass, including the 6 new `MainViewModelTests` and the existing `HostCompositionTests.Build_And_Resolve_MainWindow_And_MainViewModel_From_A_Scope_Succeeds` (which now also validates `IEnrollmentService`/`IAdminCredentialPrompt`/`IFingerprintEnroller` resolve correctly through the full DI graph, since it builds with `ValidateOnBuild = true`).

- [ ] **Step 8: Commit**

```bash
git add agent/src/AttendanceAgent/Services/IAdminCredentialPrompt.cs agent/src/AttendanceAgent/Services/InputBoxAdminCredentialPrompt.cs agent/src/AttendanceAgent/ViewModels/MainViewModel.cs agent/src/AttendanceAgent/MainWindow.xaml agent/src/AttendanceAgent/HostComposition.cs agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs agent/tests/AttendanceAgent.Tests/FakeEnrollmentService.cs agent/tests/AttendanceAgent.Tests/FakeAdminCredentialPrompt.cs
git commit -m "feat: add admin-gated enrollment panel to the desktop agent UI"
```

---

### Task 4: Real ZK4500 enrollment capture and merge

**Files:**
- Modify: `agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintEnroller.cs`
- Modify: `agent/src/AttendanceAgent/HostComposition.cs`
- Modify: `agent/tests/AttendanceAgent.Tests/ZkFingerprintDeviceRegistrationTests.cs`
- Modify: `agent/README.md`

**Interfaces:**
- Consumes: `IFingerprintDevice.CaptureForEnrollment()`, `IFingerprintEnroller.MergeCaptures()` (Task 2).
- Produces: `ZkFingerprintDevice.CaptureForEnrollment()` (real ZK4500 implementation), `ZkFingerprintEnroller : IFingerprintEnroller`. No new public surface beyond satisfying the existing interfaces.

- [ ] **Step 1: Add a registration test for `ZkFingerprintEnroller`**

```csharp
// Add to agent/tests/AttendanceAgent.Tests/ZkFingerprintDeviceRegistrationTests.cs, inside the existing class body

[Fact]
public void ZkFingerprintEnroller_ImplementsIFingerprintEnroller()
{
    Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintEnroller>(
        (object)Activator.CreateInstance(typeof(ZkFingerprintEnroller))!);
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build error — `ZkFingerprintEnroller` doesn't exist yet, and `ZkFingerprintDevice` doesn't yet implement `CaptureForEnrollment()` (it will fail with a "does not implement interface member" error the moment Task 2's interface change reaches this class — this task is what resolves it).

- [ ] **Step 3: Add `CaptureForEnrollment()` to `ZkFingerprintDevice`**

```csharp
// Add to agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintDevice.cs, directly after the existing Capture() method

    // ZK's SDK has no capture-purpose distinction (confirmed via reflection over
    // libzkfpcsharp.dll — there is no separate "enroll" acquisition primitive, only
    // AcquireFingerprint), unlike SecuGen's dedicated Enroll() call. A punch capture and an
    // enrollment capture are the exact same underlying operation for this vendor.
    public byte[] CaptureForEnrollment() => Capture();
```

- [ ] **Step 4: Implement `ZkFingerprintEnroller`**

```csharp
// agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintEnroller.cs — new file
using libzkfpcsharp;

namespace AttendanceAgent.Devices.Zk;

public class ZkFingerprintEnroller : IFingerprintEnroller
{
    private const int MergedTemplateBufferSize = 2048;

    public byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures)
    {
        if (rawCaptures.Count != 3)
            throw new ArgumentException($"ZKFinger DBMerge requires exactly 3 captures, got {rawCaptures.Count}.", nameof(rawCaptures));

        // Mirrors ZkFingerprintVerifier's independent init/cleanup pattern: DBMerge is pure
        // software (needs the algorithm library initialized and a DB handle) but does NOT need
        // an open device, and this runs after ZkFingerprintDevice has already been released.
        var initErr = zkfp2.Init();
        if (initErr != zkfperrdef.ZKFP_ERR_OK && initErr != zkfperrdef.ZKFP_ERR_ALREADY_INIT)
            throw new InvalidOperationException($"ZKFinger algorithm init failed (Init: {initErr}).");

        var dbHandle = zkfp2.DBInit();
        if (dbHandle == IntPtr.Zero)
        {
            zkfp2.Terminate();
            throw new InvalidOperationException("ZKFinger DBInit failed.");
        }

        try
        {
            var merged = new byte[MergedTemplateBufferSize];
            var mergedLen = merged.Length;
            var err = zkfp2.DBMerge(dbHandle, rawCaptures[0], rawCaptures[1], rawCaptures[2], merged, ref mergedLen);
            if (err != zkfperrdef.ZKFP_ERR_OK)
                throw new InvalidOperationException($"Fingerprint template merge failed (DBMerge: {err}).");

            var result = new byte[mergedLen];
            Array.Copy(merged, result, mergedLen);
            return result;
        }
        finally
        {
            zkfp2.DBFree(dbHandle);
            zkfp2.Terminate();
        }
    }
}
```

- [ ] **Step 5: Register `ZkFingerprintEnroller` in `HostComposition`**

```csharp
// In agent/src/AttendanceAgent/HostComposition.cs, add ONE line to the #elif DEVICE_VENDOR_ZK4500 branch:
#elif DEVICE_VENDOR_ZK4500
        services.AddSingleton<IFingerprintDevice, AttendanceAgent.Devices.Zk.ZkFingerprintDevice>();
        services.AddSingleton<IFingerprintVerifier, AttendanceAgent.Devices.Zk.ZkFingerprintVerifier>();
        services.AddSingleton<IFingerprintEnroller, AttendanceAgent.Devices.Zk.ZkFingerprintEnroller>();
```

- [ ] **Step 6: Run the full suite and both Release configurations**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: all tests pass, including the new `ZkFingerprintEnroller_ImplementsIFingerprintEnroller`.

Run: `dotnet build agent/AttendanceAgent.sln -c Release` (default, SecuGen)
Expected: succeeds — this branch doesn't reference `ZkFingerprintEnroller` at all.

Run: `dotnet build agent/AttendanceAgent.sln -c Release -p:DeviceVendor=Zk4500`
Expected: succeeds — `IFingerprintEnroller` now resolves in this configuration too.

- [ ] **Step 7: Update `agent/README.md`'s ZK4500 bring-up section**

Find the existing numbered ZK4500 bring-up list (added during the ZK hardware integration branch) and add these two items, following the same "don't assume, verify" style as the existing entries:

```markdown
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
```

- [ ] **Step 8: Commit**

```bash
git add agent/src/AttendanceAgent/Devices/Zk agent/src/AttendanceAgent/HostComposition.cs agent/tests/AttendanceAgent.Tests/ZkFingerprintDeviceRegistrationTests.cs agent/README.md
git commit -m "feat: implement real ZK4500 enrollment capture and template merge"
```

---

### Task 5: Real SecuGen enrollment capture and merge

**Files:**
- Modify: `agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintEnroller.cs`
- Modify: `agent/src/AttendanceAgent/HostComposition.cs`
- Modify: `agent/tests/AttendanceAgent.Tests/SecuGenFingerprintDeviceRegistrationTests.cs`
- Modify: `agent/README.md`

**Interfaces:**
- Consumes: `IFingerprintDevice.CaptureForEnrollment()`, `IFingerprintEnroller.MergeCaptures()` (Task 2); `SecuGenFirTextEncoding.ToBytes`/`ToFirText` (existing, from the SecuGen hardware integration branch).
- Produces: `SecuGenFingerprintDevice.CaptureForEnrollment()`, `SecuGenFingerprintEnroller : IFingerprintEnroller`. No new public surface beyond satisfying the existing interfaces.

- [ ] **Step 1: Add a registration test for `SecuGenFingerprintEnroller`**

```csharp
// Add to agent/tests/AttendanceAgent.Tests/SecuGenFingerprintDeviceRegistrationTests.cs, inside the existing class body

[Fact]
public void SecuGenFingerprintEnroller_ImplementsIFingerprintEnroller()
{
    Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintEnroller>(
        (object)Activator.CreateInstance(typeof(SecuGenFingerprintEnroller))!);
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build error — `SecuGenFingerprintEnroller` doesn't exist yet, and `SecuGenFingerprintDevice` doesn't yet implement `CaptureForEnrollment()`.

- [ ] **Step 3: Extract the kiosk capture-window setup and add `CaptureForEnrollment()` to `SecuGenFingerprintDevice`**

`Capture()` and the new `CaptureForEnrollment()` both need the same kiosk-mode window settings (no popup, no on-screen preview) before calling into the SDK — extract that duplication into a private helper rather than repeating the three lines:

```csharp
// agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintDevice.cs — full file
using SecuGen.SecuBSPPro.Windows;

namespace AttendanceAgent.Devices.SecuGen;

public class SecuGenFingerprintDevice : IFingerprintDevice
{
    private readonly SecuBSPMx _secuBsp = new();

    public void Acquire()
    {
        var enumErr = _secuBsp.EnumerateDevice();
        if (enumErr != BSPError.ERROR_NONE || _secuBsp.DeviceNum == 0)
            throw new InvalidOperationException($"No SecuGen device found (EnumerateDevice: {enumErr}).");

        _secuBsp.DeviceID = _secuBsp.GetDeviceID(0);

        var openErr = _secuBsp.OpenDevice();
        if (openErr != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Failed to open SecuGen device (OpenDevice: {openErr}).");

        // CONFIRMED against real hardware: the SDK's default DefaultTimeout
        // (10000ms) is too short for reliable capture in practice — a
        // Capture(FIRPurpose.VERIFY) call failed twice with
        // ERROR_CAPTURE_TIMEOUT even with a finger presented promptly.
        // Bumping to 15000ms made capture succeed reliably. This is a soft,
        // best-effort tuning — don't fail Acquire() if SetInitInfo itself
        // returns a non-ERROR_NONE code.
        var initInfo = new BSPInitInfo();
        _secuBsp.GetInitInfo(initInfo);
        initInfo.DefaultTimeout = 15000;
        _secuBsp.SetInitInfo(initInfo);
    }

    public byte[] Capture()
    {
        ConfigureKioskCaptureWindow();

        var err = _secuBsp.Capture(FIRPurpose.VERIFY);
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint capture failed (Capture: {err}).");

        return SecuGenFirTextEncoding.ToBytes(_secuBsp.FIRTextData);
    }

    public byte[] CaptureForEnrollment()
    {
        ConfigureKioskCaptureWindow();

        // Enroll() is a genuinely different vendor call from Capture(FIRPurpose.VERIFY) —
        // confirmed by reading the vendor's own mainform.cs demo, which uses Enroll() only for
        // its enrollment flow and Capture(FIRPurpose.VERIFY) only for its verify flow. The
        // empty string is the payload parameter (SecuGen's Enroll/CreateTemplate can embed an
        // arbitrary string into the resulting FIR) — this system tracks employee identity
        // entirely in its own backend, so no vendor-side payload is used.
        var err = _secuBsp.Enroll("");
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint enrollment capture failed (Enroll: {err}).");

        return SecuGenFirTextEncoding.ToBytes(_secuBsp.FIRTextData);
    }

    public void Release()
    {
        _secuBsp.CloseDevice();
    }

    // Kiosk mode: no popup window, no on-screen fingerprint preview — this station has no
    // operator watching a capture dialog. Shared by both Capture() and CaptureForEnrollment(),
    // which both need the exact same window suppression.
    private void ConfigureKioskCaptureWindow()
    {
        _secuBsp.CaptureWindowOption.WindowStyle = (int)WindowStyle.INVISIBLE;
        _secuBsp.CaptureWindowOption.ShowFPImage = false;
        _secuBsp.CaptureWindowOption.FingerWindow = IntPtr.Zero;
    }
}
```

- [ ] **Step 4: Implement `SecuGenFingerprintEnroller`**

```csharp
// agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintEnroller.cs — new file
using SecuGen.SecuBSPPro.Windows;

namespace AttendanceAgent.Devices.SecuGen;

public class SecuGenFingerprintEnroller : IFingerprintEnroller
{
    public byte[] MergeCaptures(IReadOnlyList<byte[]> rawCaptures)
    {
        if (rawCaptures.Count == 0)
            throw new ArgumentException("At least one capture is required.", nameof(rawCaptures));

        // CreateTemplate compares two already-captured FIRs purely in software — it does not
        // need an open device, so this creates its own short-lived SecuBSPMx instance rather
        // than sharing one with SecuGenFingerprintDevice, mirroring
        // SecuGenFingerprintVerifier's existing pattern.
        using var secuBsp = new SecuBSPMx();

        // Unlike ZK's single DBMerge(t1, t2, t3) batch call, SecuGen folds incrementally: the
        // first capture's FIR becomes the running merged FIR directly, and each subsequent
        // capture is folded into it one at a time via CreateTemplate(nextFir, runningMergedFir,
        // payload) — confirmed by reading the vendor's own mainform.cs demo. The empty string is
        // the payload parameter; this system does not use vendor-side payload embedding.
        var mergedFir = SecuGenFirTextEncoding.ToFirText(rawCaptures[0]);
        for (var i = 1; i < rawCaptures.Count; i++)
        {
            var nextFir = SecuGenFirTextEncoding.ToFirText(rawCaptures[i]);
            var err = secuBsp.CreateTemplate(nextFir, mergedFir, "");
            if (err != BSPError.ERROR_NONE)
                throw new InvalidOperationException($"Fingerprint template merge failed (CreateTemplate: {err}).");
            mergedFir = secuBsp.FIRTextData;
        }

        return SecuGenFirTextEncoding.ToBytes(mergedFir);
    }
}
```

- [ ] **Step 5: Register `SecuGenFingerprintEnroller` in `HostComposition`**

```csharp
// In agent/src/AttendanceAgent/HostComposition.cs, add ONE line to the #else branch:
#else
        services.AddSingleton<IFingerprintDevice, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintDevice>();
        services.AddSingleton<IFingerprintVerifier, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintVerifier>();
        services.AddSingleton<IFingerprintEnroller, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintEnroller>();
#endif
```

- [ ] **Step 6: Run the full suite and both Release configurations**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: all tests pass, including the new `SecuGenFingerprintEnroller_ImplementsIFingerprintEnroller`.

Run: `dotnet build agent/AttendanceAgent.sln -c Release` (default, SecuGen)
Expected: succeeds — `IFingerprintEnroller` now resolves in this configuration too.

Run: `dotnet build agent/AttendanceAgent.sln -c Release -p:DeviceVendor=Zk4500`
Expected: succeeds — unaffected by this task's SecuGen-only changes.

- [ ] **Step 7: Update `agent/README.md`'s SecuGen bring-up section**

Find the existing numbered SecuGen bring-up list and add:

```markdown
6. **Enrollment via `Enroll()`, not `Capture()`.** Confirm the vendor's `Enroll("")` call
   works reliably in kiosk mode (`ConfigureKioskCaptureWindow`'s settings) the same way
   `Capture(FIRPurpose.VERIFY)` already does — this is a different vendor call, not the same
   one Task 10 already hardware-verified, so its own timeout/window behavior needs its own
   confirmation rather than being assumed identical.
7. **Iterative merge quality.** `SecuGenFingerprintEnroller.MergeCaptures` folds captures one
   at a time via `CreateTemplate`. Confirm against real hardware that three genuine
   placements of the same finger fold into a template that later verifies correctly through
   the existing punch flow.
```

- [ ] **Step 8: Commit**

```bash
git add agent/src/AttendanceAgent/Devices/SecuGen agent/src/AttendanceAgent/HostComposition.cs agent/tests/AttendanceAgent.Tests/SecuGenFingerprintDeviceRegistrationTests.cs agent/README.md
git commit -m "feat: implement real SecuGen enrollment capture and template merge"
```

---

### Task 6: Backend — expose the station's own tenant on the health endpoint

**Files:**
- Create: `backend/src/AttendanceApi/Dtos/HealthDtos.cs`
- Modify: `backend/src/AttendanceApi/Controllers/HealthController.cs`
- Test: `backend/tests/AttendanceApi.Tests/HealthControllerTests.cs`

**Interfaces:**
- Consumes: `StationKeySchemes.Name` auth scheme (existing), `ClaimsPrincipalExtensions.TenantId()`/`StationId()` (existing).
- Produces: `GET /api/health/station` now returns a typed `StationHealthResponse(string Status, Guid StationId, Guid TenantId)` instead of an anonymous object — Task 7 consumes the `TenantId` field by this exact name.

- [ ] **Step 1: Write the failing test**

```csharp
// backend/tests/AttendanceApi.Tests/HealthControllerTests.cs — new file
using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class HealthControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetForStation_ReturnsStationIdAndTenantId()
    {
        Guid tenantId, stationId;
        string stationKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var (plaintextKey, hash) = StationKeyGenerator.Generate();
            var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = hash };
            db.Tenants.Add(tenant);
            db.Stations.Add(station);
            db.SaveChanges();
            tenantId = tenant.Id;
            stationId = station.Id;
            stationKey = plaintextKey;
        }
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/health/station");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StationHealthResponse>();
        Assert.Equal(tenantId, body!.TenantId);
        Assert.Equal(stationId, body.StationId);
        Assert.Equal("ok", body.Status);
    }

    [Fact]
    public async Task GetForStation_WithoutStationKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health/station");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter HealthControllerTests`
Expected: `GetForStation_ReturnsStationIdAndTenantId` fails — `ReadFromJsonAsync<StationHealthResponse>` either fails to find the type (doesn't exist yet) or deserializes with `TenantId`/`StationId` as `Guid.Empty` (the anonymous object's `stationId` property is currently typed as `Guid?`, not a matching shape).

- [ ] **Step 3: Add the DTO and update the controller**

```csharp
// backend/src/AttendanceApi/Dtos/HealthDtos.cs — new file
namespace AttendanceApi.Dtos;

public record StationHealthResponse(string Status, Guid StationId, Guid TenantId);
```

```csharp
// backend/src/AttendanceApi/Controllers/HealthController.cs — full file
using AttendanceApi.Auth;
using AttendanceApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok" });

    [HttpGet("station")]
    [Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
    public ActionResult<StationHealthResponse> GetForStation() =>
        Ok(new StationHealthResponse("ok", User.StationId()!.Value, User.TenantId()!.Value));
}
```

`User.StationId()!.Value`/`User.TenantId()!.Value` are safe to force-unwrap here: the `[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]` guarantees `StationKeyAuthHandler` already populated both claims (see `Auth/StationKeyAuthHandler.cs`) before this action can run.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests`
Expected: all tests pass, including the 2 new `HealthControllerTests`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/AttendanceApi/Dtos/HealthDtos.cs backend/src/AttendanceApi/Controllers/HealthController.cs backend/tests/AttendanceApi.Tests/HealthControllerTests.cs
git commit -m "feat: expose the station's own tenant id on the station health endpoint"
```

---

### Task 7: Agent — reject admin logins from a different tenant than the station's own

**Files:**
- Modify: `agent/src/AttendanceAgent/Api/ApiDtos.cs`
- Modify: `agent/src/AttendanceAgent/Api/IBackendApiClient.cs`
- Modify: `agent/src/AttendanceAgent/Api/BackendApiClient.cs`
- Modify: `agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs`
- Modify: `agent/src/AttendanceAgent/ViewModels/MainViewModel.cs`
- Test: `agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs`
- Test: `agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `GET /api/health/station`'s new `TenantId` field (Task 6).
- Produces: `LoginResult` gains a second field, `TenantId` (default `null`, so every existing `new LoginResult("TenantAdmin")` call site across Tasks 1-5's tests keeps compiling unchanged); `IBackendApiClient.GetStationTenantIdAsync(CancellationToken ct = default)` returning `Task<Guid?>`.

**Design note — same error message either way.** A wrong password, a non-admin role, and a tenant mismatch all produce the identical `"Admin login failed."` message. Don't give a would-be attacker a more specific reason (e.g. "wrong company") to distinguish a valid-but-wrong-tenant admin account from an outright invalid one.

**Breaking change you must handle:** the 4 existing `AdminLogin_*` tests in `MainViewModelTests.cs` that expect a successful login (`AdminLogin_CorrectTenantAdminCredentials_SwitchesToEnrollPanel`) construct `new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin") }` with no tenant configured. Once this task's tenant check goes in, `FakeBackendApiClient.GetStationTenantIdAsync` must be given a matching `StationTenantId` in each of those tests, or they'll now fail (an admin login with no determinable station tenant is treated as untrusted, fails closed) — update them, don't loosen the production check to make them pass unmodified.

- [ ] **Step 1: Write the failing tests**

```csharp
// Add to agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs, inside the existing class body

[Fact]
public async Task GetStationTenantIdAsync_Success_ReturnsTenantId()
{
    using var db = DbWithSettings();
    var tenantId = Guid.NewGuid();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { status = "ok", stationId = Guid.NewGuid(), tenantId }),
    });
    var client = new BackendApiClient(new HttpClient(handler), db);

    var result = await client.GetStationTenantIdAsync();

    Assert.Equal(tenantId, result);
    Assert.Equal("secret-key", handler.LastRequest!.Headers.GetValues("X-Station-Key").Single());
}

[Fact]
public async Task GetStationTenantIdAsync_HttpFailure_ReturnsNull()
{
    using var db = DbWithSettings();
    var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
    var client = new BackendApiClient(new HttpClient(handler), db);

    var result = await client.GetStationTenantIdAsync();

    Assert.Null(result);
}
```

```csharp
// Add to agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs, inside the existing class body

[Fact]
public async Task AdminLogin_MatchingTenant_SwitchesToEnrollPanel()
{
    var tenantId = Guid.NewGuid();
    var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", tenantId), StationTenantId = tenantId };
    var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

    await vm.AdminLoginCommand.ExecuteAsync(null);

    Assert.Equal(Visibility.Collapsed, vm.PunchPanelVisibility);
    Assert.Equal(Visibility.Visible, vm.EnrollPanelVisibility);
}

[Fact]
public async Task AdminLogin_DifferentTenantThanStation_StaysOnPunchPanelWithError()
{
    var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", Guid.NewGuid()), StationTenantId = Guid.NewGuid() };
    var prompt = new FakeAdminCredentialPrompt { Result = ("admin@othertenant.test", "correct-horse-battery") };
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

    await vm.AdminLoginCommand.ExecuteAsync(null);

    Assert.Equal(Visibility.Visible, vm.PunchPanelVisibility);
    Assert.Equal(Visibility.Collapsed, vm.EnrollPanelVisibility);
    Assert.Equal("Admin login failed.", vm.StatusMessage);
}

[Fact]
public async Task AdminLogin_StationTenantUnknown_StaysOnPunchPanelWithError()
{
    // GetStationTenantIdAsync returning null (network failure, station-key rejected, etc.) must
    // fail closed — an admin login with no determinable station tenant must NOT be treated as a
    // match just because there's nothing to contradict it.
    var api = new FakeBackendApiClient { LoginResult = new LoginResult("TenantAdmin", Guid.NewGuid()), StationTenantId = null };
    var prompt = new FakeAdminCredentialPrompt { Result = ("admin@acme.test", "correct-horse-battery") };
    var vm = new MainViewModel(new FakeCaptureService(), new FakeEnrollmentService(), api, new FakeFingerprintDevice(), new FakeFingerprintEnroller(), prompt);

    await vm.AdminLoginCommand.ExecuteAsync(null);

    Assert.Equal("Admin login failed.", vm.StatusMessage);
}
```

Update the 4 existing passing-case `AdminLogin_*` tests in the same file (search for `LoginResult = new LoginResult("TenantAdmin")` used to represent a SUCCESSFUL login, not the wrong-role/null-result failure tests) to also set a matching `StationTenantId` on the `FakeBackendApiClient` and pass that same tenant id into `new LoginResult("TenantAdmin", thatSameTenantId)`, per the "Breaking change" note above.

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build error — `LoginResult`'s 2-arg constructor, `FakeBackendApiClient.StationTenantId`, and `IBackendApiClient.GetStationTenantIdAsync` don't exist yet.

- [ ] **Step 3: Extend `LoginResult` and add the DTOs**

```csharp
// In agent/src/AttendanceAgent/Api/ApiDtos.cs, change this existing line:
public record LoginResult(string Role);
// to:
public record LoginResult(string Role, Guid? TenantId = null);

// And add this new internal record alongside the other internal wire-shape records:
internal record StationHealthResponsePayload(string Status, Guid StationId, Guid TenantId);
```

- [ ] **Step 4: Add the interface method**

```csharp
// Add to agent/src/AttendanceAgent/Api/IBackendApiClient.cs's interface body:
    Task<Guid?> GetStationTenantIdAsync(CancellationToken ct = default);
```

- [ ] **Step 5: Implement it**

```csharp
// Add to agent/src/AttendanceAgent/Api/BackendApiClient.cs, inside the class body

public async Task<Guid?> GetStationTenantIdAsync(CancellationToken ct = default)
{
    var request = await BuildRequestAsync(HttpMethod.Get, "/api/health/station", ct);
    var response = await _http.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode) return null;
    var result = await response.Content.ReadFromJsonAsync<StationHealthResponsePayload>(JsonOptions, ct);
    return result?.TenantId;
}
```

- [ ] **Step 6: Update `FakeBackendApiClient`**

```csharp
// Add to agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs, inside the class body

public Guid? StationTenantId { get; set; }

public Task<Guid?> GetStationTenantIdAsync(CancellationToken ct = default) => Task.FromResult(StationTenantId);
```

- [ ] **Step 7: Update `MainViewModel.AdminLoginAsync`**

```csharp
// Replace the existing AdminLoginAsync method body in agent/src/AttendanceAgent/ViewModels/MainViewModel.cs with:

    [RelayCommand]
    private async Task AdminLoginAsync()
    {
        var credentials = _prompt.PromptForCredentials();
        if (credentials is null) return;

        var login = await _api.LoginAsync(credentials.Value.Email, credentials.Value.Password);
        if (login is null || login.Role != "TenantAdmin")
        {
            StatusMessage = "Admin login failed.";
            return;
        }

        // Role alone isn't enough: a TenantAdmin for ANY tenant would otherwise unlock enrollment
        // on THIS station regardless of which company owns it. GetStationTenantIdAsync returning
        // null (network failure, etc.) fails closed — treated the same as a mismatch, not as "no
        // reason to reject."
        var stationTenantId = await _api.GetStationTenantIdAsync();
        if (stationTenantId is null || login.TenantId != stationTenantId)
        {
            StatusMessage = "Admin login failed.";
            return;
        }

        StatusMessage = "";
        PunchPanelVisibility = Visibility.Collapsed;
        EnrollPanelVisibility = Visibility.Visible;
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: all tests pass, including the 2 new `BackendApiClientTests` and 3 new `MainViewModelTests`, with the 4 pre-existing `AdminLogin_*` success-case tests updated per the "Breaking change" note rather than deleted or weakened.

- [ ] **Step 9: Commit**

```bash
git add agent/src/AttendanceAgent/Api agent/src/AttendanceAgent/ViewModels/MainViewModel.cs agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs
git commit -m "feat: reject admin logins that don't belong to the station's own tenant"
```

---

## What this plan does NOT cover (explicitly out of scope, per the design spec)

- No timed admin session — every enrollment attempt requires a fresh login.
- No re-enrollment confirmation dialog — uploading a template for an employee who already has one silently overwrites it (the backend already upserts).
- No offline queueing for enrollment uploads.
- No vendor-side payload/userID embedding in templates.
- Real hardware bring-up for the new enrollment paths (both vendors) — tracked as explicit open checklist items in `agent/README.md` (Tasks 4 and 5, Step 7 each), not silently assumed correct.
