# Desktop Agent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 1 MVP desktop agent — a WPF app that resolves an
employee code, verifies a fingerprint against a locally cached template,
queues the punch in local SQLite, and syncs it to the backend API — that
works fully offline once a station has seen an employee at least once.

**Architecture:** A .NET 8 WPF app with a small Generic Host (DI
container) wiring together: a `HttpClient`-based backend API client, two
"try-online-then-fall-back-to-local-cache" directory services (employee
lookup, template fetch), a SQLite-backed punch queue, a device-agnostic
fingerprint capture abstraction, and a background sync loop. Every service
below the ViewModel is tested against fakes — no physical scanner is
required to develop or test this plan; real ZK4500/SecuGen SDK integration
is a separate manual hardware bring-up task at the end.

**Tech Stack:** .NET 8 (WPF, `net8.0-windows`), EF Core 8 +
Microsoft.Data.Sqlite (local queue/cache, real file in prod, in-memory in
tests), CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, xUnit.

**Depends on:** [backend-api plan](2026-08-15-backend-api.md) — this plan
calls `GET /api/employees/lookup?code=`, `GET /api/templates/{employeeId}`,
and `POST /api/punches/batch`, all authenticated with the `X-Station-Key`
header issued when a station is created via that plan's Task 4.

## Global Constraints

- USB fingerprint devices are shared with other software — acquire the
  device handle only for the duration of one capture and release it
  immediately after, even if capture fails. Never hold it open between
  punches.
- The agent must work fully offline: once an employee has been resolved
  and their template fetched at least once (while online), subsequent
  punches for that employee must succeed with no network connectivity,
  queuing locally and syncing when connectivity returns.
- Punch sync to the backend must be idempotent — the same locally-queued
  punch retried after a dropped connection must not create a duplicate
  server-side record (the backend enforces this by client-generated punch
  id; this plan must preserve that id across retries).
- One device vendor per site — this plan does not need to handle mixed
  vendors; the fingerprint device/verifier abstraction takes a single
  configured vendor at a time.
- Design decision carried through this plan: the spec only calls out
  template caching explicitly, but "must work offline" combined with "1:1
  verify: employee enters ID/PIN, then places finger" means the
  employee-code-to-id resolution also needs an offline-capable local
  cache, not just the template. Task 4 builds both lookups with the same
  try-then-fallback shape.

---

## File Structure

```
agent/
  AttendanceAgent.sln
  src/AttendanceAgent/
    AttendanceAgent.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Data/AgentDbContext.cs
    Data/AgentSettings.cs
    Data/CachedEmployee.cs
    Data/CachedTemplate.cs
    Data/QueuedPunch.cs
    Devices/IFingerprintDevice.cs
    Devices/IFingerprintVerifier.cs
    Devices/DeviceCapture.cs
    Devices/FakeFingerprintDevice.cs
    Devices/FakeFingerprintVerifier.cs
    Api/IBackendApiClient.cs
    Api/BackendApiClient.cs
    Api/ApiDtos.cs
    Services/IEmployeeDirectoryService.cs
    Services/EmployeeDirectoryService.cs
    Services/ITemplateCacheService.cs
    Services/TemplateCacheService.cs
    Services/IPunchQueueService.cs
    Services/PunchQueueService.cs
    Services/IPunchCaptureService.cs
    Services/PunchCaptureService.cs
    Services/SyncBackgroundService.cs
    ViewModels/MainViewModel.cs
  tests/AttendanceAgent.Tests/
    AttendanceAgent.Tests.csproj
    TestDb.cs
    FakeHttpMessageHandler.cs
    FakeBackendApiClient.cs
    FakeCaptureService.cs
    AgentDbContextTests.cs
    DeviceCaptureTests.cs
    BackendApiClientTests.cs
    EmployeeDirectoryServiceTests.cs
    TemplateCacheServiceTests.cs
    PunchQueueServiceTests.cs
    PunchCaptureServiceTests.cs
    SyncBackgroundServiceTests.cs
    MainViewModelTests.cs
  README.md
```

---

### Task 1: Scaffold + local SQLite schema

**Files:**
- Create: `agent/AttendanceAgent.sln`
- Create: `agent/src/AttendanceAgent/AttendanceAgent.csproj`
- Create: `agent/src/AttendanceAgent/App.xaml`
- Create: `agent/src/AttendanceAgent/App.xaml.cs`
- Create: `agent/src/AttendanceAgent/MainWindow.xaml`
- Create: `agent/src/AttendanceAgent/MainWindow.xaml.cs`
- Create: `agent/src/AttendanceAgent/Data/AgentDbContext.cs`
- Create: `agent/src/AttendanceAgent/Data/AgentSettings.cs`
- Create: `agent/src/AttendanceAgent/Data/CachedEmployee.cs`
- Create: `agent/src/AttendanceAgent/Data/CachedTemplate.cs`
- Create: `agent/src/AttendanceAgent/Data/QueuedPunch.cs`
- Test: `agent/tests/AttendanceAgent.Tests/AttendanceAgent.Tests.csproj`
- Test: `agent/tests/AttendanceAgent.Tests/TestDb.cs`
- Test: `agent/tests/AttendanceAgent.Tests/AgentDbContextTests.cs`

**Interfaces:**
- Produces: `AgentDbContext` with `DbSet<AgentSettings> Settings`,
  `DbSet<CachedEmployee> CachedEmployees`, `DbSet<CachedTemplate>
  CachedTemplates`, `DbSet<QueuedPunch> QueuedPunches` — reused by every
  later task. `TestDb.CreateInMemory() -> AgentDbContext`, reused by every
  later test file.

- [ ] **Step 1: Create the solution and projects**

```bash
cd "agent"
dotnet new sln -n AttendanceAgent
dotnet new wpf -n AttendanceAgent -o src/AttendanceAgent
dotnet new xunit -n AttendanceAgent.Tests -o tests/AttendanceAgent.Tests
dotnet sln add src/AttendanceAgent/AttendanceAgent.csproj tests/AttendanceAgent.Tests/AttendanceAgent.Tests.csproj
dotnet add tests/AttendanceAgent.Tests/AttendanceAgent.Tests.csproj reference src/AttendanceAgent/AttendanceAgent.csproj
dotnet add src/AttendanceAgent/AttendanceAgent.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/AttendanceAgent/AttendanceAgent.csproj package Microsoft.Extensions.Hosting
dotnet add src/AttendanceAgent/AttendanceAgent.csproj package CommunityToolkit.Mvvm
dotnet add tests/AttendanceAgent.Tests/AttendanceAgent.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add tests/AttendanceAgent.Tests/AttendanceAgent.Tests.csproj package Microsoft.Extensions.DependencyInjection
```

- [ ] **Step 2: Write the entities**

```csharp
// agent/src/AttendanceAgent/Data/AgentSettings.cs
namespace AttendanceAgent.Data;

public class AgentSettings
{
    public int Id { get; set; } = 1;
    public required string BackendBaseUrl { get; set; }
    public required string StationApiKey { get; set; }
}
```

```csharp
// agent/src/AttendanceAgent/Data/CachedEmployee.cs
namespace AttendanceAgent.Data;

public class CachedEmployee
{
    public Guid EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
```

```csharp
// agent/src/AttendanceAgent/Data/CachedTemplate.cs
namespace AttendanceAgent.Data;

public class CachedTemplate
{
    public Guid EmployeeId { get; set; }
    public required byte[] TemplateData { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
```

```csharp
// agent/src/AttendanceAgent/Data/QueuedPunch.cs
namespace AttendanceAgent.Data;

public class QueuedPunch
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string PunchType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
```

- [ ] **Step 3: Write the DbContext**

```csharp
// agent/src/AttendanceAgent/Data/AgentDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Data;

public class AgentDbContext : DbContext
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options) { }

    public DbSet<AgentSettings> Settings => Set<AgentSettings>();
    public DbSet<CachedEmployee> CachedEmployees => Set<CachedEmployee>();
    public DbSet<CachedTemplate> CachedTemplates => Set<CachedTemplate>();
    public DbSet<QueuedPunch> QueuedPunches => Set<QueuedPunch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentSettings>().HasKey(s => s.Id);
        modelBuilder.Entity<CachedEmployee>(e =>
        {
            e.HasKey(c => c.EmployeeId);
            e.HasIndex(c => c.EmployeeCode).IsUnique();
        });
        modelBuilder.Entity<CachedTemplate>().HasKey(t => t.EmployeeId);
        modelBuilder.Entity<QueuedPunch>().HasKey(p => p.Id);
    }
}
```

- [ ] **Step 4: Write the test DB helper and the failing settings round-trip test**

```csharp
// agent/tests/AttendanceAgent.Tests/TestDb.cs
using AttendanceAgent.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Tests;

public static class TestDb
{
    public static AgentDbContext CreateInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AgentDbContext>().UseSqlite(connection).Options;
        var db = new AgentDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/AgentDbContextTests.cs
using AttendanceAgent.Data;
using Xunit;

namespace AttendanceAgent.Tests;

public class AgentDbContextTests
{
    [Fact]
    public void Settings_RoundTrips()
    {
        using var db = TestDb.CreateInMemory();

        db.Settings.Add(new AgentSettings { BackendBaseUrl = "https://api.test/", StationApiKey = "secret" });
        db.SaveChanges();

        var loaded = db.Settings.Single();
        Assert.Equal("https://api.test/", loaded.BackendBaseUrl);
    }
}
```

