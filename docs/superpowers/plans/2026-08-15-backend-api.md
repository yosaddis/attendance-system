# Backend API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 1 MVP backend API — tenant/station/employee/shift CRUD,
operator + tenant-admin auth, station API-key auth, fingerprint template
storage, punch ingestion, and a basic daily attendance view — that the
desktop agent and web portal plans consume as their contract.

**Architecture:** ASP.NET Core 8 Web API with EF Core over PostgreSQL in
production (SQLite in-memory for fast integration tests). Two auth schemes
coexist: JWT bearer for human logins (Operator role = SaaS operator,
TenantAdmin role = scoped to one tenant) and a custom `StationKey` scheme
(one API key per station, hashed at rest) for the desktop agent's
machine-to-machine calls (template fetch/enroll, punch ingestion).

**Tech Stack:** .NET 8, ASP.NET Core Web API (controllers), EF Core 8,
Npgsql (prod) / Microsoft.Data.Sqlite (tests), xUnit,
Microsoft.AspNetCore.Mvc.Testing, Microsoft.AspNetCore.Authentication.JwtBearer,
Microsoft.AspNetCore.Identity (PasswordHasher only).

## Global Constraints

- Every tenant-scoped table carries `tenant_id`; every query must filter by
  it — never trust a client-supplied tenant id, derive it from the
  authenticated principal (JWT claim or station's tenant).
- One device vendor per tenant (`zk4500`/`secugen`) — a station's vendor
  must match its tenant's vendor; reject otherwise.
- Fingerprint template bytes are encrypted at rest, never returned in list
  endpoints, only fetched individually by an authenticated station.
- Punch ingestion must be idempotent by client-generated punch id — the
  desktop agent retries queued punches after reconnecting, and duplicates
  must not be created.
- Tenant `status` (`active`/`grace`/`suspended`) is operator-only to change
  — no payment gateway, no automatic transitions in Phase 1.
- No late/hours/anomaly computation in Phase 1 (that's Phase 2) — the daily
  attendance view is a raw first-in/last-out summary only.

---

## File Structure

```
backend/
  AttendanceApi.sln
  src/AttendanceApi/
    AttendanceApi.csproj
    Program.cs
    appsettings.json
    appsettings.Development.json
    Data/AppDbContext.cs
    Entities/Tenant.cs
    Entities/User.cs
    Entities/Station.cs
    Entities/Employee.cs
    Entities/Shift.cs
    Entities/FingerprintTemplate.cs
    Entities/Punch.cs
    Dtos/TenantDtos.cs
    Dtos/AuthDtos.cs
    Dtos/StationDtos.cs
    Dtos/EmployeeDtos.cs
    Dtos/ShiftDtos.cs
    Dtos/TemplateDtos.cs
    Dtos/PunchDtos.cs
    Dtos/AttendanceDtos.cs
    Auth/StationKeyAuthHandler.cs
    Auth/AuthorizationPolicies.cs
    Auth/JwtTokenService.cs
    Auth/ClaimsPrincipalExtensions.cs
    Services/ITemplateCipher.cs
    Services/AesTemplateCipher.cs
    Services/StationKeyGenerator.cs
    Controllers/HealthController.cs
    Controllers/AuthController.cs
    Controllers/TenantsController.cs
    Controllers/StationsController.cs
    Controllers/EmployeesController.cs
    Controllers/ShiftsController.cs
    Controllers/TemplatesController.cs
    Controllers/PunchesController.cs
    Controllers/AttendanceController.cs
    Migrations/ (EF-generated)
  tests/AttendanceApi.Tests/
    AttendanceApi.Tests.csproj
    ApiFactory.cs
    HealthTests.cs
    TenantsControllerTests.cs
    AuthTests.cs
    StationsControllerTests.cs
    EmployeesControllerTests.cs
    ShiftsControllerTests.cs
    TemplatesControllerTests.cs
    PunchesControllerTests.cs
    AttendanceControllerTests.cs
  docker-compose.yml
  README.md
```

---

### Task 1: Solution scaffold + health check

**Files:**
- Create: `backend/AttendanceApi.sln`
- Create: `backend/src/AttendanceApi/AttendanceApi.csproj`
- Create: `backend/src/AttendanceApi/Program.cs`
- Create: `backend/src/AttendanceApi/appsettings.json`
- Create: `backend/src/AttendanceApi/appsettings.Development.json`
- Create: `backend/src/AttendanceApi/Controllers/HealthController.cs`
- Test: `backend/tests/AttendanceApi.Tests/AttendanceApi.Tests.csproj`
- Test: `backend/tests/AttendanceApi.Tests/HealthTests.cs`

**Interfaces:**
- Produces: `public partial class Program` (required so
  `WebApplicationFactory<Program>` can see it from the test project),
  `GET /api/health` → `200 { "status": "ok" }`.

- [ ] **Step 1: Create the solution and projects**

```bash
cd "backend"
dotnet new sln -n AttendanceApi
dotnet new webapi -n AttendanceApi -o src/AttendanceApi --use-controllers
dotnet new xunit -n AttendanceApi.Tests -o tests/AttendanceApi.Tests
dotnet sln add src/AttendanceApi/AttendanceApi.csproj tests/AttendanceApi.Tests/AttendanceApi.Tests.csproj
dotnet add tests/AttendanceApi.Tests/AttendanceApi.Tests.csproj reference src/AttendanceApi/AttendanceApi.csproj
dotnet add tests/AttendanceApi.Tests/AttendanceApi.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/AttendanceApi.Tests/AttendanceApi.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/AttendanceApi/AttendanceApi.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/AttendanceApi/AttendanceApi.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/AttendanceApi/AttendanceApi.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/AttendanceApi/AttendanceApi.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/AttendanceApi/AttendanceApi.csproj package Microsoft.AspNetCore.Identity
```

- [ ] **Step 2: Write the failing health check test**

```csharp
// backend/tests/AttendanceApi.Tests/HealthTests.cs
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AttendanceApi.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body!.Status);
    }

    private record HealthResponse(string Status);
}
```

Add `using Microsoft.AspNetCore.Mvc.Testing;` at the top.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests`
Expected: FAIL — compile error, `HealthController`/route doesn't exist yet.

- [ ] **Step 4: Implement the health controller**

```csharp
// backend/src/AttendanceApi/Controllers/HealthController.cs
using Microsoft.AspNetCore.Mvc;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok" });
}
```

Add `using Microsoft.AspNetCore.Authorization;` at the top. Replace the
default `Program.cs` generated by `dotnet new webapi` with:

```csharp
// backend/src/AttendanceApi/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: scaffold backend API with health check"
```

---

### Task 2: EF Core + Tenant entity + open CRUD

**Files:**
- Create: `backend/src/AttendanceApi/Data/AppDbContext.cs`
- Create: `backend/src/AttendanceApi/Entities/Tenant.cs`
- Create: `backend/src/AttendanceApi/Dtos/TenantDtos.cs`
- Create: `backend/src/AttendanceApi/Controllers/TenantsController.cs`
- Modify: `backend/src/AttendanceApi/Program.cs`
- Modify: `backend/src/AttendanceApi/appsettings.json`
- Test: `backend/tests/AttendanceApi.Tests/ApiFactory.cs`
- Test: `backend/tests/AttendanceApi.Tests/TenantsControllerTests.cs`

**Interfaces:**
- Consumes: nothing yet (first entity).
- Produces: `AppDbContext` with `DbSet<Tenant> Tenants`, reused by every
  later task; `ApiFactory` test fixture (Sqlite in-memory), reused by every
  later controller test; `Tenant { Guid Id, string Name, TenantStatus
  Status, DeviceVendor DeviceVendor, DateTimeOffset CreatedAt }`.

- [ ] **Step 1: Write the entity and enums**

```csharp
// backend/src/AttendanceApi/Entities/Tenant.cs
namespace AttendanceApi.Entities;

public enum TenantStatus { Active, Grace, Suspended }
public enum DeviceVendor { Zk4500, Secugen }

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public DeviceVendor DeviceVendor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Write the DbContext**

```csharp
// backend/src/AttendanceApi/Data/AppDbContext.cs
using AttendanceApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>();
            e.Property(t => t.DeviceVendor).HasConversion<string>();
        });
    }
}
```

- [ ] **Step 3: Wire EF Core into `Program.cs`**

```csharp
// backend/src/AttendanceApi/Program.cs
using AttendanceApi.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
```

Add to `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=attendance;Username=attendance;Password=attendance"
  },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

- [ ] **Step 4: Add the test factory**

```csharp
// backend/tests/AttendanceApi.Tests/ApiFactory.cs
using AttendanceApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceApi.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}
```

Add `using System.Linq;` at the top.

- [ ] **Step 5: Write the failing tenant CRUD test**

```csharp
// backend/tests/AttendanceApi.Tests/TenantsControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Dtos;
using Xunit;

