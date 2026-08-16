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
