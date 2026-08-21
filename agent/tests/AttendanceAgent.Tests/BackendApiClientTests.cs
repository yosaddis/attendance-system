using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

        Assert.Equal(PunchBatchSubmitResult.Accepted, result);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task SubmitPunchesAsync_HttpBadRequest_ReturnsRejectedByBackend()
    {
        // The backend rejects the WHOLE batch (400) if any single punch in it is invalid — a
        // permanent, batch-level verdict that retrying won't fix. This must be distinguished from a
        // transient/server failure so the caller can drop the poison batch instead of retrying it
        // forever.
        using var db = DbWithSettings();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = new BackendApiClient(new HttpClient(handler), db);
        var punch = new QueuedPunch { Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), PunchType = "In", Timestamp = DateTimeOffset.UtcNow };

        var result = await client.SubmitPunchesAsync(new List<QueuedPunch> { punch });

        Assert.Equal(PunchBatchSubmitResult.RejectedByBackend, result);
    }

    [Fact]
    public async Task SubmitPunchesAsync_HttpServerError_ReturnsTransientFailure()
    {
        using var db = DbWithSettings();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new BackendApiClient(new HttpClient(handler), db);
        var punch = new QueuedPunch { Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), PunchType = "In", Timestamp = DateTimeOffset.UtcNow };

        var result = await client.SubmitPunchesAsync(new List<QueuedPunch> { punch });

        Assert.Equal(PunchBatchSubmitResult.TransientFailure, result);
    }

    [Fact]
    public async Task FetchTemplateAsync_SendsStationKeyHeader()
    {
        // Only the lookup call was ever asserted for the station-key header; template fetch and
        // punch submit went unchecked. All three request types must carry it.
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

        await client.FetchTemplateAsync(Guid.NewGuid());

        Assert.Equal("secret-key", handler.LastRequest!.Headers.GetValues("X-Station-Key").Single());
    }

    [Fact]
    public async Task SubmitPunchesAsync_SendsStationKeyHeader_AndCorrectJsonBodyShape()
    {
        using var db = DbWithSettings();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new BackendApiClient(new HttpClient(handler), db);
        var punchId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-08-16T12:34:56Z");
        var punch = new QueuedPunch { Id = punchId, EmployeeId = employeeId, PunchType = "In", Timestamp = timestamp };

        await client.SubmitPunchesAsync(new List<QueuedPunch> { punch });

        Assert.Equal("secret-key", handler.LastRequest!.Headers.GetValues("X-Station-Key").Single());

        // Pins the actual outgoing JSON shape (field names the backend expects), not just the HTTP
        // method — a regression that renamed/dropped a field previously would have gone unnoticed.
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var punches = json.RootElement.GetProperty("Punches");
        Assert.Equal(1, punches.GetArrayLength());
        var first = punches[0];
        Assert.Equal(punchId, first.GetProperty("Id").GetGuid());
        Assert.Equal(employeeId, first.GetProperty("EmployeeId").GetGuid());
        Assert.Equal("In", first.GetProperty("PunchType").GetString());
        Assert.Equal(timestamp, first.GetProperty("Timestamp").GetDateTimeOffset());
    }

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
}