namespace AttendanceApi.Tests;

public class TenantsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TenantsControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateThenGet_RoundTrips()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/tenants",
            new CreateTenantRequest("Acme Foods", "Zk4500"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantResponse>();

        var getResponse = await client.GetAsync($"/api/tenants/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<TenantResponse>();

        Assert.Equal("Acme Foods", fetched!.Name);
        Assert.Equal("Active", fetched.Status);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter TenantsControllerTests`
Expected: FAIL — compile error, `TenantDtos`/`TenantsController` don't exist.

- [ ] **Step 7: Write the DTOs and controller**

```csharp
// backend/src/AttendanceApi/Dtos/TenantDtos.cs
namespace AttendanceApi.Dtos;

public record CreateTenantRequest(string Name, string DeviceVendor);
public record UpdateTenantRequest(string Name, string DeviceVendor);
public record UpdateTenantStatusRequest(string Status);
public record TenantResponse(Guid Id, string Name, string Status, string DeviceVendor, DateTimeOffset CreatedAt);
```

```csharp
// backend/src/AttendanceApi/Controllers/TenantsController.cs
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TenantsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<TenantResponse>>> List()
    {
        var tenants = await _db.Tenants.ToListAsync();
        return tenants.Select(ToResponse).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantResponse>> Get(Guid id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        return tenant is null ? NotFound() : ToResponse(tenant);
    }

    [HttpPost]
    public async Task<ActionResult<TenantResponse>> Create(CreateTenantRequest request)
    {
        var tenant = new Tenant
        {
            Name = request.Name,
            DeviceVendor = Enum.Parse<DeviceVendor>(request.DeviceVendor),
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = tenant.Id }, ToResponse(tenant));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TenantResponse>> Update(Guid id, UpdateTenantRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        tenant.Name = request.Name;
        tenant.DeviceVendor = Enum.Parse<DeviceVendor>(request.DeviceVendor);
        await _db.SaveChangesAsync();
        return ToResponse(tenant);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TenantResponse>> UpdateStatus(Guid id, UpdateTenantStatusRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        tenant.Status = Enum.Parse<TenantStatus>(request.Status);
        await _db.SaveChangesAsync();
        return ToResponse(tenant);
    }

    private static TenantResponse ToResponse(Tenant t) =>
        new(t.Id, t.Name, t.Status.ToString(), t.DeviceVendor.ToString(), t.CreatedAt);
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter TenantsControllerTests`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add backend
git commit -m "feat: add Tenant entity and CRUD endpoints"
```

---

### Task 3: Auth — User entity, JWT login, Operator/TenantAdmin policies

**Files:**
- Create: `backend/src/AttendanceApi/Entities/User.cs`
- Create: `backend/src/AttendanceApi/Dtos/AuthDtos.cs`
- Create: `backend/src/AttendanceApi/Auth/JwtTokenService.cs`
- Create: `backend/src/AttendanceApi/Auth/AuthorizationPolicies.cs`
- Create: `backend/src/AttendanceApi/Auth/ClaimsPrincipalExtensions.cs`
- Create: `backend/src/AttendanceApi/Controllers/AuthController.cs`
- Modify: `backend/src/AttendanceApi/Data/AppDbContext.cs`
- Modify: `backend/src/AttendanceApi/Controllers/TenantsController.cs`
- Modify: `backend/src/AttendanceApi/Program.cs`
- Modify: `backend/src/AttendanceApi/appsettings.Development.json`
- Test: `backend/tests/AttendanceApi.Tests/AuthTests.cs`
- Test: `backend/tests/AttendanceApi.Tests/TenantsControllerTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 2).
- Produces: `JwtTokenService.IssueToken(User user) -> string`; policies
  named `"Operator"` and `"TenantAdmin"`; `ClaimsPrincipalExtensions.TenantId(this
  ClaimsPrincipal)` returning `Guid?`, used by every later tenant-scoped
  controller. `POST /api/auth/login` → `200 { Token, Role, TenantId? }` or
  `401`.

- [ ] **Step 1: Write the User entity and register it on the DbContext**

```csharp
// backend/src/AttendanceApi/Entities/User.cs
namespace AttendanceApi.Entities;

public enum UserRole { Operator, TenantAdmin }

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public Guid? TenantId { get; set; }
}
```

```csharp
// backend/src/AttendanceApi/Data/AppDbContext.cs
using AttendanceApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>();
            e.Property(t => t.DeviceVendor).HasConversion<string>();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Role).HasConversion<string>();
            e.HasIndex(u => u.Email).IsUnique();
        });
    }
}
```

- [ ] **Step 2: Write the failing auth test**

```csharp
// backend/tests/AttendanceApi.Tests/AuthTests.cs
using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithValidOperatorCredentials_ReturnsToken()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = new PasswordHasher<User>();
            var user = new User { Email = "operator@zak.test", PasswordHash = "", Role = UserRole.Operator };
            user.PasswordHash = hasher.HashPassword(user, "correct-horse");
            db.Users.Add(user);
            db.SaveChanges();
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("operator@zak.test", "correct-horse"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(body!.Token));
        Assert.Equal("Operator", body.Role);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@zak.test", "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter AuthTests`
Expected: FAIL — compile error, `LoginRequest`/`AuthController` don't exist.

- [ ] **Step 4: Write DTOs, token service, policies, and controller**

```csharp
// backend/src/AttendanceApi/Dtos/AuthDtos.cs
namespace AttendanceApi.Dtos;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string Role, Guid? TenantId);
```

```csharp
// backend/src/AttendanceApi/Auth/JwtTokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AttendanceApi.Entities;
using Microsoft.IdentityModel.Tokens;

namespace AttendanceApi.Auth;

