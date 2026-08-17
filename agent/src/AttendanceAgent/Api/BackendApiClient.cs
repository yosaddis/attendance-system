using System.Net;
using System.Net.Http;
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

    public async Task<PunchBatchSubmitResult> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default)
    {
        var payload = new PunchBatchPayload(punches
            .Select(p => new PunchPayload(p.Id, p.EmployeeId, p.PunchType, p.Timestamp))
            .ToList());
        var request = await BuildRequestAsync(HttpMethod.Post, "/api/punches/batch", ct);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode) return PunchBatchSubmitResult.Accepted;

        // The backend rejects the WHOLE batch (400) if any single punch in it is invalid — a
        // permanent, batch-level verdict retrying the same punches won't change. Everything else
        // (5xx, or any other non-2xx) is treated as transient and worth retrying as-is.
        if (response.StatusCode == HttpStatusCode.BadRequest) return PunchBatchSubmitResult.RejectedByBackend;

        return PunchBatchSubmitResult.TransientFailure;
    }
}