Add `using System.Linq;` at the top.

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: FAIL — compile error until Step 2-3's files exist (write them
first if running strictly in TDD order per-file; the test's shape is
established here regardless).

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: PASS

- [ ] **Step 7: Write the minimal WPF shell**

```xml
<!-- agent/src/AttendanceAgent/App.xaml -->
<Application x:Class="AttendanceAgent.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</Application>
```

```csharp
// agent/src/AttendanceAgent/App.xaml.cs
using System.IO;
using System.Windows;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AttendanceAgent;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZakAttendanceAgent");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "agent.db");

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<AgentDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
                services.AddSingleton<MainWindow>();
            })
            .Build();

        using (var scope = _host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
        }

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
```

```xml
<!-- agent/src/AttendanceAgent/MainWindow.xaml -->
<Window x:Class="AttendanceAgent.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ZAK Attendance" Height="240" Width="400">
    <TextBlock Text="Attendance agent starting up..." Margin="16" />
</Window>
```

```csharp
// agent/src/AttendanceAgent/MainWindow.xaml.cs
using System.Windows;

namespace AttendanceAgent;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

Remove the `StartupUri` attribute if the WPF template generated one in
`App.xaml` (it's not present in the version above, since startup is
handled in code in `OnStartup`).

- [ ] **Step 8: Commit**

```bash
git add agent
git commit -m "feat: scaffold desktop agent with local SQLite schema"
```

---

### Task 2: Fingerprint device abstraction + safe capture

**Files:**
- Create: `agent/src/AttendanceAgent/Devices/IFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/IFingerprintVerifier.cs`
- Create: `agent/src/AttendanceAgent/Devices/DeviceCapture.cs`
- Create: `agent/src/AttendanceAgent/Devices/FakeFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/FakeFingerprintVerifier.cs`
- Test: `agent/tests/AttendanceAgent.Tests/DeviceCaptureTests.cs`

**Interfaces:**
- Produces: `IFingerprintDevice { void Acquire(); byte[] Capture(); void
  Release(); }`, `IFingerprintVerifier { bool Verify(byte[] captured,
  byte[] enrolled); }`, `DeviceCapture.CaptureOnce(IFingerprintDevice) ->
  byte[]` (guarantees `Release()` runs even if `Capture()` throws) —
  consumed by Task 6's `PunchCaptureService`.

- [ ] **Step 1: Write the interfaces**

```csharp
// agent/src/AttendanceAgent/Devices/IFingerprintDevice.cs
namespace AttendanceAgent.Devices;

public interface IFingerprintDevice
{
    void Acquire();
    byte[] Capture();
    void Release();
}
```

```csharp
// agent/src/AttendanceAgent/Devices/IFingerprintVerifier.cs
namespace AttendanceAgent.Devices;

public interface IFingerprintVerifier
{
    bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate);
}
```

- [ ] **Step 2: Write the failing safe-capture test**

```csharp
// agent/tests/AttendanceAgent.Tests/DeviceCaptureTests.cs
using AttendanceAgent.Devices;
using Xunit;

namespace AttendanceAgent.Tests;

public class DeviceCaptureTests
{
    [Fact]
    public void CaptureOnce_ReturnsCapturedBytes_AndReleases()
    {
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9, 9 } };

        var result = DeviceCapture.CaptureOnce(device);

        Assert.Equal(new byte[] { 9, 9, 9 }, result);
        Assert.False(device.IsAcquired);
        Assert.Equal(new[] { "Acquire", "Capture", "Release" }, device.CallLog);
    }

    [Fact]
    public void CaptureOnce_WhenCaptureThrows_StillReleases()
    {
        var device = new FakeFingerprintDevice { ThrowOnCapture = true };

        Assert.Throws<InvalidOperationException>(() => DeviceCapture.CaptureOnce(device));

        Assert.False(device.IsAcquired);
        Assert.Equal(new[] { "Acquire", "Capture", "Release" }, device.CallLog);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter DeviceCaptureTests`
Expected: FAIL — compile error, `FakeFingerprintDevice`/`DeviceCapture` don't exist.

- [ ] **Step 4: Write the fakes and the safe-capture wrapper**

```csharp
// agent/src/AttendanceAgent/Devices/FakeFingerprintDevice.cs
namespace AttendanceAgent.Devices;

public class FakeFingerprintDevice : IFingerprintDevice
{
    public bool IsAcquired { get; private set; }
    public byte[] NextCapture { get; set; } = Array.Empty<byte>();
    public bool ThrowOnCapture { get; set; }
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

    public void Release()
    {
        IsAcquired = false;
        CallLog.Add("Release");
    }
}
```

```csharp
// agent/src/AttendanceAgent/Devices/FakeFingerprintVerifier.cs
namespace AttendanceAgent.Devices;

public class FakeFingerprintVerifier : IFingerprintVerifier
{
    public bool AlwaysMatches { get; set; } = true;

    public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate) => AlwaysMatches;
}
```

```csharp
// agent/src/AttendanceAgent/Devices/DeviceCapture.cs
namespace AttendanceAgent.Devices;

public static class DeviceCapture
{
    public static byte[] CaptureOnce(IFingerprintDevice device)
    {
        device.Acquire();
        try
        {
            return device.Capture();
        }
        finally
        {
            device.Release();
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter DeviceCaptureTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add agent
git commit -m "feat: add fingerprint device abstraction with guaranteed release"
```

---

### Task 3: Backend API client

**Files:**
- Create: `agent/src/AttendanceAgent/Api/ApiDtos.cs`
- Create: `agent/src/AttendanceAgent/Api/IBackendApiClient.cs`
- Create: `agent/src/AttendanceAgent/Api/BackendApiClient.cs`
- Test: `agent/tests/AttendanceAgent.Tests/FakeHttpMessageHandler.cs`
- Test: `agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs`

**Interfaces:**
- Consumes: `AgentDbContext.Settings` (Task 1), `QueuedPunch` (Task 1).
- Produces: `IBackendApiClient { Task<EmployeeLookupResult?>
  LookupEmployeeAsync(string, CancellationToken); Task<byte[]?>
  FetchTemplateAsync(Guid, CancellationToken); Task<bool>
  SubmitPunchesAsync(IReadOnlyList<QueuedPunch>, CancellationToken); }` —
  the real implementation is consumed at runtime by Tasks 4 and 7; a fake
  implementation of the same interface (`FakeBackendApiClient`, written in
  Task 4) is what Tasks 4-7's tests use instead of real HTTP.

- [ ] **Step 1: Write the DTOs matching the backend contract**

```csharp
// agent/src/AttendanceAgent/Api/ApiDtos.cs
namespace AttendanceAgent.Api;

public record EmployeeLookupResult(Guid EmployeeId, string EmployeeCode, string Name);

internal record TemplateFetchResponse(Guid EmployeeId, string TemplateData, DateTimeOffset EnrolledAt);
internal record PunchPayload(Guid Id, Guid EmployeeId, string PunchType, DateTimeOffset Timestamp);
internal record PunchBatchPayload(List<PunchPayload> Punches);
```

- [ ] **Step 2: Write the interface and the failing test**

```csharp
// agent/src/AttendanceAgent/Api/IBackendApiClient.cs
using AttendanceAgent.Data;

namespace AttendanceAgent.Api;

public interface IBackendApiClient
{
    Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default);
    Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default);
    Task<bool> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default);
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/FakeHttpMessageHandler.cs
namespace AttendanceAgent.Tests;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_responder(request));
    }
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/BackendApiClientTests.cs
using System.Net;
using System.Net.Http.Json;
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using Xunit;

namespace AttendanceAgent.Tests;

public class BackendApiClientTests
{
    private static AgentDbContext DbWithSettings()
    {
        var db = TestDb.CreateInMemory();
        db.Settings.Add(new AgentSettings { BackendBaseUrl = "https://api.test/", StationApiKey = "secret-key" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task LookupEmployeeAsync_SendsStationKeyHeader_AndParsesResponse()
    {
        using var db = DbWithSettings();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                employeeId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                employeeCode = "E001",
                name = "Jane Doe",
            }),
        });
        var client = new BackendApiClient(new HttpClient(handler), db);

        var result = await client.LookupEmployeeAsync("E001");