public class JwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config) => _config = config;

    public string IssueToken(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        if (user.TenantId is Guid tenantId)
            claims.Add(new Claim("tenant_id", tenantId.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Issuer"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

```csharp
// backend/src/AttendanceApi/Auth/AuthorizationPolicies.cs
using Microsoft.AspNetCore.Authorization;

namespace AttendanceApi.Auth;

public static class AuthorizationPolicies
{
    public const string Operator = "Operator";
    public const string TenantAdmin = "TenantAdmin";

    public static void AddAttendancePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Operator, p => p.RequireRole("Operator"));
        options.AddPolicy(TenantAdmin, p => p.RequireRole("TenantAdmin"));
    }
}
```

```csharp
// backend/src/AttendanceApi/Auth/ClaimsPrincipalExtensions.cs
using System.Security.Claims;

namespace AttendanceApi.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid? TenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("tenant_id")?.Value;
        return value is null ? null : Guid.Parse(value);
    }
}
```

```csharp
// backend/src/AttendanceApi/Controllers/AuthController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokens;

    public AuthController(AppDbContext db, JwtTokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
        if (user is null) return Unauthorized();

        var hasher = new PasswordHasher<Entities.User>();
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return Unauthorized();

        var token = _tokens.IssueToken(user);
        return new LoginResponse(token, user.Role.ToString(), user.TenantId);
    }
}
```

- [ ] **Step 5: Lock down `TenantsController` and wire auth into `Program.cs`**

```csharp
// backend/src/AttendanceApi/Controllers/TenantsController.cs
// Add at the top of the class, right after [Route("api/tenants")]:
[Authorize(Policy = AttendanceApi.Auth.AuthorizationPolicies.Operator)]
```

```csharp
// backend/src/AttendanceApi/Program.cs
using System.Text;
using AttendanceApi.Auth;
using AttendanceApi.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Issuer"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
    });

builder.Services.AddAuthorization(options => options.AddAttendancePolicies());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
```

Add to `appsettings.Development.json` (dev-only secret; production value comes
from an environment variable, see Task 9):

```json
{
  "Jwt": {
    "Key": "dev-only-super-secret-key-change-me-1234567890",
    "Issuer": "attendance-api-dev"
  }
}
```

- [ ] **Step 6: Update the tenant CRUD test to authenticate as Operator**

```csharp
// backend/tests/AttendanceApi.Tests/TenantsControllerTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class TenantsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TenantsControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> OperatorClientAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = new PasswordHasher<User>();
            var user = new User { Email = $"op-{Guid.NewGuid()}@zak.test", PasswordHash = "", Role = UserRole.Operator };
            user.PasswordHash = hasher.HashPassword(user, "correct-horse");
            db.Users.Add(user);
            db.SaveChanges();

            var client = _factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(user.Email, "correct-horse"));
            var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
            return client;
        }
    }

    [Fact]
    public async Task CreateThenGet_RoundTrips()
    {
        var client = await OperatorClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/tenants",
            new CreateTenantRequest("Acme Foods", "Zk4500"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantResponse>();

        var getResponse = await client.GetAsync($"/api/tenants/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<TenantResponse>();

        Assert.Equal("Acme Foods", fetched!.Name);
        Assert.Equal("Active", fetched.Status);
    }

    [Fact]
    public async Task Create_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tenants",
            new CreateTenantRequest("Acme Foods", "Zk4500"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Also add `Jwt:Key` / `Jwt:Issuer` to the test project so token validation
works in-process — simplest is to add an `appsettings.json` to the test
project with the same dev key, copied to output:

```json
// backend/tests/AttendanceApi.Tests/appsettings.json
{
  "Jwt": {
    "Key": "dev-only-super-secret-key-change-me-1234567890",
    "Issuer": "attendance-api-dev"
  }
}
```

Add to `AttendanceApi.Tests.csproj` inside an `<ItemGroup>`:

```xml
<Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
```

`ApiFactory` inherits the host's configuration providers, so this file is
picked up automatically since `WebApplicationFactory` uses `"Development"`
as the environment by default, loading `appsettings.Development.json` from
the web project's content root — override that by also placing the same
`Jwt` section directly in the test project's own `appsettings.json` as
shown above, which `ConfigureAppConfiguration` merges in ahead of it. No
code change needed beyond adding the file.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test backend/tests/AttendanceApi.Tests`
Expected: PASS (all tests, including the new unauthorized case)

- [ ] **Step 8: Commit**

```bash
git add backend
git commit -m "feat: add JWT auth with Operator/TenantAdmin roles"
```

---

### Task 4: Station entity + StationKey auth + StationsController

**Files:**
- Create: `backend/src/AttendanceApi/Entities/Station.cs`
- Create: `backend/src/AttendanceApi/Dtos/StationDtos.cs`
- Create: `backend/src/AttendanceApi/Services/StationKeyGenerator.cs`
- Create: `backend/src/AttendanceApi/Auth/StationKeyAuthHandler.cs`
- Create: `backend/src/AttendanceApi/Controllers/StationsController.cs`
- Modify: `backend/src/AttendanceApi/Data/AppDbContext.cs`
- Modify: `backend/src/AttendanceApi/Program.cs`
- Test: `backend/tests/AttendanceApi.Tests/StationsControllerTests.cs`

**Interfaces:**
- Consumes: `Tenant` (Task 2), `AuthorizationPolicies.Operator` (Task 3).
- Produces: `Station { Guid Id, Guid TenantId, string Name, DeviceVendor
  DeviceVendor, DateTimeOffset? LastSyncedAt }`; auth scheme name
  `"StationKey"`, populating `ClaimsPrincipal` with `tenant_id` and
  `station_id` claims — consumed by Tasks 6 and 7 via
  `[Authorize(AuthenticationSchemes = "StationKey")]` and a new
  `ClaimsPrincipalExtensions.StationId(this ClaimsPrincipal) -> Guid?`.

- [ ] **Step 1: Write the entity**

```csharp
// backend/src/AttendanceApi/Entities/Station.cs
namespace AttendanceApi.Entities;

public class Station
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public DeviceVendor DeviceVendor { get; set; }
    public required string ApiKeyHash { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
```

- [ ] **Step 2: Register it on the DbContext**

```csharp
// backend/src/AttendanceApi/Data/AppDbContext.cs
// Add DbSet:
public DbSet<Station> Stations => Set<Station>();

// In OnModelCreating, add:
modelBuilder.Entity<Station>(e =>
{
    e.Property(s => s.DeviceVendor).HasConversion<string>();
    e.HasIndex(s => s.ApiKeyHash).IsUnique();
});
```

- [ ] **Step 3: Write the key generator (plaintext shown once, hash stored)**

```csharp
// backend/src/AttendanceApi/Services/StationKeyGenerator.cs
using System.Security.Cryptography;
using System.Text;

namespace AttendanceApi.Services;

public static class StationKeyGenerator
{
    public static (string PlaintextKey, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToBase64String(bytes).Replace("=", "").Replace("+", "").Replace("/", "");
        return (plaintext, Hash(plaintext));
    }

    public static string Hash(string plaintextKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 4: Write the failing station test**

```csharp
// backend/tests/AttendanceApi.Tests/StationsControllerTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class StationsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StationsControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid TenantId)> OperatorClientWithTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = new PasswordHasher<User>();
        var user = new User { Email = $"op-{Guid.NewGuid()}@zak.test", PasswordHash = "", Role = UserRole.Operator };
        user.PasswordHash = hasher.HashPassword(user, "correct-horse");
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        db.Users.Add(user);
        db.Tenants.Add(tenant);
        db.SaveChanges();

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-horse"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return (client, tenant.Id);
    }

    [Fact]
    public async Task CreateStation_ReturnsPlaintextKeyOnce_AndRejectsMismatchedVendor()
    {
        var (client, tenantId) = await OperatorClientWithTenantAsync();

        var mismatched = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/stations",
            new CreateStationRequest("Front Desk", "Secugen"));
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);

        var created = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/stations",
            new CreateStationRequest("Front Desk", "Zk4500"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<StationCreatedResponse>();
        Assert.False(string.IsNullOrEmpty(body!.ApiKey));
    }

    [Fact]
    public async Task Health_WithStationKey_Authenticates()
    {
        var (client, tenantId) = await OperatorClientWithTenantAsync();
        var created = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/stations",
            new CreateStationRequest("Front Desk", "Zk4500"));
        var body = await created.Content.ReadFromJsonAsync<StationCreatedResponse>();

        var stationClient = _factory.CreateClient();
        stationClient.DefaultRequestHeaders.Add("X-Station-Key", body!.ApiKey);
        var response = await stationClient.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter StationsControllerTests`
Expected: FAIL — compile error, `StationDtos`/`StationsController` don't exist.

- [ ] **Step 6: Write the DTOs, auth handler, and controller**

```csharp
// backend/src/AttendanceApi/Dtos/StationDtos.cs
namespace AttendanceApi.Dtos;

public record CreateStationRequest(string Name, string DeviceVendor);
public record StationResponse(Guid Id, Guid TenantId, string Name, string DeviceVendor, DateTimeOffset? LastSyncedAt);
public record StationCreatedResponse(Guid Id, string Name, string ApiKey);
```

```csharp
// backend/src/AttendanceApi/Auth/StationKeyAuthHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using AttendanceApi.Data;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AttendanceApi.Auth;

public static class StationKeySchemes
{
    public const string Name = "StationKey";
}

public class StationKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDbContext _db;

    public StationKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Station-Key", out var key) || string.IsNullOrEmpty(key))
            return AuthenticateResult.NoResult();

        var hash = StationKeyGenerator.Hash(key!);
        var station = await _db.Stations.SingleOrDefaultAsync(s => s.ApiKeyHash == hash);
        if (station is null) return AuthenticateResult.Fail("Invalid station key");

        var claims = new[]
        {
            new Claim("tenant_id", station.TenantId.ToString()),
            new Claim("station_id", station.Id.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
```

```csharp
// backend/src/AttendanceApi/Controllers/StationsController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/stations")]
[Authorize(Policy = AuthorizationPolicies.Operator)]
public class StationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StationsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<StationResponse>>> List(Guid tenantId)
    {
        var stations = await _db.Stations.Where(s => s.TenantId == tenantId).ToListAsync();
        return stations.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StationCreatedResponse>> Create(Guid tenantId, CreateStationRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        var vendor = Enum.Parse<DeviceVendor>(request.DeviceVendor);
        if (vendor != tenant.DeviceVendor)
            return BadRequest($"Station vendor must match tenant vendor ({tenant.DeviceVendor}).");

        var (plaintextKey, hash) = StationKeyGenerator.Generate();
        var station = new Station
        {
            TenantId = tenantId,
            Name = request.Name,
            DeviceVendor = vendor,
            ApiKeyHash = hash,
        };
        _db.Stations.Add(station);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { tenantId }, new StationCreatedResponse(station.Id, station.Name, plaintextKey));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid tenantId, Guid id)
    {
        var station = await _db.Stations.SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (station is null) return NotFound();

        _db.Stations.Remove(station);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static StationResponse ToResponse(Station s) =>
        new(s.Id, s.TenantId, s.Name, s.DeviceVendor.ToString(), s.LastSyncedAt);
}
```

Add `StationId()` to `ClaimsPrincipalExtensions.cs`:

```csharp
public static Guid? StationId(this ClaimsPrincipal principal)
{
    var value = principal.FindFirst("station_id")?.Value;
    return value is null ? null : Guid.Parse(value);
}
```

- [ ] **Step 7: Register the scheme and allow it on the health endpoint**

```csharp
// backend/src/AttendanceApi/Program.cs
// Change the AddAuthentication(...) chain to also add the station scheme:
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options => { /* unchanged from Task 3 */ })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AttendanceApi.Auth.StationKeyAuthHandler>(
        AttendanceApi.Auth.StationKeySchemes.Name, _ => { });
```

`HealthController.Get()` is already `[AllowAnonymous]`, so a request
carrying a valid `X-Station-Key` still reaches it (anonymous endpoints skip
the auth requirement but don't reject a supplied scheme) — no controller
change needed for the Task 6 test to pass; `HandleAuthenticateAsync` still
runs when `[AllowAnonymous]` is present as long as the request supplies the
header, since ASP.NET Core evaluates authentication before the anonymous
short-circuit only affects the *authorization* requirement, not whether the
handler runs. Verify this in Step 8; if the test fails because the handler
never runs, add `[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]`
to a dedicated `GET /api/health/station` action instead and point the test
there.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter StationsControllerTests`
Expected: PASS. If the station-key health check fails per the note in Step
7, apply the fallback described there, then re-run.

- [ ] **Step 9: Commit**

```bash
git add backend
git commit -m "feat: add Station entity, API-key generation, and StationKey auth scheme"
```

---

### Task 5: Employee + Shift entities and tenant-admin-scoped CRUD

**Files:**
- Create: `backend/src/AttendanceApi/Entities/Employee.cs`
- Create: `backend/src/AttendanceApi/Entities/Shift.cs`
- Create: `backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`
- Create: `backend/src/AttendanceApi/Dtos/ShiftDtos.cs`
- Create: `backend/src/AttendanceApi/Controllers/EmployeesController.cs`
- Create: `backend/src/AttendanceApi/Controllers/ShiftsController.cs`
- Create: `backend/src/AttendanceApi/Controllers/EmployeeLookupController.cs`
- Modify: `backend/src/AttendanceApi/Data/AppDbContext.cs`
- Test: `backend/tests/AttendanceApi.Tests/EmployeesControllerTests.cs`
- Test: `backend/tests/AttendanceApi.Tests/ShiftsControllerTests.cs`
- Test: `backend/tests/AttendanceApi.Tests/EmployeeLookupControllerTests.cs`

**Interfaces:**
- Consumes: `AuthorizationPolicies.TenantAdmin`,
  `ClaimsPrincipalExtensions.TenantId()` (Task 3), `StationKeySchemes.Name`,
  `ClaimsPrincipalExtensions.StationId()` (Task 4).
- Produces: `Employee { Guid Id, Guid TenantId, string EmployeeCode, string
  Name, Guid? ShiftId }`; `Shift { Guid Id, Guid TenantId, string Name,
  TimeOnly StartTime, TimeOnly EndTime, int GraceMinutes, PunchMode
  PunchMode, TimeOnly? BreakStart, TimeOnly? BreakEnd, int?
  AllowedBreakMinutes }` — consumed by Task 8 (attendance view) via
  `Employee.Name`/`Employee.TenantId`. Also produces
  `GET /api/employees/lookup?code={code}` (StationKey auth) →
  `EmployeeLookupResponse { Guid EmployeeId, string EmployeeCode, string
  Name }` or `404` — this is how the desktop agent plan's punch flow
  resolves an employee-entered ID/PIN to an `EmployeeId` before fetching a
  template (see the desktop agent plan, Task 4).

- [ ] **Step 1: Write the entities**

```csharp
// backend/src/AttendanceApi/Entities/Employee.cs
namespace AttendanceApi.Entities;

public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string Name { get; set; }
    public Guid? ShiftId { get; set; }
}
```

```csharp
// backend/src/AttendanceApi/Entities/Shift.cs
namespace AttendanceApi.Entities;

public enum PunchMode { TwoPunch, FourPunch }

public class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int GraceMinutes { get; set; }
    public PunchMode PunchMode { get; set; } = PunchMode.TwoPunch;
    public TimeOnly? BreakStart { get; set; }
    public TimeOnly? BreakEnd { get; set; }
    public int? AllowedBreakMinutes { get; set; }
}
```

- [ ] **Step 2: Register both on the DbContext**

```csharp
// backend/src/AttendanceApi/Data/AppDbContext.cs
// Add DbSets:
public DbSet<Employee> Employees => Set<Employee>();
public DbSet<Shift> Shifts => Set<Shift>();

// In OnModelCreating, add:
modelBuilder.Entity<Employee>(e =>
{
    e.HasIndex(x => new { x.TenantId, x.EmployeeCode }).IsUnique();
});

modelBuilder.Entity<Shift>(e =>
{
    e.Property(s => s.PunchMode).HasConversion<string>();
});
```

- [ ] **Step 3: Write the failing shift test (shifts have no FK dependency, build first)**

```csharp
// backend/tests/AttendanceApi.Tests/ShiftsControllerTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class ShiftsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ShiftsControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> TenantAdminClientAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Email = $"admin-{Guid.NewGuid()}@zak.test",
            PasswordHash = "",
            Role = UserRole.TenantAdmin,
            TenantId = tenantId,
        };
        user.PasswordHash = hasher.HashPassword(user, "correct-horse");
        db.Users.Add(user);
        db.SaveChanges();

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-horse"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private Guid CreateTenant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant.Id;
    }

    [Fact]
    public async Task CreateShift_ThenList_ScopedToOwnTenant()
    {
        var tenantId = CreateTenant();
        var otherTenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);
        var otherClient = await TenantAdminClientAsync(otherTenantId);

        await client.PostAsJsonAsync("/api/shifts", new CreateShiftRequest(
            "Day Shift", new TimeOnly(9, 0), new TimeOnly(17, 0), 10, "TwoPunch", null, null, null));
        await otherClient.PostAsJsonAsync("/api/shifts", new CreateShiftRequest(
            "Night Shift", new TimeOnly(21, 0), new TimeOnly(5, 0), 5, "TwoPunch", null, null, null));

        var response = await client.GetAsync("/api/shifts");
        var shifts = await response.Content.ReadFromJsonAsync<List<ShiftResponse>>();

        Assert.Single(shifts!);
        Assert.Equal("Day Shift", shifts![0].Name);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter ShiftsControllerTests`
Expected: FAIL — compile error, DTOs/controller don't exist.

- [ ] **Step 5: Write shift DTOs and controller**

```csharp
// backend/src/AttendanceApi/Dtos/ShiftDtos.cs
namespace AttendanceApi.Dtos;

public record CreateShiftRequest(
    string Name, TimeOnly StartTime, TimeOnly EndTime, int GraceMinutes,
    string PunchMode, TimeOnly? BreakStart, TimeOnly? BreakEnd, int? AllowedBreakMinutes);

public record ShiftResponse(
    Guid Id, string Name, TimeOnly StartTime, TimeOnly EndTime, int GraceMinutes,
    string PunchMode, TimeOnly? BreakStart, TimeOnly? BreakEnd, int? AllowedBreakMinutes);
```

```csharp
// backend/src/AttendanceApi/Controllers/ShiftsController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/shifts")]
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
public class ShiftsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ShiftsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<ShiftResponse>>> List()
    {
        var tenantId = User.TenantId()!.Value;
        var shifts = await _db.Shifts.Where(s => s.TenantId == tenantId).ToListAsync();
        return shifts.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<ShiftResponse>> Create(CreateShiftRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var shift = new Shift
        {
            TenantId = tenantId,
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            GraceMinutes = request.GraceMinutes,
            PunchMode = Enum.Parse<PunchMode>(request.PunchMode),
            BreakStart = request.BreakStart,
            BreakEnd = request.BreakEnd,
            AllowedBreakMinutes = request.AllowedBreakMinutes,
        };
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), null, ToResponse(shift));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ShiftResponse>> Update(Guid id, CreateShiftRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var shift = await _db.Shifts.SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (shift is null) return NotFound();

        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.GraceMinutes = request.GraceMinutes;
        shift.PunchMode = Enum.Parse<PunchMode>(request.PunchMode);
        shift.BreakStart = request.BreakStart;
        shift.BreakEnd = request.BreakEnd;
        shift.AllowedBreakMinutes = request.AllowedBreakMinutes;
        await _db.SaveChangesAsync();
        return ToResponse(shift);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var shift = await _db.Shifts.SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (shift is null) return NotFound();

        _db.Shifts.Remove(shift);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static ShiftResponse ToResponse(Shift s) => new(
        s.Id, s.Name, s.StartTime, s.EndTime, s.GraceMinutes, s.PunchMode.ToString(),
        s.BreakStart, s.BreakEnd, s.AllowedBreakMinutes);
}
```

- [ ] **Step 6: Run shift test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter ShiftsControllerTests`
Expected: PASS

- [ ] **Step 7: Write the failing employee test**

```csharp
// backend/tests/AttendanceApi.Tests/EmployeesControllerTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class EmployeesControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EmployeesControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> TenantAdminClientAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Email = $"admin-{Guid.NewGuid()}@zak.test",
            PasswordHash = "",
            Role = UserRole.TenantAdmin,
            TenantId = tenantId,
        };
        user.PasswordHash = hasher.HashPassword(user, "correct-horse");
        db.Users.Add(user);
        db.SaveChanges();

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-horse"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private Guid CreateTenant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant.Id;
    }

    [Fact]
    public async Task CreateEmployee_ThenDuplicateCode_Returns400()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var first = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "John Roe", null));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }
}
```

- [ ] **Step 8: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter EmployeesControllerTests`
Expected: FAIL — compile error, DTOs/controller don't exist.

