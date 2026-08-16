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

    [Fact]
    public async Task Create_WithInvalidDeviceVendor_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/tenants",
            new CreateTenantRequest("Acme Foods", "NotARealVendor"));

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
    }
}
