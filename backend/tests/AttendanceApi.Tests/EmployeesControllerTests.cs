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
}