- [ ] **Step 9: Write employee DTOs and controller**

```csharp
// backend/src/AttendanceApi/Dtos/EmployeeDtos.cs
namespace AttendanceApi.Dtos;

public record CreateEmployeeRequest(string EmployeeCode, string Name, Guid? ShiftId);
public record EmployeeResponse(Guid Id, string EmployeeCode, string Name, Guid? ShiftId);
```

```csharp
// backend/src/AttendanceApi/Controllers/EmployeesController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _db;

    public EmployeesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<EmployeeResponse>>> List()
    {
        var tenantId = User.TenantId()!.Value;
        var employees = await _db.Employees.Where(e => e.TenantId == tenantId).ToListAsync();
        return employees.Select(ToResponse).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Get(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        return employee is null ? NotFound() : ToResponse(employee);
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeResponse>> Create(CreateEmployeeRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var exists = await _db.Employees.AnyAsync(e => e.TenantId == tenantId && e.EmployeeCode == request.EmployeeCode);
        if (exists) return BadRequest($"Employee code '{request.EmployeeCode}' is already in use.");

        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeCode = request.EmployeeCode,
            Name = request.Name,
            ShiftId = request.ShiftId,
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToResponse(employee));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Update(Guid id, CreateEmployeeRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        employee.Name = request.Name;
        employee.ShiftId = request.ShiftId;
        await _db.SaveChangesAsync();
        return ToResponse(employee);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static EmployeeResponse ToResponse(Employee e) => new(e.Id, e.EmployeeCode, e.Name, e.ShiftId);
}
```

