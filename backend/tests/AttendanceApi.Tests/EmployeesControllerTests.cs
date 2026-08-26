using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task CreateEmployee_ReturnsHasFingerprintFalse()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var created = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var employee = await created.Content.ReadFromJsonAsync<EmployeeResponse>();

        Assert.False(employee!.HasFingerprint);
    }

    [Fact]
    public async Task ListEmployees_ReflectsFingerprintEnrollmentStatus()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var enrolledCreated = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var enrolled = await enrolledCreated.Content.ReadFromJsonAsync<EmployeeResponse>();
        var unenrolledCreated = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E002", "John Smith", null));
        var unenrolled = await unenrolledCreated.Content.ReadFromJsonAsync<EmployeeResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cipher = scope.ServiceProvider.GetRequiredService<ITemplateCipher>();
            db.FingerprintTemplates.Add(new FingerprintTemplate
            {
                EmployeeId = enrolled!.Id,
                Vendor = DeviceVendor.Zk4500,
                TemplateDataEncrypted = cipher.Encrypt(new byte[] { 1, 2, 3 }),
            });
            db.SaveChanges();
        }

        var listResponse = await client.GetAsync("/api/employees");
        var rawJson = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"hasFingerprint\"", rawJson);

        var list = JsonSerializer.Deserialize<List<EmployeeResponse>>(
            rawJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.True(list!.Single(e => e.Id == enrolled.Id).HasFingerprint);
        Assert.False(list.Single(e => e.Id == unenrolled!.Id).HasFingerprint);
    }

    [Fact]
    public async Task Employee_FromOtherTenant_IsNotVisibleOrEditable()
    {
        var tenantId = CreateTenant();
        var otherTenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);
        var otherClient = await TenantAdminClientAsync(otherTenantId);

        var created = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var employee = await created.Content.ReadFromJsonAsync<EmployeeResponse>();

        var getResponse = await otherClient.GetAsync($"/api/employees/{employee!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var updateResponse = await otherClient.PutAsJsonAsync($"/api/employees/{employee.Id}",
            new CreateEmployeeRequest("E001", "Hacked Name", null));
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        var deleteResponse = await otherClient.DeleteAsync($"/api/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        var listResponse = await otherClient.GetAsync("/api/employees");
        var list = await listResponse.Content.ReadFromJsonAsync<List<EmployeeResponse>>();
        Assert.Empty(list!);
    }

    [Fact]
    public async Task CreateEmployee_WithNonexistentShiftId_ReturnsBadRequest()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var response = await client.PostAsJsonAsync("/api/employees",
            new CreateEmployeeRequest("E001", "Jane Doe", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEmployee_WithShiftIdFromAnotherTenant_ReturnsBadRequest()
    {
        var tenantId = CreateTenant();
        var otherTenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        Guid otherTenantShiftId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var shift = new Shift { TenantId = otherTenantId, Name = "Night Shift", StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0) };
            db.Shifts.Add(shift);
            db.SaveChanges();
            otherTenantShiftId = shift.Id;
        }

        var response = await client.PostAsJsonAsync("/api/employees",
            new CreateEmployeeRequest("E001", "Jane Doe", otherTenantShiftId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEmployee_WithNonexistentShiftId_ReturnsBadRequest()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var created = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var employee = await created.Content.ReadFromJsonAsync<EmployeeResponse>();

        var response = await client.PutAsJsonAsync($"/api/employees/{employee!.Id}",
            new CreateEmployeeRequest("E001", "Jane Doe", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEmployee_WithShiftIdFromAnotherTenant_ReturnsBadRequest()
    {
        var tenantId = CreateTenant();
        var otherTenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var created = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var employee = await created.Content.ReadFromJsonAsync<EmployeeResponse>();

        Guid otherTenantShiftId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var shift = new Shift { TenantId = otherTenantId, Name = "Night Shift", StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0) };
            db.Shifts.Add(shift);
            db.SaveChanges();
            otherTenantShiftId = shift.Id;
        }

        var response = await client.PutAsJsonAsync($"/api/employees/{employee!.Id}",
            new CreateEmployeeRequest("E001", "Jane Doe", otherTenantShiftId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEmployee_AlsoRemovesTheirFingerprintTemplate()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var created = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var employee = await created.Content.ReadFromJsonAsync<EmployeeResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cipher = scope.ServiceProvider.GetRequiredService<ITemplateCipher>();
            db.FingerprintTemplates.Add(new FingerprintTemplate
            {
                EmployeeId = employee!.Id,
                Vendor = DeviceVendor.Zk4500,
                TemplateDataEncrypted = cipher.Encrypt(new byte[] { 1, 2, 3 }),
            });
            db.SaveChanges();
        }

        var deleteResponse = await client.DeleteAsync($"/api/employees/{employee!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.FingerprintTemplates.AnyAsync(t => t.EmployeeId == employee.Id));
        }
    }
}