        Assert.Equal("Jane Doe", result!.Name);
        Assert.Equal("secret-key", handler.LastRequest!.Headers.GetValues("X-Station-Key").Single());
    }

    [Fact]
    public async Task LookupEmployeeAsync_NotFound_ReturnsNull()
    {
        using var db = DbWithSettings();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new BackendApiClient(new HttpClient(handler), db);

        var result = await client.LookupEmployeeAsync("NOPE");

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchTemplateAsync_DecodesBase64Payload()
    {
        using var db = DbWithSettings();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                employeeId = Guid.NewGuid(),
                templateData = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                enrolledAt = DateTimeOffset.UtcNow,
            }),
        });
        var client = new BackendApiClient(new HttpClient(handler), db);

        var result = await client.FetchTemplateAsync(Guid.NewGuid());

        Assert.Equal(new byte[] { 1, 2, 3 }, result);
    }

    [Fact]
    public async Task SubmitPunchesAsync_PostsBatchAndReturnsSuccess()
    {
        using var db = DbWithSettings();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { acceptedIds = new List<Guid>() }),
        });
        var client = new BackendApiClient(new HttpClient(handler), db);
        var punch = new QueuedPunch { Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), PunchType = "In", Timestamp = DateTimeOffset.UtcNow };

        var result = await client.SubmitPunchesAsync(new List<QueuedPunch> { punch });

        Assert.True(result);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }
}
```

Add `using System.Linq;` at the top.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter BackendApiClientTests`
Expected: FAIL — compile error, `BackendApiClient` doesn't exist.

- [ ] **Step 4: Write the implementation**

```csharp
// agent/src/AttendanceAgent/Api/BackendApiClient.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Api;

public class BackendApiClient : IBackendApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly AgentDbContext _db;

    public BackendApiClient(HttpClient http, AgentDbContext db)
    {
        _http = http;
        _db = db;
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var settings = await _db.Settings.SingleAsync(ct);
        var request = new HttpRequestMessage(method, new Uri(new Uri(settings.BackendBaseUrl), path));
        request.Headers.Add("X-Station-Key", settings.StationApiKey);
        return request;
    }

    public async Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default)
    {
        var request = await BuildRequestAsync(HttpMethod.Get, $"/api/employees/lookup?code={Uri.EscapeDataString(code)}", ct);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeLookupResult>(JsonOptions, ct);
    }

    public async Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default)
    {
        var request = await BuildRequestAsync(HttpMethod.Get, $"/api/templates/{employeeId}", ct);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TemplateFetchResponse>(JsonOptions, ct);
        return Convert.FromBase64String(body!.TemplateData);
    }

    public async Task<bool> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default)
    {
        var payload = new PunchBatchPayload(punches
            .Select(p => new PunchPayload(p.Id, p.EmployeeId, p.PunchType, p.Timestamp))
            .ToList());
        var request = await BuildRequestAsync(HttpMethod.Post, "/api/punches/batch", ct);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter BackendApiClientTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add agent
git commit -m "feat: add backend API client for lookup/template/punch endpoints"
```

---

### Task 4: Offline-capable employee and template caches

**Files:**
- Create: `agent/src/AttendanceAgent/Services/IEmployeeDirectoryService.cs`
- Create: `agent/src/AttendanceAgent/Services/EmployeeDirectoryService.cs`
- Create: `agent/src/AttendanceAgent/Services/ITemplateCacheService.cs`
- Create: `agent/src/AttendanceAgent/Services/TemplateCacheService.cs`
- Test: `agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs`
- Test: `agent/tests/AttendanceAgent.Tests/EmployeeDirectoryServiceTests.cs`
- Test: `agent/tests/AttendanceAgent.Tests/TemplateCacheServiceTests.cs`

**Interfaces:**
- Consumes: `IBackendApiClient` (Task 3), `CachedEmployee`/`CachedTemplate`
  (Task 1).
- Produces: `IEmployeeDirectoryService { Task<EmployeeLookupResult?>
  ResolveAsync(string code, CancellationToken); }`,
  `ITemplateCacheService { Task<byte[]?> GetTemplateAsync(Guid employeeId,
  CancellationToken); }` — both consumed by Task 6's
  `PunchCaptureService`. `FakeBackendApiClient` (test double implementing
  `IBackendApiClient`) is reused by every remaining test file in this plan.

- [ ] **Step 1: Write the fake backend client used by all remaining tests**

```csharp
// agent/tests/AttendanceAgent.Tests/FakeBackendApiClient.cs
using AttendanceAgent.Api;
using AttendanceAgent.Data;

namespace AttendanceAgent.Tests;

public class FakeBackendApiClient : IBackendApiClient
{
    public EmployeeLookupResult? LookupResult { get; set; }
    public bool ThrowOnLookup { get; set; }
    public byte[]? TemplateResult { get; set; }
    public bool ThrowOnTemplate { get; set; }
    public bool SubmitResult { get; set; } = true;
    public bool ThrowOnSubmit { get; set; }

    public Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default)
    {
        if (ThrowOnLookup) throw new HttpRequestException("offline");
        return Task.FromResult(LookupResult);
    }

    public Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (ThrowOnTemplate) throw new HttpRequestException("offline");
        return Task.FromResult(TemplateResult);
    }

    public Task<bool> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default)
    {
        if (ThrowOnSubmit) throw new HttpRequestException("offline");
        return Task.FromResult(SubmitResult);
    }
}
```

- [ ] **Step 2: Write the interfaces and failing employee directory test**

```csharp
// agent/src/AttendanceAgent/Services/IEmployeeDirectoryService.cs
using AttendanceAgent.Api;

namespace AttendanceAgent.Services;

public interface IEmployeeDirectoryService
{
    Task<EmployeeLookupResult?> ResolveAsync(string code, CancellationToken ct = default);
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/EmployeeDirectoryServiceTests.cs
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Xunit;

namespace AttendanceAgent.Tests;

public class EmployeeDirectoryServiceTests
{
    private static readonly Guid EmployeeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ResolveAsync_Online_CachesResultLocally()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(EmployeeId, "E001", "Jane Doe") };
        var service = new EmployeeDirectoryService(api, db);

        var result = await service.ResolveAsync("E001");

        Assert.Equal("Jane Doe", result!.Name);
        Assert.NotNull(await db.CachedEmployees.FindAsync(EmployeeId));
    }

    [Fact]
    public async Task ResolveAsync_Offline_FallsBackToLocalCache()
    {
        using var db = TestDb.CreateInMemory();
        db.CachedEmployees.Add(new CachedEmployee { EmployeeId = EmployeeId, EmployeeCode = "E001", Name = "Jane Doe", CachedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var api = new FakeBackendApiClient { ThrowOnLookup = true };
        var service = new EmployeeDirectoryService(api, db);

        var result = await service.ResolveAsync("E001");

        Assert.Equal("Jane Doe", result!.Name);
    }

    [Fact]
    public async Task ResolveAsync_OfflineAndUncached_ReturnsNull()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { ThrowOnLookup = true };
        var service = new EmployeeDirectoryService(api, db);

        var result = await service.ResolveAsync("E001");

        Assert.Null(result);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter EmployeeDirectoryServiceTests`
Expected: FAIL — compile error, `EmployeeDirectoryService` doesn't exist.

- [ ] **Step 4: Implement `EmployeeDirectoryService`**

```csharp
// agent/src/AttendanceAgent/Services/EmployeeDirectoryService.cs
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Services;

public class EmployeeDirectoryService : IEmployeeDirectoryService
{
    private readonly IBackendApiClient _api;
    private readonly AgentDbContext _db;

    public EmployeeDirectoryService(IBackendApiClient api, AgentDbContext db)
    {
        _api = api;
        _db = db;
    }

    public async Task<EmployeeLookupResult?> ResolveAsync(string code, CancellationToken ct = default)
    {
        try
        {
            var result = await _api.LookupEmployeeAsync(code, ct);
            if (result is not null)
            {
                await UpsertCacheAsync(result, ct);
                return result;
            }
        }
        catch (HttpRequestException)
        {
            // offline or unreachable — fall through to the local cache
        }

        var cached = await _db.CachedEmployees.SingleOrDefaultAsync(e => e.EmployeeCode == code, ct);
        return cached is null ? null : new EmployeeLookupResult(cached.EmployeeId, cached.EmployeeCode, cached.Name);
    }

    private async Task UpsertCacheAsync(EmployeeLookupResult result, CancellationToken ct)
    {
        var existing = await _db.CachedEmployees.FindAsync(new object[] { result.EmployeeId }, ct);
        if (existing is null)
        {
            _db.CachedEmployees.Add(new CachedEmployee
            {
                EmployeeId = result.EmployeeId,
                EmployeeCode = result.EmployeeCode,
                Name = result.Name,
                CachedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.EmployeeCode = result.EmployeeCode;
            existing.Name = result.Name;
            existing.CachedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter EmployeeDirectoryServiceTests`
Expected: PASS

- [ ] **Step 6: Write the interface and failing template cache test**

