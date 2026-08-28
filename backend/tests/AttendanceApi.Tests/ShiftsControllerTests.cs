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

    [Fact]
    public async Task Create_WithInvalidPunchMode_ReturnsBadRequest()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var response = await client.PostAsJsonAsync("/api/shifts", new CreateShiftRequest(
            "Day Shift", new TimeOnly(9, 0), new TimeOnly(17, 0), 10, "NotARealPunchMode", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNumericPunchModeString_ReturnsBadRequest()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var response = await client.PostAsJsonAsync("/api/shifts", new CreateShiftRequest(
            "Day Shift", new TimeOnly(9, 0), new TimeOnly(17, 0), 10, "99", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidPunchMode_ReturnsBadRequest()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var created = await client.PostAsJsonAsync("/api/shifts", new CreateShiftRequest(
            "Day Shift", new TimeOnly(9, 0), new TimeOnly(17, 0), 10, "TwoPunch", null, null, null));
        var shift = await created.Content.ReadFromJsonAsync<ShiftResponse>();

        var response = await client.PutAsJsonAsync($"/api/shifts/{shift!.Id}", new CreateShiftRequest(
            "Day Shift", new TimeOnly(9, 0), new TimeOnly(17, 0), 10, "NotARealPunchMode", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Shift_FromOtherTenant_IsNotEditableOrDeletable()
    {
        var tenantId = CreateTenant();
        var otherTenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);
        var otherClient = await TenantAdminClientAsync(otherTenantId);

        var created = await client.PostAsJsonAsync("/api/shifts", new CreateShiftRequest(
            "Day Shift", new TimeOnly(9, 0), new TimeOnly(17, 0), 10, "TwoPunch", null, null, null));
        var shift = await created.Content.ReadFromJsonAsync<ShiftResponse>();

        var updateResponse = await otherClient.PutAsJsonAsync($"/api/shifts/{shift!.Id}", new CreateShiftRequest(
            "Hacked Shift", new TimeOnly(0, 0), new TimeOnly(1, 0), 0, "FourPunch", null, null, null));
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        var deleteResponse = await otherClient.DeleteAsync($"/api/shifts/{shift.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }
}