- [ ] **Step 10: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter EmployeesControllerTests`
Expected: PASS

- [ ] **Step 11: Write the failing employee lookup test**

```csharp
// backend/tests/AttendanceApi.Tests/EmployeeLookupControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class EmployeeLookupControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EmployeeLookupControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private string SeedTenantEmployeeAndStation(out Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe" };
        var (plaintextKey, hash) = StationKeyGenerator.Generate();
        var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = hash };
        db.Tenants.Add(tenant);
        db.Employees.Add(employee);
        db.Stations.Add(station);
        db.SaveChanges();
        employeeId = employee.Id;
        return plaintextKey;
    }

    [Fact]
    public async Task Lookup_ByCode_ReturnsEmployeeId()
    {
        var stationKey = SeedTenantEmployeeAndStation(out var employeeId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=E001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EmployeeLookupResponse>();
        Assert.Equal(employeeId, body!.EmployeeId);
    }

    [Fact]
    public async Task Lookup_UnknownCode_ReturnsNotFound()
    {
        var stationKey = SeedTenantEmployeeAndStation(out _);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=NOPE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 12: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter EmployeeLookupControllerTests`
Expected: FAIL — compile error, `EmployeeLookupResponse`/`EmployeeLookupController` don't exist.

- [ ] **Step 13: Write the lookup DTO and controller**

Add to `backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`:

```csharp
public record EmployeeLookupResponse(Guid EmployeeId, string EmployeeCode, string Name);
```

```csharp
// backend/src/AttendanceApi/Controllers/EmployeeLookupController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/employees/lookup")]
[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
public class EmployeeLookupController : ControllerBase
{
    private readonly AppDbContext _db;

    public EmployeeLookupController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<EmployeeLookupResponse>> Lookup([FromQuery] string code)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.EmployeeCode == code);
        if (employee is null) return NotFound();

        return new EmployeeLookupResponse(employee.Id, employee.EmployeeCode, employee.Name);
    }
}
```

This is a separate controller (rather than an action on
`EmployeesController`) because `EmployeesController` carries a
class-level `[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]` for
the JWT scheme — ASP.NET Core combines class- and method-level
`[Authorize]` attributes with AND semantics, so a method-level
`[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]` on that
controller would require *both* a valid JWT TenantAdmin token *and* a
station key on the same request, which no caller can satisfy.

- [ ] **Step 14: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter EmployeeLookupControllerTests`
Expected: PASS

- [ ] **Step 15: Commit**

```bash
git add backend
git commit -m "feat: add Employee and Shift entities with tenant-scoped CRUD"
```

---

### Task 6: Fingerprint template storage (encrypted) via StationKey auth

**Files:**
- Create: `backend/src/AttendanceApi/Entities/FingerprintTemplate.cs`
- Create: `backend/src/AttendanceApi/Dtos/TemplateDtos.cs`
- Create: `backend/src/AttendanceApi/Services/ITemplateCipher.cs`
- Create: `backend/src/AttendanceApi/Services/AesTemplateCipher.cs`
- Create: `backend/src/AttendanceApi/Controllers/TemplatesController.cs`
- Modify: `backend/src/AttendanceApi/Data/AppDbContext.cs`
- Modify: `backend/src/AttendanceApi/Program.cs`
- Modify: `backend/src/AttendanceApi/appsettings.Development.json`
- Modify: `backend/tests/AttendanceApi.Tests/appsettings.json`
- Test: `backend/tests/AttendanceApi.Tests/TemplatesControllerTests.cs`

**Interfaces:**
- Consumes: `Station`/`Employee` (Tasks 4–5), `StationKeySchemes.Name`
  (Task 4), `ClaimsPrincipalExtensions.TenantId()/StationId()`.
- Produces: `FingerprintTemplate { Guid Id, Guid EmployeeId, DeviceVendor
  Vendor, byte[] TemplateDataEncrypted, DateTimeOffset EnrolledAt }`;
  `ITemplateCipher.Encrypt/Decrypt(byte[]) -> byte[]`, reused nowhere else
  in Phase 1 but kept as an interface so a KMS-backed implementation can
  replace it later without touching the controller.

- [ ] **Step 1: Write the entity, cipher interface, and AES implementation**

```csharp
// backend/src/AttendanceApi/Entities/FingerprintTemplate.cs
namespace AttendanceApi.Entities;

public class FingerprintTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public DeviceVendor Vendor { get; set; }
    public required byte[] TemplateDataEncrypted { get; set; }
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
}
```

```csharp
// backend/src/AttendanceApi/Services/ITemplateCipher.cs
namespace AttendanceApi.Services;

public interface ITemplateCipher
{
    byte[] Encrypt(byte[] plaintext);
    byte[] Decrypt(byte[] ciphertext);
}
```

```csharp
// backend/src/AttendanceApi/Services/AesTemplateCipher.cs
using System.Security.Cryptography;

namespace AttendanceApi.Services;

public class AesTemplateCipher : ITemplateCipher
{
    private readonly byte[] _key;

    public AesTemplateCipher(IConfiguration config)
    {
        _key = Convert.FromBase64String(config["Templates:EncryptionKey"]!);
    }

    public byte[] Encrypt(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        return aes.IV.Concat(cipherBytes).ToArray();
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = ciphertext[..16];
        var cipherBytes = ciphertext[16..];
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }
}
```

Add `using System.Linq;` at the top of `AesTemplateCipher.cs`.

- [ ] **Step 2: Register the entity on the DbContext and the cipher in DI**

```csharp
// backend/src/AttendanceApi/Data/AppDbContext.cs
// Add DbSet:
public DbSet<FingerprintTemplate> FingerprintTemplates => Set<FingerprintTemplate>();

// In OnModelCreating, add:
modelBuilder.Entity<FingerprintTemplate>(e =>
{
    e.Property(t => t.Vendor).HasConversion<string>();
    e.HasIndex(t => t.EmployeeId).IsUnique();
});
```

```csharp
// backend/src/AttendanceApi/Program.cs
// Add alongside the other service registrations:
builder.Services.AddSingleton<AttendanceApi.Services.ITemplateCipher, AttendanceApi.Services.AesTemplateCipher>();
```

Add a dev-only 32-byte base64 key to both `appsettings.Development.json`
and the test project's `appsettings.json`:

```json
{
  "Templates": {
    "EncryptionKey": "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI="
  }
}
```

- [ ] **Step 3: Write the failing template test**

```csharp
// backend/tests/AttendanceApi.Tests/TemplatesControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class TemplatesControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TemplatesControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private (Guid TenantId, Guid EmployeeId, string StationKey) SeedTenantEmployeeAndStation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe" };
        var (plaintextKey, hash) = StationKeyGenerator.Generate();
        var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = hash };
        db.Tenants.Add(tenant);
        db.Employees.Add(employee);
        db.Stations.Add(station);
        db.SaveChanges();
        return (tenant.Id, employee.Id, plaintextKey);
    }

    [Fact]
    public async Task EnrollThenFetch_RoundTripsPlaintextTemplate()
    {
        var (_, employeeId, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var templateBytes = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        var enroll = await client.PostAsJsonAsync("/api/templates", new EnrollTemplateRequest(employeeId, templateBytes));
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);

        var fetch = await client.GetAsync($"/api/templates/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
        var body = await fetch.Content.ReadFromJsonAsync<TemplateResponse>();

        Assert.Equal(templateBytes, body!.TemplateData);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = db.FingerprintTemplates.Single(t => t.EmployeeId == employeeId);
        Assert.NotEqual(Convert.FromBase64String(templateBytes), stored.TemplateDataEncrypted);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter TemplatesControllerTests`
Expected: FAIL — compile error, DTOs/controller don't exist.

- [ ] **Step 5: Write template DTOs and controller**

```csharp
// backend/src/AttendanceApi/Dtos/TemplateDtos.cs
namespace AttendanceApi.Dtos;

public record EnrollTemplateRequest(Guid EmployeeId, string TemplateData);
public record TemplateResponse(Guid EmployeeId, string TemplateData, DateTimeOffset EnrolledAt);
```

```csharp
// backend/src/AttendanceApi/Controllers/TemplatesController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/templates")]
[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
public class TemplatesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITemplateCipher _cipher;

    public TemplatesController(AppDbContext db, ITemplateCipher cipher)
    {
        _db = db;
        _cipher = cipher;
    }

    [HttpPost]
    public async Task<IActionResult> Enroll(EnrollTemplateRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        var station = await _db.Stations.SingleAsync(s => s.Id == User.StationId()!.Value);
        var plaintext = Convert.FromBase64String(request.TemplateData);
        var encrypted = _cipher.Encrypt(plaintext);

        var existing = await _db.FingerprintTemplates.SingleOrDefaultAsync(t => t.EmployeeId == request.EmployeeId);
        if (existing is null)
        {
            _db.FingerprintTemplates.Add(new FingerprintTemplate
            {
                EmployeeId = request.EmployeeId,
                Vendor = station.DeviceVendor,
                TemplateDataEncrypted = encrypted,
            });
        }
        else
        {
            existing.TemplateDataEncrypted = encrypted;
            existing.Vendor = station.DeviceVendor;
            existing.EnrolledAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("{employeeId:guid}")]
    public async Task<ActionResult<TemplateResponse>> Fetch(Guid employeeId)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        var template = await _db.FingerprintTemplates.SingleOrDefaultAsync(t => t.EmployeeId == employeeId);
        if (template is null) return NotFound();

        var plaintext = _cipher.Decrypt(template.TemplateDataEncrypted);
        return new TemplateResponse(employeeId, Convert.ToBase64String(plaintext), template.EnrolledAt);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter TemplatesControllerTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: add encrypted fingerprint template enroll/fetch endpoints"
```

---

### Task 7: Punch ingestion (idempotent, StationKey auth)

**Files:**
- Create: `backend/src/AttendanceApi/Entities/Punch.cs`
- Create: `backend/src/AttendanceApi/Dtos/PunchDtos.cs`
- Create: `backend/src/AttendanceApi/Controllers/PunchesController.cs`
- Modify: `backend/src/AttendanceApi/Data/AppDbContext.cs`
- Test: `backend/tests/AttendanceApi.Tests/PunchesControllerTests.cs`

**Interfaces:**
- Consumes: `Employee`/`Station` (Tasks 4–5), `StationKeySchemes.Name`.
- Produces: `Punch { Guid Id, Guid TenantId, Guid EmployeeId, Guid
  StationId, PunchType PunchType, DateTimeOffset Timestamp, DateTimeOffset
  SyncedAt }` — consumed by Task 8's attendance query.

- [ ] **Step 1: Write the entity**

```csharp
// backend/src/AttendanceApi/Entities/Punch.cs
namespace AttendanceApi.Entities;

public enum PunchType { In, BreakOut, BreakIn, Out }

public class Punch
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid StationId { get; set; }
    public PunchType PunchType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Note `Id` has no default — the desktop agent generates it client-side so
retried syncs are idempotent.

- [ ] **Step 2: Register it on the DbContext**

```csharp
// backend/src/AttendanceApi/Data/AppDbContext.cs
// Add DbSet:
public DbSet<Punch> Punches => Set<Punch>();

// In OnModelCreating, add:
modelBuilder.Entity<Punch>(e =>
{
    e.Property(p => p.PunchType).HasConversion<string>();
});
```

- [ ] **Step 3: Write the failing punch test**

```csharp
// backend/tests/AttendanceApi.Tests/PunchesControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class PunchesControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PunchesControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private (Guid EmployeeId, string StationKey) SeedTenantEmployeeAndStation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe" };
        var (plaintextKey, hash) = StationKeyGenerator.Generate();
        var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = hash };
        db.Tenants.Add(tenant);
        db.Employees.Add(employee);
        db.Stations.Add(station);
        db.SaveChanges();
        return (employee.Id, plaintextKey);
    }

    [Fact]
    public async Task BatchIngest_IsIdempotentByPunchId()
    {
        var (employeeId, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var punchId = Guid.NewGuid();
        var request = new PunchBatchRequest(new List<PunchDto>
        {
            new(punchId, employeeId, "In", DateTimeOffset.UtcNow),
        });

        var first = await client.PostAsJsonAsync("/api/punches/batch", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var retry = await client.PostAsJsonAsync("/api/punches/batch", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.Punches, p => p.Id == punchId);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter PunchesControllerTests`
Expected: FAIL — compile error, DTOs/controller don't exist.

- [ ] **Step 5: Write punch DTOs and controller**

```csharp
// backend/src/AttendanceApi/Dtos/PunchDtos.cs
namespace AttendanceApi.Dtos;

public record PunchDto(Guid Id, Guid EmployeeId, string PunchType, DateTimeOffset Timestamp);
public record PunchBatchRequest(List<PunchDto> Punches);
public record PunchBatchResponse(List<Guid> AcceptedIds);
```

```csharp
// backend/src/AttendanceApi/Controllers/PunchesController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/punches")]
[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
public class PunchesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PunchesController(AppDbContext db) => _db = db;

    [HttpPost("batch")]
    public async Task<ActionResult<PunchBatchResponse>> Batch(PunchBatchRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var stationId = User.StationId()!.Value;

        var incomingIds = request.Punches.Select(p => p.Id).ToList();
        var existingIds = await _db.Punches.Where(p => incomingIds.Contains(p.Id)).Select(p => p.Id).ToListAsync();

        var accepted = new List<Guid>();
        foreach (var dto in request.Punches)
        {
            accepted.Add(dto.Id);
            if (existingIds.Contains(dto.Id)) continue;

            _db.Punches.Add(new Punch
            {
                Id = dto.Id,
                TenantId = tenantId,
                EmployeeId = dto.EmployeeId,
                StationId = stationId,
                PunchType = Enum.Parse<PunchType>(dto.PunchType),
                Timestamp = dto.Timestamp,
            });
        }

        var station = await _db.Stations.SingleAsync(s => s.Id == stationId);
        station.LastSyncedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();
        return new PunchBatchResponse(accepted);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter PunchesControllerTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add backend
git commit -m "feat: add idempotent punch batch ingestion endpoint"
```

---

### Task 8: Daily attendance summary view

**Files:**
- Create: `backend/src/AttendanceApi/Dtos/AttendanceDtos.cs`
- Create: `backend/src/AttendanceApi/Controllers/AttendanceController.cs`
- Test: `backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs`

**Interfaces:**
- Consumes: `Punch`, `Employee` (Tasks 5, 7), `AuthorizationPolicies.TenantAdmin`.
- Produces: `GET /api/attendance/daily?date=YYYY-MM-DD` →
  `List<DailyAttendanceResponse>`. This is the only attendance read Phase 1
  needs; late/hours/anomaly fields are explicitly out of scope here (Phase 2).

- [ ] **Step 1: Write the failing attendance test**

```csharp
// backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class AttendanceControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AttendanceControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DailyView_ReturnsFirstInAndLastOut()
    {
        Guid tenantId, employeeId;
        var day = new DateOnly(2026, 8, 10);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe" };
            var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = "hash" };
            db.Tenants.Add(tenant);
            db.Employees.Add(employee);
            db.Stations.Add(station);
            db.Punches.AddRange(
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = day.ToDateTime(new TimeOnly(9, 2)) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.Out, Timestamp = day.ToDateTime(new TimeOnly(17, 5)) });
            db.SaveChanges();
            tenantId = tenant.Id;
            employeeId = employee.Id;
        }

        var client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = new PasswordHasher<User>();
            var user = new User { Email = $"admin-{Guid.NewGuid()}@zak.test", PasswordHash = "", Role = UserRole.TenantAdmin, TenantId = tenantId };
            user.PasswordHash = hasher.HashPassword(user, "correct-horse");
            db.Users.Add(user);
            db.SaveChanges();

            var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-horse"));
            var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        }

        var response = await client.GetAsync("/api/attendance/daily?date=2026-08-10");
        var rows = await response.Content.ReadFromJsonAsync<List<DailyAttendanceResponse>>();

        var row = Assert.Single(rows!, r => r.EmployeeId == employeeId);
        Assert.Equal(9, row.FirstIn!.Value.Hour);
        Assert.Equal(17, row.LastOut!.Value.Hour);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter AttendanceControllerTests`
Expected: FAIL — compile error, `DailyAttendanceResponse`/`AttendanceController` don't exist.

- [ ] **Step 3: Write the DTO and controller**

```csharp
// backend/src/AttendanceApi/Dtos/AttendanceDtos.cs
namespace AttendanceApi.Dtos;

public record DailyAttendanceResponse(Guid EmployeeId, string EmployeeName, DateTimeOffset? FirstIn, DateTimeOffset? LastOut);
```

```csharp
// backend/src/AttendanceApi/Controllers/AttendanceController.cs
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;

    public AttendanceController(AppDbContext db) => _db = db;

    [HttpGet("daily")]
    public async Task<ActionResult<List<DailyAttendanceResponse>>> Daily([FromQuery] DateOnly date)
    {
        var tenantId = User.TenantId()!.Value;
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var punches = await _db.Punches
            .Where(p => p.TenantId == tenantId && p.Timestamp >= start && p.Timestamp < end)
            .ToListAsync();

        var employeeIds = punches.Select(p => p.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name);

        var rows = punches
            .GroupBy(p => p.EmployeeId)
            .Select(g => new DailyAttendanceResponse(
                g.Key,
                employees.GetValueOrDefault(g.Key, "Unknown"),
                g.Min(p => (DateTimeOffset?)p.Timestamp),
                g.Max(p => (DateTimeOffset?)p.Timestamp)))
            .ToList();

        return rows;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter AttendanceControllerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: add daily attendance summary endpoint"
```

---

### Task 9: Postgres migration, Docker Compose, and operator bootstrap

**Files:**
- Create: `backend/docker-compose.yml`
- Create: `backend/README.md`
- Modify: `backend/src/AttendanceApi/Program.cs`
- Modify: `backend/src/AttendanceApi/appsettings.json`

**Interfaces:**
- Consumes: `AppDbContext` (all prior tasks).
- Produces: an EF Core migration applied automatically on startup in
  non-Development environments, and a seeded Operator account so the very
  first login has credentials.

- [ ] **Step 1: Generate the initial EF Core migration**

```bash
cd backend/src/AttendanceApi
dotnet ef migrations add InitialCreate
```

Expected: a new `Migrations/` folder with `InitialCreate` files. Run
`dotnet test backend/tests/AttendanceApi.Tests` afterward to confirm
nothing broke — Sqlite tests use `EnsureCreated()`, not migrations, so they
should still pass unchanged.

- [ ] **Step 2: Write `docker-compose.yml`**

```yaml
# backend/docker-compose.yml
services:
  db:
    image: postgres:16
    environment:
      POSTGRES_DB: attendance
      POSTGRES_USER: attendance
      POSTGRES_PASSWORD: attendance
    ports:
      - "5432:5432"
    volumes:
      - db-data:/var/lib/postgresql/data

  api:
    build:
      context: .
      dockerfile: src/AttendanceApi/Dockerfile
    depends_on:
      - db
    environment:
      ConnectionStrings__Default: "Host=db;Database=attendance;Username=attendance;Password=attendance"
      Jwt__Key: "${JWT_KEY}"
      Jwt__Issuer: "attendance-api"
      Templates__EncryptionKey: "${TEMPLATE_ENCRYPTION_KEY}"
      Operator__Email: "${OPERATOR_EMAIL}"
      Operator__Password: "${OPERATOR_PASSWORD}"
    ports:
      - "8080:8080"

volumes:
  db-data:
```

`Jwt__Key`, `Templates__EncryptionKey`, `Operator__Email`, and
`Operator__Password` are read from a `.env` file next to
`docker-compose.yml` (not committed) — document this in Step 4's README.

- [ ] **Step 3: Apply migrations and seed the first Operator on startup**

```csharp
// backend/src/AttendanceApi/Program.cs
// Add just before app.Run():
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AttendanceApi.Data.AppDbContext>();
    db.Database.Migrate();

    var operatorEmail = builder.Configuration["Operator:Email"];
    var operatorPassword = builder.Configuration["Operator:Password"];
    if (!string.IsNullOrEmpty(operatorEmail) && !string.IsNullOrEmpty(operatorPassword)
        && !db.Users.Any(u => u.Email == operatorEmail))
    {
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<AttendanceApi.Entities.User>();
        var user = new AttendanceApi.Entities.User
        {
            Email = operatorEmail,
            PasswordHash = "",
            Role = AttendanceApi.Entities.UserRole.Operator,
        };
        user.PasswordHash = hasher.HashPassword(user, operatorPassword);
        db.Users.Add(user);
        db.SaveChanges();
    }
}
```

`db.Database.Migrate()` only runs against a real relational provider — it
will throw against the Sqlite in-memory test database if the test host
also hits this code path. Confirm `ApiFactory` in the test project builds
its own `WebApplicationFactory` (it does, from Task 2) and does not go
through `Program.cs`'s top-level statements a second time; it does, since
`WebApplicationFactory<Program>` re-runs `Program.cs` with the DbContext
swapped afterward via `ConfigureServices`. This means `Migrate()` will run
against Sqlite in tests too, and Sqlite supports EF Core migrations, so
this is safe — but `EnsureCreated()` in `ApiFactory` and `Migrate()` in
`Program.cs` both firing back-to-back on the same connection is redundant,
not broken, since `EnsureCreated()` runs first and `Migrate()` becomes a
no-op if the schema already matches. Verify this holds in Step 4 rather
than assuming it; if migrations conflict with `EnsureCreated()` in tests,
remove `db.Database.EnsureCreated()` from `ApiFactory` and rely solely on
`db.Database.Migrate()` for both environments.

- [ ] **Step 4: Run the full test suite to verify nothing broke**

Run: `dotnet test backend/tests/AttendanceApi.Tests`
Expected: PASS. Apply the fallback from Step 3 if `Migrate()` vs
`EnsureCreated()` conflict surfaces.

- [ ] **Step 5: Write `backend/README.md`**

```markdown
# Attendance API

## Local development

1. Copy `.env.example` to `.env` and fill in `JWT_KEY`,
   `TEMPLATE_ENCRYPTION_KEY` (32 random bytes, base64-encoded — generate
   with `openssl rand -base64 32`), `OPERATOR_EMAIL`, `OPERATOR_PASSWORD`.
2. `docker compose up --build`
3. API is available at `http://localhost:8080`, Swagger UI at
   `http://localhost:8080/swagger` in Development.
4. Log in as the seeded Operator via `POST /api/auth/login` to start
   creating tenants and stations.

## Running tests

`dotnet test backend/tests/AttendanceApi.Tests`

Tests use an in-memory SQLite database — no Docker or Postgres required.
```

Create `backend/.env.example` alongside it:

```
JWT_KEY=
TEMPLATE_ENCRYPTION_KEY=
OPERATOR_EMAIL=
OPERATOR_PASSWORD=
```

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "chore: add Docker Compose, migrations, and operator bootstrap"
```

---

## What Phase 2+ picks up from here

- Template sync endpoint fan-out to multiple stations at one site (today,
  every station fetches directly — fine for single-station Phase 1 sites).
- `daily_attendance` materialization with late/early/hours/anomaly
  computation, driven by `Shift` fields already modeled in Task 5.
- Admin manual correction endpoints for anomalies.