```csharp
// agent/src/AttendanceAgent/Services/ITemplateCacheService.cs
namespace AttendanceAgent.Services;

public interface ITemplateCacheService
{
    Task<byte[]?> GetTemplateAsync(Guid employeeId, CancellationToken ct = default);
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/TemplateCacheServiceTests.cs
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Xunit;

namespace AttendanceAgent.Tests;

public class TemplateCacheServiceTests
{
    private static readonly Guid EmployeeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetTemplateAsync_Online_CachesResultLocally()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { TemplateResult = new byte[] { 1, 2, 3 } };
        var service = new TemplateCacheService(api, db);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Equal(new byte[] { 1, 2, 3 }, result);
        Assert.NotNull(await db.CachedTemplates.FindAsync(EmployeeId));
    }

    [Fact]
    public async Task GetTemplateAsync_Offline_FallsBackToLocalCache()
    {
        using var db = TestDb.CreateInMemory();
        db.CachedTemplates.Add(new CachedTemplate { EmployeeId = EmployeeId, TemplateData = new byte[] { 4, 5, 6 }, CachedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var api = new FakeBackendApiClient { ThrowOnTemplate = true };
        var service = new TemplateCacheService(api, db);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Equal(new byte[] { 4, 5, 6 }, result);
    }

    [Fact]
    public async Task GetTemplateAsync_OfflineAndUncached_ReturnsNull()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { ThrowOnTemplate = true };
        var service = new TemplateCacheService(api, db);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Null(result);
    }
}
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter TemplateCacheServiceTests`
Expected: FAIL — compile error, `TemplateCacheService` doesn't exist.

- [ ] **Step 8: Implement `TemplateCacheService`**

```csharp
// agent/src/AttendanceAgent/Services/TemplateCacheService.cs
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Services;

public class TemplateCacheService : ITemplateCacheService
{
    private readonly IBackendApiClient _api;
    private readonly AgentDbContext _db;

    public TemplateCacheService(IBackendApiClient api, AgentDbContext db)
    {
        _api = api;
        _db = db;
    }

    public async Task<byte[]?> GetTemplateAsync(Guid employeeId, CancellationToken ct = default)
    {
        try
        {
            var result = await _api.FetchTemplateAsync(employeeId, ct);
            if (result is not null)
            {
                await UpsertCacheAsync(employeeId, result, ct);
                return result;
            }
        }
        catch (HttpRequestException)
        {
            // offline or unreachable — fall through to the local cache
        }

        var cached = await _db.CachedTemplates.FindAsync(new object[] { employeeId }, ct);
        return cached?.TemplateData;
    }

    private async Task UpsertCacheAsync(Guid employeeId, byte[] templateData, CancellationToken ct)
    {
        var existing = await _db.CachedTemplates.FindAsync(new object[] { employeeId }, ct);
        if (existing is null)
        {
            _db.CachedTemplates.Add(new CachedTemplate { EmployeeId = employeeId, TemplateData = templateData, CachedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            existing.TemplateData = templateData;
            existing.CachedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter TemplateCacheServiceTests`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add agent
git commit -m "feat: add offline-capable employee and template caches"
```

---

### Task 5: Local punch queue

**Files:**
- Create: `agent/src/AttendanceAgent/Services/IPunchQueueService.cs`
- Create: `agent/src/AttendanceAgent/Services/PunchQueueService.cs`
- Test: `agent/tests/AttendanceAgent.Tests/PunchQueueServiceTests.cs`

**Interfaces:**
- Consumes: `QueuedPunch` (Task 1).
- Produces: `IPunchQueueService { Task EnqueueAsync(Guid employeeId,
  string punchType, DateTimeOffset timestamp, CancellationToken);
  Task<List<QueuedPunch>> GetPendingAsync(CancellationToken); Task
  RemoveSyncedAsync(IEnumerable<Guid> punchIds, CancellationToken); }` —
  consumed by Task 6 (`PunchCaptureService.EnqueueAsync`) and Task 7
  (`SyncBackgroundService` reads and clears the queue).

- [ ] **Step 1: Write the interface and the failing test**

```csharp
// agent/src/AttendanceAgent/Services/IPunchQueueService.cs
using AttendanceAgent.Data;

namespace AttendanceAgent.Services;

public interface IPunchQueueService
{
    Task EnqueueAsync(Guid employeeId, string punchType, DateTimeOffset timestamp, CancellationToken ct = default);
    Task<List<QueuedPunch>> GetPendingAsync(CancellationToken ct = default);
    Task RemoveSyncedAsync(IEnumerable<Guid> punchIds, CancellationToken ct = default);
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/PunchQueueServiceTests.cs
using AttendanceAgent.Services;
using Xunit;

namespace AttendanceAgent.Tests;

public class PunchQueueServiceTests
{
    [Fact]
    public async Task Enqueue_ThenGetPending_ReturnsIt()
    {
        using var db = TestDb.CreateInMemory();
        var service = new PunchQueueService(db);
        var employeeId = Guid.NewGuid();

        await service.EnqueueAsync(employeeId, "In", DateTimeOffset.UtcNow);
        var pending = await service.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal(employeeId, pending[0].EmployeeId);
    }

