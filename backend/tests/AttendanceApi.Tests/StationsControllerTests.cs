using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        var response = await stationClient.GetAsync("/api/health/station");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(body.Id, payload.GetProperty("stationId").GetGuid());
    }

    [Fact]
    public async Task Health_Station_WithoutStationKey_ReturnsUnauthorized()
    {
        var stationClient = _factory.CreateClient();
        var response = await stationClient.GetAsync("/api/health/station");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_Station_WithInvalidStationKey_ReturnsUnauthorized()
    {
        var stationClient = _factory.CreateClient();
        stationClient.DefaultRequestHeaders.Add("X-Station-Key", "not-a-real-key");
        var response = await stationClient.GetAsync("/api/health/station");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidDeviceVendor_ReturnsBadRequest()
    {
        var (client, tenantId) = await OperatorClientWithTenantAsync();

        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/stations",
            new CreateStationRequest("Front Desk", "NotARealVendor"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNumericDeviceVendorString_ReturnsBadRequest()
    {
        var (client, tenantId) = await OperatorClientWithTenantAsync();

        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/stations",
            new CreateStationRequest("Front Desk", "99"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
