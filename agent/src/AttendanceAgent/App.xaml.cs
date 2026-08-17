using System.IO;
using System.Windows;
using AttendanceAgent.Data;
using AttendanceAgent.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AttendanceAgent;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZakAttendanceAgent");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "agent.db");

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<AgentDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
                services.AddHttpClient<AttendanceAgent.Api.IBackendApiClient, AttendanceAgent.Api.BackendApiClient>();
                services.AddScoped<AttendanceAgent.Services.IEmployeeDirectoryService, AttendanceAgent.Services.EmployeeDirectoryService>();
                services.AddScoped<AttendanceAgent.Services.ITemplateCacheService, AttendanceAgent.Services.TemplateCacheService>();
                services.AddScoped<AttendanceAgent.Services.IPunchQueueService, AttendanceAgent.Services.PunchQueueService>();
                services.AddScoped<AttendanceAgent.Services.IPunchCaptureService, AttendanceAgent.Services.PunchCaptureService>();
                services.AddSingleton<AttendanceAgent.Devices.IFingerprintDevice, AttendanceAgent.Devices.FakeFingerprintDevice>();
                services.AddSingleton<AttendanceAgent.Devices.IFingerprintVerifier, AttendanceAgent.Devices.FakeFingerprintVerifier>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddHostedService<AttendanceAgent.Services.SyncBackgroundService>();
            })
            .Build();

        using (var scope = _host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
        }

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            if (!db.Settings.Any())
            {
                var backendUrl = Microsoft.VisualBasic.Interaction.InputBox(
                    "Backend base URL (e.g. https://attendance.example.com):", "First-run setup");
                var apiKey = Microsoft.VisualBasic.Interaction.InputBox(
                    "Station API key (from the admin portal's station creation screen):", "First-run setup");
                db.Settings.Add(new AgentSettings { BackendBaseUrl = backendUrl, StationApiKey = apiKey });
                db.SaveChanges();
            }
        }

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