    [Fact]
    public async Task RemoveSynced_DeletesOnlyGivenIds()
    {
        using var db = TestDb.CreateInMemory();
        var service = new PunchQueueService(db);
        var employeeId = Guid.NewGuid();
        await service.EnqueueAsync(employeeId, "In", DateTimeOffset.UtcNow);
        await service.EnqueueAsync(employeeId, "Out", DateTimeOffset.UtcNow);
        var pending = await service.GetPendingAsync();

        await service.RemoveSyncedAsync(new[] { pending[0].Id });

        var remaining = await service.GetPendingAsync();
        Assert.Single(remaining);
        Assert.Equal(pending[1].Id, remaining[0].Id);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter PunchQueueServiceTests`
Expected: FAIL — compile error, `PunchQueueService` doesn't exist.

- [ ] **Step 3: Implement `PunchQueueService`**

```csharp
// agent/src/AttendanceAgent/Services/PunchQueueService.cs
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Services;

public class PunchQueueService : IPunchQueueService
{
    private readonly AgentDbContext _db;

    public PunchQueueService(AgentDbContext db) => _db = db;

    public async Task EnqueueAsync(Guid employeeId, string punchType, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        _db.QueuedPunches.Add(new QueuedPunch
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            PunchType = punchType,
            Timestamp = timestamp,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<QueuedPunch>> GetPendingAsync(CancellationToken ct = default) =>
        await _db.QueuedPunches.OrderBy(p => p.Timestamp).ToListAsync(ct);

    public async Task RemoveSyncedAsync(IEnumerable<Guid> punchIds, CancellationToken ct = default)
    {
        var ids = punchIds.ToList();
        var rows = await _db.QueuedPunches.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        _db.QueuedPunches.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter PunchQueueServiceTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add agent
git commit -m "feat: add local SQLite punch queue"
```

---

### Task 6: Punch capture orchestration

**Files:**
- Create: `agent/src/AttendanceAgent/Services/IPunchCaptureService.cs`
- Create: `agent/src/AttendanceAgent/Services/PunchCaptureService.cs`
- Test: `agent/tests/AttendanceAgent.Tests/PunchCaptureServiceTests.cs`

**Interfaces:**
- Consumes: `IEmployeeDirectoryService`, `ITemplateCacheService` (Task 4),
  `IPunchQueueService` (Task 5), `IFingerprintDevice`,
  `IFingerprintVerifier`, `DeviceCapture` (Task 2).
- Produces: `PunchResult(bool Success, string Message)`,
  `IPunchCaptureService { Task<PunchResult> CapturePunchAsync(string
  employeeCode, string punchType, IFingerprintDevice device,
  CancellationToken); }` — consumed by Task 8's `MainViewModel`.

- [ ] **Step 1: Write the failing orchestration tests**

```csharp
// agent/tests/AttendanceAgent.Tests/PunchCaptureServiceTests.cs
using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using Xunit;

namespace AttendanceAgent.Tests;

public class PunchCaptureServiceTests
{
    [Fact]
    public async Task CapturePunch_MatchingFingerprint_EnqueuesPunch()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe"),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.True(result.Success);
        Assert.Single(await queue.GetPendingAsync());
        Assert.False(device.IsAcquired);
    }

    [Fact]
    public async Task CapturePunch_UnknownEmployee_FailsWithoutEnqueueing()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient();
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier(),
            queue);

        var result = await service.CapturePunchAsync("NOPE", "In", new FakeFingerprintDevice());

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_FingerprintMismatch_DoesNotEnqueue()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe"),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier { AlwaysMatches = false },
            queue);

        var result = await service.CapturePunchAsync("E001", "In", new FakeFingerprintDevice());

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_NoTemplateAvailable_Fails()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe"),
            TemplateResult = null,
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier(),
            queue);

        var result = await service.CapturePunchAsync("E001", "In", new FakeFingerprintDevice());

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter PunchCaptureServiceTests`
Expected: FAIL — compile error, `PunchCaptureService` doesn't exist.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// agent/src/AttendanceAgent/Services/IPunchCaptureService.cs
using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public record PunchResult(bool Success, string Message);

public interface IPunchCaptureService
{
    Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default);
}
```

```csharp
// agent/src/AttendanceAgent/Services/PunchCaptureService.cs
using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public class PunchCaptureService : IPunchCaptureService
{
    private readonly IEmployeeDirectoryService _employees;
    private readonly ITemplateCacheService _templates;
    private readonly IFingerprintVerifier _verifier;
    private readonly IPunchQueueService _queue;

    public PunchCaptureService(
        IEmployeeDirectoryService employees,
        ITemplateCacheService templates,
        IFingerprintVerifier verifier,
        IPunchQueueService queue)
    {
        _employees = employees;
        _templates = templates;
        _verifier = verifier;
        _queue = queue;
    }

    public async Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default)
    {
        var employee = await _employees.ResolveAsync(employeeCode, ct);
        if (employee is null)
            return new PunchResult(false, $"Employee code '{employeeCode}' not recognized.");

        var enrolledTemplate = await _templates.GetTemplateAsync(employee.EmployeeId, ct);
        if (enrolledTemplate is null)
            return new PunchResult(false, "No enrolled fingerprint template available (and none cached offline).");

        byte[] captured;
        try
        {
            captured = DeviceCapture.CaptureOnce(device);
        }
        catch (Exception ex)
        {
            return new PunchResult(false, $"Fingerprint capture failed: {ex.Message}");
        }

        if (!_verifier.Verify(captured, enrolledTemplate))
            return new PunchResult(false, "Fingerprint did not match.");

        await _queue.EnqueueAsync(employee.EmployeeId, punchType, DateTimeOffset.UtcNow, ct);
        return new PunchResult(true, $"Punch recorded for {employee.Name}.");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter PunchCaptureServiceTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add agent
git commit -m "feat: add punch capture orchestration service"
```

---

### Task 7: Background sync

**Files:**
- Create: `agent/src/AttendanceAgent/Services/SyncBackgroundService.cs`
- Test: `agent/tests/AttendanceAgent.Tests/SyncBackgroundServiceTests.cs`

**Interfaces:**
- Consumes: `IPunchQueueService` (Task 5), `IBackendApiClient` (Task 3).
- Produces: `SyncBackgroundService : BackgroundService` with a public
  `FlushOnceAsync(CancellationToken)` method the test drives directly
  (the timer loop itself is not unit-tested — it just calls
  `FlushOnceAsync` on an interval) — registered as a hosted service in
  Task 9's final DI wiring.

- [ ] **Step 1: Write the failing flush tests**

```csharp
// agent/tests/AttendanceAgent.Tests/SyncBackgroundServiceTests.cs
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceAgent.Tests;

public class SyncBackgroundServiceTests
{
    private static ServiceProvider BuildProvider(FakeBackendApiClient api)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IBackendApiClient>(api);
        services.AddScoped<IPunchQueueService, PunchQueueService>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
        return provider;
    }

    [Fact]
    public async Task FlushOnce_Online_SubmitsAndClearsQueue()
    {
        var api = new FakeBackendApiClient { SubmitResult = true };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>());
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task FlushOnce_Offline_LeavesQueueIntact()
    {
        var api = new FakeBackendApiClient { ThrowOnSubmit = true };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>());
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Single(remaining);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter SyncBackgroundServiceTests`
Expected: FAIL — compile error, `SyncBackgroundService` doesn't exist.

- [ ] **Step 3: Implement `SyncBackgroundService`**

```csharp
// agent/src/AttendanceAgent/Services/SyncBackgroundService.cs
using AttendanceAgent.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AttendanceAgent.Services;

public class SyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;

    public SyncBackgroundService(IServiceScopeFactory scopeFactory, TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _interval = interval ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await FlushOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
        }
    }

    public async Task FlushOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPunchQueueService>();
        var api = scope.ServiceProvider.GetRequiredService<IBackendApiClient>();

        var pending = await queue.GetPendingAsync(ct);
        if (pending.Count == 0) return;

        try
        {
            var accepted = await api.SubmitPunchesAsync(pending, ct);
            if (accepted)
                await queue.RemoveSyncedAsync(pending.Select(p => p.Id), ct);
        }
        catch (HttpRequestException)
        {
            // still offline — leave the queue untouched, retry next tick
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter SyncBackgroundServiceTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add agent
git commit -m "feat: add background punch sync with offline retry"
```

---

### Task 8: MainViewModel + UI wiring

**Files:**
- Create: `agent/src/AttendanceAgent/ViewModels/MainViewModel.cs`
- Modify: `agent/src/AttendanceAgent/MainWindow.xaml`
- Modify: `agent/src/AttendanceAgent/MainWindow.xaml.cs`
- Modify: `agent/src/AttendanceAgent/App.xaml.cs`
- Test: `agent/tests/AttendanceAgent.Tests/FakeCaptureService.cs`
- Test: `agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `IPunchCaptureService` (Task 6), `IFingerprintDevice` (Task 2).
- Produces: `MainViewModel { string EmployeeCode; string StatusMessage;
  IAsyncRelayCommand PunchCommand; }` bound directly by `MainWindow.xaml`.

- [ ] **Step 1: Write the fake capture service and failing ViewModel test**

```csharp
// agent/tests/AttendanceAgent.Tests/FakeCaptureService.cs
using AttendanceAgent.Devices;
using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeCaptureService : IPunchCaptureService
{
    public PunchResult Result { get; set; } = new(true, "ok");

    public Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default) =>
        Task.FromResult(Result);
}
```

```csharp
// agent/tests/AttendanceAgent.Tests/MainViewModelTests.cs
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using AttendanceAgent.ViewModels;
using Xunit;

namespace AttendanceAgent.Tests;

public class MainViewModelTests
{
    [Fact]
    public async Task Punch_OnSuccess_ClearsEmployeeCodeAndSetsStatus()
    {
        var capture = new FakeCaptureService { Result = new PunchResult(true, "Punch recorded for Jane Doe.") };
        var vm = new MainViewModel(capture, new FakeFingerprintDevice());
        vm.EmployeeCode = "E001";

        await vm.PunchCommand.ExecuteAsync("In");

        Assert.Equal("", vm.EmployeeCode);
        Assert.Equal("Punch recorded for Jane Doe.", vm.StatusMessage);
    }

    [Fact]
    public async Task Punch_OnFailure_KeepsEmployeeCodeAndSetsStatus()
    {
        var capture = new FakeCaptureService { Result = new PunchResult(false, "Fingerprint did not match.") };
        var vm = new MainViewModel(capture, new FakeFingerprintDevice());
        vm.EmployeeCode = "E001";

        await vm.PunchCommand.ExecuteAsync("In");

        Assert.Equal("E001", vm.EmployeeCode);
        Assert.Equal("Fingerprint did not match.", vm.StatusMessage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter MainViewModelTests`
Expected: FAIL — compile error, `MainViewModel` doesn't exist.

- [ ] **Step 3: Implement `MainViewModel`**

```csharp
// agent/src/AttendanceAgent/ViewModels/MainViewModel.cs
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AttendanceAgent.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPunchCaptureService _captureService;
    private readonly IFingerprintDevice _device;

    [ObservableProperty]
    private string employeeCode = "";

    [ObservableProperty]
    private string statusMessage = "";

    public MainViewModel(IPunchCaptureService captureService, IFingerprintDevice device)
    {
        _captureService = captureService;
        _device = device;
    }

    [RelayCommand]
    private async Task PunchAsync(string punchType)
    {
        var result = await _captureService.CapturePunchAsync(EmployeeCode, punchType, _device);
        StatusMessage = result.Message;
        if (result.Success) EmployeeCode = "";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test agent/tests/AttendanceAgent.Tests --filter MainViewModelTests`
Expected: PASS

- [ ] **Step 5: Wire the ViewModel into the window and finish DI registration**

```xml
<!-- agent/src/AttendanceAgent/MainWindow.xaml -->
<Window x:Class="AttendanceAgent.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ZAK Attendance" Height="260" Width="420">
    <StackPanel Margin="16">
        <TextBlock Text="Employee ID / PIN" Margin="0,0,0,4" />
        <TextBox Text="{Binding EmployeeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />
        <WrapPanel>
            <Button Content="IN" Command="{Binding PunchCommand}" CommandParameter="In" Margin="0,0,8,0" />
            <Button Content="BREAK OUT" Command="{Binding PunchCommand}" CommandParameter="BreakOut" Margin="0,0,8,0" />
            <Button Content="BREAK IN" Command="{Binding PunchCommand}" CommandParameter="BreakIn" Margin="0,0,8,0" />
            <Button Content="OUT" Command="{Binding PunchCommand}" CommandParameter="Out" />
        </WrapPanel>
        <TextBlock Text="{Binding StatusMessage}" Margin="0,16,0,0" TextWrapping="Wrap" />
    </StackPanel>
</Window>
```

```csharp
// agent/src/AttendanceAgent/MainWindow.xaml.cs
using System.Windows;
using AttendanceAgent.ViewModels;

namespace AttendanceAgent;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

```csharp
// agent/src/AttendanceAgent/App.xaml.cs
// Replace the ConfigureServices block with the full registration set:
services.AddDbContext<AgentDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
services.AddHttpClient<AttendanceAgent.Api.IBackendApiClient, AttendanceAgent.Api.BackendApiClient>();
services.AddScoped<AttendanceAgent.Services.IEmployeeDirectoryService, AttendanceAgent.Services.EmployeeDirectoryService>();
services.AddScoped<AttendanceAgent.Services.ITemplateCacheService, AttendanceAgent.Services.TemplateCacheService>();
services.AddScoped<AttendanceAgent.Services.IPunchQueueService, AttendanceAgent.Services.PunchQueueService>();
services.AddScoped<AttendanceAgent.Services.IPunchCaptureService, AttendanceAgent.Services.PunchCaptureService>();
services.AddSingleton<AttendanceAgent.Devices.IFingerprintDevice, AttendanceAgent.Devices.FakeFingerprintDevice>();
services.AddSingleton<AttendanceAgent.Devices.IFingerprintVerifier, AttendanceAgent.Devices.FakeFingerprintVerifier>();
services.AddSingleton<MainViewModel>();
services.AddSingleton<MainWindow>();
```

`AddHttpClient<TInterface, TImplementation>` requires the
`Microsoft.Extensions.Http` package — add it:

```bash
dotnet add agent/src/AttendanceAgent/AttendanceAgent.csproj package Microsoft.Extensions.Http
```

Note the real fingerprint device/verifier are still the fakes at this
point — Task 9 documents swapping these registrations for real vendor SDK
wrappers once hardware is available; nothing else in the DI graph changes
when that swap happens, since everything depends on the `IFingerprintDevice`/
`IFingerprintVerifier` interfaces only.

- [ ] **Step 6: Commit**

```bash
git add agent
git commit -m "feat: wire MainViewModel and finish service DI registration"
```

---

### Task 9: Station setup, background sync registration, and hardware bring-up

**Files:**
- Modify: `agent/src/AttendanceAgent/App.xaml.cs`
- Create: `agent/README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: a runnable agent with the sync loop active, and a documented
  first-run flow for entering a station's API key.

- [ ] **Step 1: Register the background sync service**

```csharp
// agent/src/AttendanceAgent/App.xaml.cs
// Add to the ConfigureServices block, alongside the other registrations:
services.AddHostedService<AttendanceAgent.Services.SyncBackgroundService>();
```

`SyncBackgroundService`'s constructor takes an optional `TimeSpan?
interval` (Task 7) — when registered via `AddHostedService`, DI supplies
only the `IServiceScopeFactory` argument and the default 30-second
interval is used automatically.

- [ ] **Step 2: Add first-run settings capture to `OnStartup`**

```csharp
// agent/src/AttendanceAgent/App.xaml.cs
// After building _host, before showing MainWindow, add:
using (var scope = _host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
    if (!db.Settings.Any())
    {
        var backendUrl = Microsoft.VisualBasic.Interaction.InputBox(
            "Backend base URL (e.g. https://attendance.example.com):", "First-run setup");
        var apiKey = Microsoft.VisualBasic.Interaction.InputBox(
            "Station API key (from the admin portal's station creation screen):", "First-run setup");
        db.Settings.Add(new AgentSettings { BackendBaseUrl = backendUrl, StationApiKey = apiKey });
        db.SaveChanges();
    }
}
```

`Microsoft.VisualBasic.Interaction.InputBox` needs a reference to
`Microsoft.VisualBasic` — add `<Reference Include="Microsoft.VisualBasic"
/>` inside an `<ItemGroup>` in `AttendanceAgent.csproj`, or replace this
with a small dedicated `SetupWindow` if a nicer first-run UI is wanted
later; the input box is the minimal Phase 1 version and is out of
automated-test scope (it's a blocking native dialog, not app logic).

- [ ] **Step 3: Run the full test suite to confirm nothing broke**

Run: `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: PASS (unchanged — this task only touches `App.xaml.cs`, which
has no unit tests)

- [ ] **Step 4: Write `agent/README.md`**

```markdown
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
```

- [ ] **Step 5: Commit**

```bash
git add agent
git commit -m "chore: register background sync, add first-run setup, and document hardware bring-up"
```

---

### Task 10: Real SecuGen hardware integration

**Added after Task 9**: vendor SDK resources became available (SecuGen's
SecuBSP SDK Pro — the "SecuGen Hamster Plus"-class device family named in
the spec, distributed as `SecuBSPMx.NET.dll`, a mixed-mode/IJW managed
wrapper around the native `SecuBSPMx.dll` + `sgfpamx.dll`). This task
replaces the fake device/verifier with real implementations for Release
builds, closing the gap `StartupGuards.AssertNoFakeHardwareInRelease`
(Task 9) was written to detect — today a genuine Release build has
nothing else to register and would always fail that guard.

**What can and cannot be automated here:** the class structure, DI wiring,
and build correctness can be verified without hardware. The actual
capture/match *behavior* against a physical scanner cannot — no device is
attached to the development machine. Treat this the same way Task 9
treats hardware bring-up: implement and test everything that doesn't
require a scanner, and leave a manual verification checklist for the rest.

**Files:**
- Create: `agent/src/AttendanceAgent/Vendor/SecuGen/x64/SecuBSPMx.dll` (binary, already copied)
- Create: `agent/src/AttendanceAgent/Vendor/SecuGen/x64/SecuBSPMx.NET.dll` (binary, already copied)
- Create: `agent/src/AttendanceAgent/Vendor/SecuGen/x64/sgfpamx.dll` (binary, already copied)
- Modify: `agent/src/AttendanceAgent/AttendanceAgent.csproj`
- Create: `agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintVerifier.cs`
- Modify: `agent/src/AttendanceAgent/HostComposition.cs` (or wherever `IFingerprintDevice`/`IFingerprintVerifier` are registered — check current file)
- Modify: `agent/README.md`
- Test: `agent/tests/AttendanceAgent.Tests/SecuGenFingerprintDeviceRegistrationTests.cs` (or similar name — verifies wiring, not hardware behavior)

**Interfaces:**
- Consumes: `IFingerprintDevice`, `IFingerprintVerifier` (Task 2) — the new classes implement these EXACTLY, no interface changes.
- Produces: `SecuGenFingerprintDevice : IFingerprintDevice`, `SecuGenFingerprintVerifier : IFingerprintVerifier`, registered as the real implementations under `#else` (Release) in the existing `#if DEBUG` guard that currently registers the fakes unconditionally.

- [ ] **Step 1: Add the vendor reference and platform target**

The vendor DLLs are already copied into
`agent/src/AttendanceAgent/Vendor/SecuGen/x64/`. `SecuBSPMx.NET.dll` is a
C++/CLI mixed-mode (IJW) assembly — it requires an EXACT platform match
(not AnyCPU) and the `Microsoft.DotNet.IjwHost` shim to load under modern
.NET. Update `AttendanceAgent.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.30" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.1" />
    <PackageReference Include="Microsoft.DotNet.IjwHost" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="SecuBSPMx.NET">
      <HintPath>Vendor\SecuGen\x64\SecuBSPMx.NET.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <None Include="Vendor\SecuGen\x64\SecuBSPMx.dll" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Vendor\SecuGen\x64\sgfpamx.dll" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

Check whether `Microsoft.DotNet.IjwHost` version `8.0.0` actually exists
on nuget.org — if not, use whatever the latest 8.x release is (the
package versions independently of the SDK; any 8.x release is fine for
net8.0-windows). If the package doesn't resolve at all, fall back to
copying `Ijwhost.dll` directly next to the vendor DLLs the same way
`SecuBSPMx.dll`/`sgfpamx.dll` are copied (a copy exists alongside the
vendor sample binaries at `C:\Users\Traveler\Documents\Work\ZAK\FP
Resources\samples\Net5.0\ReDist-Samples\bin\x64\Ijwhost.dll` if needed)
— but prefer the NuGet package since it won't go stale.

**Do NOT change `agent/tests/AttendanceAgent.Tests/AttendanceAgent.Tests.csproj`'s
platform** unless the build actually requires it — the test project only
references Fakes and should have no vendor dependency at all; keep the
hardware-dependent code isolated to the main project.

- [ ] **Step 2: Run the full test suite to confirm the platform-target change didn't break anything**

Run: `dotnet build agent/AttendanceAgent.sln` and `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build succeeds (0 warnings), all existing tests still pass unchanged (the platform target change affects the main project's output architecture, not test behavior).

- [ ] **Step 3: Implement `SecuGenFingerprintDevice`**

```csharp
// agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintDevice.cs
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
    }

    public byte[] Capture()
    {
        // Kiosk mode: no popup window, no on-screen fingerprint preview —
        // this station has no operator watching a capture dialog.
        _secuBsp.CaptureWindowOption.WindowStyle = (int)WindowStyle.INVISIBLE;
        _secuBsp.CaptureWindowOption.ShowFPImage = false;
        _secuBsp.CaptureWindowOption.FingerWindow = IntPtr.Zero;

        var err = _secuBsp.Capture(FIRPurpose.VERIFY);
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint capture failed (Capture: {err}).");

        // ASSUMPTION, NOT YET VERIFIED AGAINST REAL HARDWARE: FIRTextData
        // is the SDK's "text-encoded FIR" form (SecuAPI_FIR_FORM_TEXTENCODE),
        // which for every SecuGen SDK generation observed in the vendor
        // samples is base64. Verify this empirically during hardware
        // bring-up (Step 6 below) — if FIRTextData turns out not to be
        // valid base64, this line is the one to fix.
        return Convert.FromBase64String(_secuBsp.FIRTextData);
    }

    public void Release()
    {
        _secuBsp.CloseDevice();
    }
}
```

- [ ] **Step 4: Implement `SecuGenFingerprintVerifier`**

```csharp
// agent/src/AttendanceAgent/Devices/SecuGen/SecuGenFingerprintVerifier.cs
using SecuGen.SecuBSPPro.Windows;

namespace AttendanceAgent.Devices.SecuGen;

public class SecuGenFingerprintVerifier : IFingerprintVerifier
{
    public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate)
    {
        // VerifyMatch compares two already-captured FIR templates purely
        // in software — it does not need an open device, so this creates
        // its own short-lived SecuBSPMx instance rather than sharing one
        // with SecuGenFingerprintDevice.
        using var secuBsp = new SecuBSPMx();

        var capturedFir = Convert.ToBase64String(capturedTemplate);
        var enrolledFir = Convert.ToBase64String(enrolledTemplate);

        var err = secuBsp.VerifyMatch(capturedFir, enrolledFir);
        if (err != BSPError.ERROR_NONE)
            throw new InvalidOperationException($"Fingerprint match failed (VerifyMatch: {err}).");

        return secuBsp.IsMatched;
    }
}
```

Check whether `SecuBSPMx` implements `IDisposable` (the WinForms demo
calls `m_SecuBSP.Dispose()` in its close handler) — if so, the `using`
above is correct as written; if not, remove the `using` and just
construct it normally.

- [ ] **Step 5: Wire the real implementations into Release builds**

Find where `IFingerprintDevice`/`IFingerprintVerifier` are currently
registered (likely `HostComposition.cs`, alongside the existing
`#if DEBUG` / `#else` block that also guards
`AssertNoFakeHardwareInRelease`'s `isReleaseBuild` flag from Task 9).
Change the registration so Debug still uses the fakes (for local dev
without hardware) and Release uses the real SecuGen classes:

```csharp
#if DEBUG
services.AddSingleton<IFingerprintDevice, FakeFingerprintDevice>();
services.AddSingleton<IFingerprintVerifier, FakeFingerprintVerifier>();
#else
services.AddSingleton<IFingerprintDevice, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintDevice>();
services.AddSingleton<IFingerprintVerifier, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintVerifier>();
#endif
```

Adjust to match whatever the current file's exact structure is — the
point is Release must register the real classes, not the fakes, so
`StartupGuards.AssertNoFakeHardwareInRelease` (Task 9) actually passes on
a genuine Release build instead of always throwing.

- [ ] **Step 6: Write a registration test proving the DI wiring (not hardware behavior)**

```csharp
// agent/tests/AttendanceAgent.Tests/SecuGenFingerprintDeviceRegistrationTests.cs
using AttendanceAgent.Devices.SecuGen;
using Xunit;

namespace AttendanceAgent.Tests;

public class SecuGenFingerprintDeviceRegistrationTests
{
    [Fact]
    public void SecuGenFingerprintDevice_ImplementsIFingerprintDevice()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintDevice>(
            (object)Activator.CreateInstance(typeof(SecuGenFingerprintDevice))!);
    }

    [Fact]
    public void SecuGenFingerprintVerifier_ImplementsIFingerprintVerifier()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintVerifier>(
            (object)Activator.CreateInstance(typeof(SecuGenFingerprintVerifier))!);
    }
}
```

This only proves the classes exist and satisfy the interface contract —
it does NOT touch a real device (constructing `SecuBSPMx` doesn't require
one; only `OpenDevice()`/`Capture()` do). If constructing either class
throws in this test environment (e.g. a missing native DLL at test
runtime), that's real signal — investigate rather than deleting the test.

- [ ] **Step 7: Run the full suite one more time and update the README's hardware bring-up section**

Run: `dotnet test agent/tests/AttendanceAgent.Tests` and `dotnet build agent/AttendanceAgent.sln -c Release`
Expected: tests pass; Release build succeeds and does NOT throw
`StartupGuards`'s fake-hardware exception at the code level (the guard
itself only runs at app startup, not build time — but confirm by reading
the registration code that Release now points at the real classes).

Update `agent/README.md`'s "Real hardware bring-up" section: mark the SDK
integration itself as done, and narrow the remaining manual checklist to
what genuinely still needs a physical device:

```markdown
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
```

- [ ] **Step 8: Commit**

```bash
git add agent
git commit -m "feat: integrate real SecuGen fingerprint SDK for Release builds"
```

---

### Task 11: Real ZKFinger (ZK4500-class) hardware integration

**Added after Task 10**: ZKFinger Standard SDK 5.3.0.33 resources became
available (`libzkfpcsharp.dll`, a plain managed .NET Framework 2.0
P/Invoke wrapper — confirmed IL-only via `Module.GetPEKind`, not a
mixed-mode/IJW assembly like SecuGen's `SecuBSPMx.NET.dll`, so this does
NOT need the `Ijwhost.dll` shim Task 10 needed). This gives the desktop
agent a second real vendor implementation alongside SecuGen (Task 10), so
a station configured for the spec's `Zk4500` device vendor can run
without the fake device.

**Vendor selection is a build-time choice, not runtime.** Per the spec,
each station has exactly one device vendor. Task 10 wired `SecuGen`
unconditionally into every Release build. This task keeps that default
(so existing builds/tests are unaffected) and adds a new `DeviceVendor`
MSBuild property (`SecuGen` | `Zk4500`, default `SecuGen`) that picks
which real classes get registered in Release via a nested `#if`. Debug
always uses the fakes regardless of `DeviceVendor` — unchanged from
Task 10.

**Key vendor API differences from SecuGen (verify these against real
hardware — do not assume):**

1. **Templates are already raw bytes.** `AcquireFingerprint` fills a
   `byte[]` template buffer directly — there is no text-encoded form like
   SecuGen's `FIRTextData`, so there should be no equivalent of the
   base64-vs-UTF8 surprise Task 10 hit. Still confirm with a real capture
   that the returned bytes round-trip through storage and match
   correctly — don't assume this is automatically safe just because it's
   binary.
2. **Capture is poll-based, not blocking.** `AcquireFingerprint` returns
   immediately with `ZKFP_ERR_CAPTURE` (-8) if no finger is currently on
   the sensor — it does not block waiting for one like SecuGen's
   `Capture()`. `ZkFingerprintDevice.Capture()` below polls every 200ms
   (matching the vendor demo's own capture-thread interval) against a 15s
   deadline (matching the tuned timeout Task 10 found necessary for
   SecuGen — unconfirmed whether ZK needs the same, adjust if hardware
   bring-up shows otherwise).
3. **Matching needs its own algorithm handle.** Unlike SecuGen's
   `VerifyMatch` (pure software, no init needed), ZK's `DBMatch` requires
   `zkfp2.Init()` to have run and a `DBInit()` handle. Since the verifier
   runs after the device has already been released
   (`PunchCaptureService` calls `DeviceCapture.CaptureOnce` — which
   releases the device in its `finally` — before calling
   `IFingerprintVerifier.Verify`), `ZkFingerprintVerifier.Verify` does its
   own `Init()`/`DBInit()`/`DBFree()`/`Terminate()` around each call. This
   means a single punch attempt calls `zkfp2.Init()`/`Terminate()` twice
   in quick succession (once for capture, once for verify) — **hardware
   bring-up must confirm the native library tolerates rapid
   re-init/terminate cycling**; if it doesn't, the fix is a
   longer-lived shared handle instead of a short-lived one, but don't
   build that speculatively until hardware proves it's needed.
4. **No native runtime DLL ships in this SDK's `C#/lib/` folders** — only
   `libzkfpcsharp.dll` (the managed wrapper) and, elsewhere in the SDK,
   `.lib` import stubs for native C/C++ builds. The SDK root has a
   `setup.exe`. This strongly suggests ZKTeco expects the native
   driver/runtime to be installed system-wide via that installer on each
   station machine, unlike SecuGen's loose-DLL redistributable approach.
   **This is an assumption, not a confirmed fact** — hardware bring-up
   must check whether `zkfp2.Init()` throws `DllNotFoundException`
   without the driver installed, and if so, document running `setup.exe`
   as a required station provisioning step.

**Files:**
- Create: `agent/src/AttendanceAgent/Vendor/Zk/x64/libzkfpcsharp.dll` (binary, already copied)
- Modify: `agent/src/AttendanceAgent/AttendanceAgent.csproj`
- Create: `agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintDevice.cs`
- Create: `agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintVerifier.cs`
- Modify: `agent/src/AttendanceAgent/HostComposition.cs`
- Modify: `agent/README.md`
- Test: `agent/tests/AttendanceAgent.Tests/ZkFingerprintDeviceRegistrationTests.cs`

**Interfaces:**
- Consumes: `IFingerprintDevice`, `IFingerprintVerifier` (Task 2) — no interface changes.
- Produces: `ZkFingerprintDevice : IFingerprintDevice`, `ZkFingerprintVerifier : IFingerprintVerifier`, registered under a new `DEVICE_VENDOR_ZK4500` compile constant inside the existing Release (`#else` of `#if DEBUG`) branch.

- [ ] **Step 1: Add the `DeviceVendor` MSBuild property and the vendor reference**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <DeviceVendor Condition="'$(DeviceVendor)'==''">SecuGen</DeviceVendor>
    <DefineConstants>$(DefineConstants);DEVICE_VENDOR_$(DeviceVendor.ToUpperInvariant())</DefineConstants>
  </PropertyGroup>

  <!-- ... existing PackageReference / SecuGen ItemGroups unchanged ... -->

  <ItemGroup>
    <Reference Include="libzkfpcsharp">
      <HintPath>Vendor\Zk\x64\libzkfpcsharp.dll</HintPath>
    </Reference>
  </ItemGroup>

</Project>
```

`libzkfpcsharp.dll` is a plain IL-only managed reference — normal
reference copy-local behavior puts it in the output directory already,
so (unlike SecuGen's native companions) it does NOT need an explicit
`<None Include=... CopyToOutputDirectory>` entry. Default `DeviceVendor`
to `SecuGen` so every existing build command, and all of Task 10's tests,
keep working unchanged. Building for a ZK4500 station is
`dotnet build agent/AttendanceAgent.sln -p:DeviceVendor=Zk4500` (or the
equivalent `dotnet publish ... -c Release -p:DeviceVendor=Zk4500`).

- [ ] **Step 2: Run the full test suite to confirm nothing broke**

Run: `dotnet build agent/AttendanceAgent.sln` and `dotnet test agent/tests/AttendanceAgent.Tests`
Expected: build succeeds, all 52 existing tests still pass unchanged (no
`DeviceVendor` was passed, so this build is identical to Task 10's
default SecuGen configuration).

- [ ] **Step 3: Implement `ZkFingerprintDevice`**

```csharp
// agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintDevice.cs
using libzkfpcsharp;

namespace AttendanceAgent.Devices.Zk;

public class ZkFingerprintDevice : IFingerprintDevice
{
    private const int ParamImageWidth = 1;
    private const int ParamImageHeight = 2;
    private const int TemplateBufferSize = 2048;
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private IntPtr _devHandle = IntPtr.Zero;
    private byte[] _imageBuffer = Array.Empty<byte>();

    public void Acquire()
    {
        var initErr = zkfp2.Init();
        if (initErr != zkfperrdef.ZKFP_ERR_OK && initErr != zkfperrdef.ZKFP_ERR_ALREADY_INIT)
            throw new InvalidOperationException($"ZKFinger algorithm init failed (Init: {initErr}).");

        if (zkfp2.GetDeviceCount() == 0)
            throw new InvalidOperationException("No ZKFinger device found.");

        _devHandle = zkfp2.OpenDevice(0);
        if (_devHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open ZKFinger device (OpenDevice).");

        var width = ReadIntParameter(_devHandle, ParamImageWidth);
        var height = ReadIntParameter(_devHandle, ParamImageHeight);
        _imageBuffer = new byte[Math.Max(1, width * height)];
    }

    public byte[] Capture()
    {
        var template = new byte[TemplateBufferSize];
        var deadline = DateTime.UtcNow + CaptureTimeout;
        int lastErr;
        do
        {
            var size = TemplateBufferSize;
            lastErr = zkfp2.AcquireFingerprint(_devHandle, _imageBuffer, template, ref size);
            if (lastErr == zkfperrdef.ZKFP_ERR_OK)
            {
                var result = new byte[size];
                Array.Copy(template, result, size);
                return result;
            }

            Thread.Sleep(PollInterval);
        } while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException($"Fingerprint capture timed out (last AcquireFingerprint result: {lastErr}).");
    }

    public void Release()
    {
        if (_devHandle != IntPtr.Zero)
        {
            zkfp2.CloseDevice(_devHandle);
            _devHandle = IntPtr.Zero;
        }
        zkfp2.Terminate();
    }

    private static int ReadIntParameter(IntPtr devHandle, int code)
    {
        var buffer = new byte[4];
        var size = buffer.Length;
        zkfp2.GetParameters(devHandle, code, buffer, ref size);
        var value = 0;
        zkfp2.ByteArray2Int(buffer, ref value);
        return value;
    }
}
```

- [ ] **Step 4: Implement `ZkFingerprintVerifier`**

```csharp
// agent/src/AttendanceAgent/Devices/Zk/ZkFingerprintVerifier.cs
using libzkfpcsharp;

namespace AttendanceAgent.Devices.Zk;

public class ZkFingerprintVerifier : IFingerprintVerifier
{
    public bool Verify(byte[] capturedTemplate, byte[] enrolledTemplate)
    {
        // DBMatch is pure software but, unlike SecuGen's VerifyMatch, still
        // needs the algorithm library initialized and a DB handle — it does
        // NOT need an open device. This runs independently of
        // ZkFingerprintDevice, which has already been released by the time
        // this is called (see DeviceCapture.CaptureOnce).
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
            var score = zkfp2.DBMatch(dbHandle, capturedTemplate, enrolledTemplate);
            return score > 0;
        }
        finally
        {
            zkfp2.DBFree(dbHandle);
            zkfp2.Terminate();
        }
    }
}
```

- [ ] **Step 5: Wire the real implementations into Release builds under the new compile constant**

```csharp
#if DEBUG
services.AddSingleton<IFingerprintDevice, FakeFingerprintDevice>();
services.AddSingleton<IFingerprintVerifier, FakeFingerprintVerifier>();
#elif DEVICE_VENDOR_ZK4500
services.AddSingleton<IFingerprintDevice, AttendanceAgent.Devices.Zk.ZkFingerprintDevice>();
services.AddSingleton<IFingerprintVerifier, AttendanceAgent.Devices.Zk.ZkFingerprintVerifier>();
#else
services.AddSingleton<IFingerprintDevice, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintDevice>();
services.AddSingleton<IFingerprintVerifier, AttendanceAgent.Devices.SecuGen.SecuGenFingerprintVerifier>();
#endif
```

Adjust to match the current file's exact structure. Confirm the default
(no `DeviceVendor` passed) still resolves to the `#else` SecuGen branch.

- [ ] **Step 6: Write a registration test proving the DI wiring (not hardware behavior)**

```csharp
// agent/tests/AttendanceAgent.Tests/ZkFingerprintDeviceRegistrationTests.cs
using AttendanceAgent.Devices.Zk;
using Xunit;

namespace AttendanceAgent.Tests;

public class ZkFingerprintDeviceRegistrationTests
{
    [Fact]
    public void ZkFingerprintDevice_ImplementsIFingerprintDevice()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintDevice>(
            (object)Activator.CreateInstance(typeof(ZkFingerprintDevice))!);
    }

    [Fact]
    public void ZkFingerprintVerifier_ImplementsIFingerprintVerifier()
    {
        Assert.IsAssignableFrom<AttendanceAgent.Devices.IFingerprintVerifier>(
            (object)Activator.CreateInstance(typeof(ZkFingerprintVerifier))!);
    }
}
```

Constructing either class must not touch the device (no side effects
outside `Acquire()`) — same principle as Task 10's SecuGen registration
test. This project does not build with `DEVICE_VENDOR_ZK4500` defined,
so these tests only prove the classes compile and satisfy the interface
contract, same limitation as Task 10.

- [ ] **Step 7: Run the full suite in both configurations and update the README**

Run: `dotnet test agent/tests/AttendanceAgent.Tests` (default, SecuGen),
then `dotnet build agent/AttendanceAgent.sln -c Release -p:DeviceVendor=Zk4500`
to confirm the ZK4500 configuration actually compiles (it won't be
exercised by the default test run).
Expected: default tests pass unchanged; the `Zk4500` Release build
succeeds.

Update `agent/README.md`'s "Real hardware bring-up" section with a new
ZK4500 subsection listing what a physical device must confirm: the raw
byte-array-templates assumption, the AcquireFingerprint polling
timeout, the double Init/Terminate cycle per punch, and whether
`setup.exe`'s driver install is a prerequisite (see the four numbered
points above). Do not mark any of them "verified" until they're actually
checked against real hardware.

- [ ] **Step 8: Commit**

```bash
git add agent
git commit -m "feat: integrate real ZKFinger fingerprint SDK for ZK4500-configured stations"
```

---

## What Phase 2+ picks up from here

- Multi-station template pre-sync (today each station fetches on demand;
  Phase 2 pushes templates to every station at a site proactively).
- Agent auto-update mechanism (Phase 4 per the spec).
- Per-station `DeviceVendor` selection is a build-time MSBuild property
  today (Task 10 + Task 11) — if a fleet ever needs to mix vendors from a
  single distributed binary rather than a per-station build, that would
  need to become a runtime choice driven by the station's backend
  config instead.
