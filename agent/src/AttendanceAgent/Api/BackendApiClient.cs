using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AttendanceAgent.Api;

public class BackendApiClient : IBackendApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly AgentDbContext _db;
    private readonly ILogger<BackendApiClient> _logger;

    public BackendApiClient(HttpClient http, AgentDbContext db, ILogger<BackendApiClient> logger)
    {
        _http = http;
        _db = db;
        _logger = logger;
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

    public async Task<LoginResult?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        // Deliberately does NOT go through BuildRequestAsync — that helper always attaches
        // X-Station-Key, but /api/auth/login is the same public login endpoint the web portal
        // uses, not a station-scoped call. This is only ever used as a one-time admin-gate
        // check; the resulting JWT is not read from the response or stored anywhere.
        //
        // Catches network-level failures here (not just non-success HTTP responses) because the
        // design spec's documented contract is "returns null on any non-success response,
        // regardless of whether it was a 401 or a network failure" — MainViewModel.AdminLoginAsync
        // has no try/catch of its own, so an uncaught exception here previously crashed to a
        // modal MessageBox while leaving the admin-gated Enroll panel unlocked behind it.
        try
        {
            var settings = await _db.Settings.SingleAsync(ct);
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BackendBaseUrl), "/api/auth/login"))
            {
                Content = JsonContent.Create(new LoginRequestPayload(email, password), options: JsonOptions),
            };
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Admin login failed against the backend (network/transport failure).");
            return null;
        }
    }

    public async Task<bool> EnrollTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default)
    {
        try
        {
            var request = await BuildRequestAsync(HttpMethod.Post, "/api/templates", ct);
            request.Content = JsonContent.Create(
                new EnrollTemplateRequestPayload(employeeId, Convert.ToBase64String(templateData)),
                options: JsonOptions);
            var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Template upload for employee '{EmployeeId}' failed against the backend (network/transport failure).", employeeId);
            return false;
        }
    }

    public async Task<Guid?> GetStationTenantIdAsync(CancellationToken ct = default)
    {
        try
        {
            var request = await BuildRequestAsync(HttpMethod.Get, "/api/health/station", ct);
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<StationHealthResponsePayload>(JsonOptions, ct);
            return result?.TenantId;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Station tenant lookup failed against the backend (network/transport failure).");
            return null;
        }
    }
}
