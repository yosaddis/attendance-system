using System.Linq;
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
}
